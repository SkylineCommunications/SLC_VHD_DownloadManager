using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace SLC_DownloadManager;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.MarkupLine("[bold cyan]SLC VHD Download Manager[/]");
        AnsiConsole.WriteLine();

        // Parse command-line args
        string url = args.Length > 0 ? args[0] : "http://ipv4.download.thinkbroadband.com/100MB.zip";
        int threads = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 8;
        string outputPath = args.Length > 2 ? args[2] : "downloaded_file.bin";
        int maxRetries = 3;
        bool chaosMode = false;
        string? expectedHash = null;

        // Parse optional flags
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--chaos")
                chaosMode = true;
            else if (args[i].StartsWith("--retries=") && int.TryParse(args[i].Substring(10), out var r))
                maxRetries = r;
            else if (args[i].StartsWith("--hash="))
                expectedHash = args[i].Substring(7);
        }

        AnsiConsole.MarkupLine($"[dim]URL: {url}[/]");
        AnsiConsole.MarkupLine($"[dim]Threads: {threads}[/]");
        AnsiConsole.MarkupLine($"[dim]Output: {outputPath}[/]");
        AnsiConsole.MarkupLine($"[dim]Max Retries: {maxRetries}[/]");
        if (chaosMode) AnsiConsole.MarkupLine("[yellow]Chaos Mode: ENABLED[/]");
        if (expectedHash != null) AnsiConsole.MarkupLine($"[dim]Expected Hash: {expectedHash}[/]");
        AnsiConsole.WriteLine();

        using var cts = new CancellationTokenSource();
        var manager = new DownloadManager(chaosMode, maxRetries);

        try
        {
            bool success = await manager.DownloadAsync(url, outputPath, threads, expectedHash, cts.Token);

            if (success)
            {
                AnsiConsole.MarkupLine("[green bold]Download successful![/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red bold]Download failed[/]");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            Environment.Exit(1);
        }
        finally
        {
            manager.Dispose();
        }
    }
}
