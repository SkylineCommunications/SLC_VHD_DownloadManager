using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace SLC_DownloadManager;

public class DownloadManager
{
    private const int RetryDelayMs = 2000;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<int, SegmentStatus> _segmentStatus;
    private readonly bool _chaosMode;
    private readonly int _maxRetries;

    public DownloadManager(bool chaosMode = false, int maxRetries = 3)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SLC-DownloadManager/1.0");
        _segmentStatus = new Dictionary<int, SegmentStatus>();
        _chaosMode = chaosMode;
        _maxRetries = Math.Max(1, maxRetries);
    }

    public async Task<bool> DownloadAsync(string url, string outputPath, int threadCount, string? expectedHash = null, CancellationToken ct = default)
    {
        try
        {
            // Get file info
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Error: Server returned {response.StatusCode}[/]");
                return false;
            }

            if (!response.Content.Headers.ContentLength.HasValue)
            {
                AnsiConsole.MarkupLine("[red]Error: Server did not return Content-Length[/]");
                return false;
            }

            long fileSize = response.Content.Headers.ContentLength.Value;
            string tempDir = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", ".segments");
            
            // Clean up any existing temp directory from previous runs
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch { /* ignore cleanup errors */ }
            }
            Directory.CreateDirectory(tempDir);

            // Initialize segment status
            for (int i = 0; i < threadCount; i++)
            {
                _segmentStatus[i] = new SegmentStatus { Index = i, State = SegmentState.Pending };
            }

            // Build segment list
            long segmentSize = (long)Math.Ceiling((double)fileSize / threadCount);
            var segments = new List<SegmentInfo>();

            for (int i = 0; i < threadCount; i++)
            {
                long start = i * segmentSize;
                long end = i == threadCount - 1 ? fileSize - 1 : (i + 1) * segmentSize - 1;
                string segmentPath = Path.Combine(tempDir, $"segment_{i}");
                segments.Add(new SegmentInfo
                {
                    Index = i,
                    Start = start,
                    End = end,
                    Url = url,
                    LocalPath = segmentPath
                });
            }

            AnsiConsole.MarkupLine($"[yellow]Launching parallel download with {threadCount} threads...[/]");
            AnsiConsole.WriteLine();

            var downloadStartTime = DateTime.UtcNow;
            var tasks = segments.Select(s => DownloadSegmentAsync(s, ct)).ToList();

            // Monitor progress
            using var progressMonitor = new Timer(_ => UpdateProgress(fileSize, tempDir, threadCount), null, 500, 500);

            // Wait for all tasks
            await Task.WhenAll(tasks);

            // Final sync
            UpdateProgress(fileSize, tempDir, threadCount);

            // Check for failures
            var failed = _segmentStatus.Values.Where(s => s.State == SegmentState.Failed).ToList();
            if (failed.Any())
            {
                AnsiConsole.MarkupLine($"[red]Segments failed: {string.Join(", ", failed.Select(f => f.Index))}[/]");
                
                // Show details of failed segments
                foreach (var failedSegment in failed)
                {
                    string segmentPath = Path.Combine(tempDir, $"segment_{failedSegment.Index}");
                    bool fileExists = File.Exists(segmentPath);
                    AnsiConsole.MarkupLine($"[red]  Segment {failedSegment.Index}: {failedSegment.LastError} (file exists: {fileExists})[/]");
                }
                
                DisplayRetryRecommendation();
                return false;
            }

            AnsiConsole.MarkupLine("[green]All segments downloaded successfully[/]");
            AnsiConsole.WriteLine();

            // Verify all segment files exist before merging
            var missingSegments = new List<int>();
            for (int i = 0; i < threadCount; i++)
            {
                string segmentPath = Path.Combine(tempDir, $"segment_{i}");
                if (!File.Exists(segmentPath))
                {
                    missingSegments.Add(i);
                }
            }

            if (missingSegments.Any())
            {
                AnsiConsole.MarkupLine($"[red]Error: Segment files missing: {string.Join(", ", missingSegments)}[/]");
                AnsiConsole.MarkupLine("[red]This should not happen after successful status. Please retry the download.[/]");
                return false;
            }

            // Clear screen and show final download status before merge
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[cyan]Download Complete - Final Segment Status:[/]");
            AnsiConsole.MarkupLine(BuildHeatmapString());
            AnsiConsole.MarkupLine($"[green]100% | {fileSize / (1024.0 * 1024.0):F2} MB / {fileSize / (1024.0 * 1024.0):F2} MB[/]");
            AnsiConsole.WriteLine();

            // Merge segments
            AnsiConsole.MarkupLine("[yellow]Merging segments into final file...[/]");
            if (!await MergeSegmentsAsync(segments, outputPath, ct))
            {
                return false;
            }

            // Cleanup temp directory
            AnsiConsole.MarkupLine("[yellow]Cleaning up temporary files...[/]");
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { /* ignore cleanup errors */ }

            var elapsed = DateTime.UtcNow - downloadStartTime;
            AnsiConsole.MarkupLine($"[green]✓ Download completed in {elapsed.TotalSeconds:F1} seconds[/]");
            
            // Verify file integrity if hash was provided
            if (!string.IsNullOrEmpty(expectedHash))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Verifying file integrity...[/]");
                string actualHash = await CalculateFileHashAsync(outputPath, ct);
                
                bool hashMatches = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                
                if (hashMatches)
                {
                    AnsiConsole.MarkupLine("[green]FileIntegrity: Good ✓[/]");
                    AnsiConsole.MarkupLine($"[dim]SHA256: {actualHash}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]FileIntegrity: Bad ✗[/]");
                    AnsiConsole.MarkupLine($"[red]Expected: {expectedHash}[/]");
                    AnsiConsole.MarkupLine($"[red]Actual:   {actualHash}[/]");
                    return false;
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return false;
        }
    }

    private async Task DownloadSegmentAsync(SegmentInfo segment, CancellationToken ct)
    {
        int retryCount = 0;
        while (retryCount < _maxRetries)
        {
            try
            {
                // Chaos mode: fail specific segments for testing
                if (_chaosMode)
                {
                    // Segment 0: immediate failure that will retry
                    if (segment.Index == 0 && retryCount == 0)
                    {
                        throw new HttpRequestException($"[CHAOS] Simulated failure for segment {segment.Index}");
                    }
                    
                    // Segment 1: hangs indefinitely (timeout)
                    if (segment.Index == 1)
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5 second timeout per attempt
                        
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), timeoutCts.Token); // Simulate long-running operation
                        }
                        catch (OperationCanceledException)
                        {
                            throw new HttpRequestException($"[CHAOS] Segment {segment.Index} timeout");
                        }
                    }
                }

                var request = new HttpRequestMessage(HttpMethod.Get, segment.Url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(segment.Start, segment.End);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP {response.StatusCode}");
                }

                // Delete any partial file from previous retry attempt
                if (File.Exists(segment.LocalPath))
                {
                    File.Delete(segment.LocalPath);
                }

                using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = File.Create(segment.LocalPath))
                {
                    await contentStream.CopyToAsync(fileStream, 81920, ct);
                    fileStream.Flush();
                }

                // Validate file was created and has expected size
                if (!File.Exists(segment.LocalPath))
                {
                    throw new IOException($"Segment file {segment.Index} was not created");
                }

                long expectedSize = segment.End - segment.Start + 1;
                long actualSize = new FileInfo(segment.LocalPath).Length;
                if (actualSize != expectedSize)
                {
                    throw new IOException($"Segment {segment.Index} size mismatch: expected {expectedSize} bytes, got {actualSize} bytes");
                }

                _segmentStatus[segment.Index] = new SegmentStatus
                {
                    Index = segment.Index,
                    State = SegmentState.Success,
                    Retries = retryCount
                };
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _segmentStatus[segment.Index] = new SegmentStatus
                {
                    Index = segment.Index,
                    State = retryCount >= _maxRetries ? SegmentState.Failed : SegmentState.Retrying,
                    Retries = retryCount,
                    LastError = ex.Message
                };

                if (retryCount < _maxRetries)
                {
                    await Task.Delay(RetryDelayMs, ct);
                }
            }
        }
    }

    private void DisplayRetryRecommendation()
    {
        int proposedRetries = _maxRetries * 2;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Retry Recommendation:[/]");
        AnsiConsole.MarkupLine($"[yellow]  Current max retries:  [/][red]{_maxRetries}[/]");
        AnsiConsole.MarkupLine($"[yellow]  Proposed max retries: [/][green]{proposedRetries}[/][yellow] (doubled)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Try running with:[/]");
        AnsiConsole.MarkupLine($"[cyan]  dotnet run -- <url> <threads> <out> --retries={proposedRetries}[/]");
        AnsiConsole.WriteLine();
    }

    private void UpdateProgress(long totalSize, string tempDir, int threadCount)
    {
        // Calculate total downloaded from actual segment files
        long totalDownloaded = 0;
        
        if (Directory.Exists(tempDir))
        {
            for (int i = 0; i < threadCount; i++)
            {
                string segmentPath = Path.Combine(tempDir, $"segment_{i}");
                try
                {
                    if (File.Exists(segmentPath))
                    {
                        totalDownloaded += new FileInfo(segmentPath).Length;
                    }
                }
                catch (FileNotFoundException)
                {
                    // File was deleted between Exists check and Length access (retry in progress)
                    // Skip this segment for this progress update
                }
                catch (IOException)
                {
                    // File is locked or inaccessible, skip for now
                }
            }
        }

        // Cap total to file size (shouldn't exceed due to range requests, but just in case)
        totalDownloaded = Math.Min(totalDownloaded, totalSize);
        
        double percent = totalSize > 0 ? (double)totalDownloaded / totalSize * 100 : 0;
        double mbDownloaded = totalDownloaded / (1024.0 * 1024.0);
        double mbTotal = totalSize / (1024.0 * 1024.0);

        // Build heatmap + progress as a single string (no scrolling)
        var output = new System.Text.StringBuilder();
        output.AppendLine("Segment Status:");
        output.Append(BuildHeatmapString());
        output.AppendLine($"[cyan]Progress: {percent:F0}% | {mbDownloaded:F2} MB / {mbTotal:F2} MB[/]");

        // Clear previous output and redraw in place
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine(output.ToString().TrimEnd());
    }

    private string BuildHeatmapString()
    {
        const int columns = 16;
        var sb = new System.Text.StringBuilder();

        var rows = (int)Math.Ceiling((double)_segmentStatus.Count / columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int idx = r * columns + c;
                if (idx >= _segmentStatus.Count) break;

                var seg = _segmentStatus[idx];
                var color = seg.State switch
                {
                    SegmentState.Success => "green",
                    SegmentState.Retrying => "yellow",
                    SegmentState.Failed => "red",
                    _ => "gray"
                };

                string label = seg.Retries > 0 ? seg.Retries.ToString() : "0";
                sb.Append($"[{color}]{label:00}[/] ");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<bool> MergeSegmentsAsync(List<SegmentInfo> segments, string outputPath, CancellationToken ct)
    {
        try
        {
            string tempFile = outputPath + ".tmp";
            using (var outputStream = File.Create(tempFile))
            {
                int completed = 0;

                foreach (var segment in segments)
                {
                    if (!File.Exists(segment.LocalPath))
                    {
                        AnsiConsole.MarkupLine($"[red]Segment file not found: {segment.LocalPath}[/]");
                        return false;
                    }

                    using (var inputStream = File.OpenRead(segment.LocalPath))
                    {
                        await inputStream.CopyToAsync(outputStream, 1024 * 1024, ct);
                    }

                    completed++;
                    int percent = (int)(completed * 100.0 / segments.Count);
                    AnsiConsole.MarkupLine($"[yellow]Merging: {percent}%[/]");
                }
                
                outputStream.Flush();
            }

            // Ensure output stream is closed before moving file
            await Task.Delay(100, ct);

            if (File.Exists(outputPath)) 
            {
                File.Delete(outputPath);
            }
            
            File.Move(tempFile, outputPath, overwrite: true);

            AnsiConsole.MarkupLine($"[green]Successfully created: {outputPath}[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Merge failed: {ex.Message}[/]");
            string tempFile = outputPath + ".tmp";
            if (File.Exists(tempFile)) 
            {
                try { File.Delete(tempFile); } 
                catch { }
            }
            return false;
        }
    }

    private async Task<string> CalculateFileHashAsync(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        byte[] hashBytes = await sha256.ComputeHashAsync(fileStream, ct);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public enum SegmentState
{
    Pending,
    Retrying,
    Success,
    Failed
}

public class SegmentStatus
{
    public int Index { get; set; }
    public SegmentState State { get; set; }
    public int Retries { get; set; }
    public string? LastError { get; set; }
}

public class SegmentInfo
{
    public int Index { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public string Url { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}
