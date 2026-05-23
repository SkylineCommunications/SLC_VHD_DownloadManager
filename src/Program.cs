using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace SLC_DownloadManager;

class Program
{
    private const string DefaultUrl = "http://ipv4.download.thinkbroadband.com/100MB.zip";
    private const string DefaultCatalogUrl = "https://softwaredownloads.dataminer.services/dataminer-virtual-disk/";

    static async Task Main(string[] args)
    {
        AnsiConsole.MarkupLine("[bold cyan]SLC VHD Download Manager[/]");
        AnsiConsole.WriteLine();

        CliOptions options = ParseArgs(args);

        using var cts = new CancellationTokenSource();
        using var metadataClient = new HttpClient();
        metadataClient.DefaultRequestHeaders.Add("User-Agent", "SLC-DownloadManager/1.0");

        var catalogService = new ImageCatalogService(metadataClient);
        var hashService = new HashResolutionService(metadataClient);

        DownloadManager? manager = null;

        try
        {
            if (options.ListImages || options.SelectImage)
            {
                var images = await catalogService.ListVhdxImagesAsync(options.CatalogUrl, options.CatalogPrefix, cts.Token);
                if (images.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No VHDX images found for the current catalog filter.[/]");
                    Environment.Exit(1);
                    return;
                }

                if (options.ListImages)
                {
                    RenderImageTable(images);
                    if (string.IsNullOrWhiteSpace(options.Url))
                    {
                        return;
                    }
                }

                if (options.SelectImage)
                {
                    options.Url = PromptForImageSelection(images);
                }
            }

            string url = string.IsNullOrWhiteSpace(options.Url) ? DefaultUrl : options.Url;
            string outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
                ? BuildDefaultOutputPath(url)
                : options.OutputPath;

            string? expectedHash = null;
            if (!options.NoHashVerify)
            {
                var resolution = await hashService.ResolveAsync(url, options.ExplicitHash, options.HashUrl, options.AutoHash, cts.Token);
                if (resolution != null)
                {
                    expectedHash = resolution.Hash;
                    AnsiConsole.MarkupLine($"[dim]Resolved SHA256 from: {resolution.Source}[/]");
                }
                else if (options.AutoHash)
                {
                    AnsiConsole.MarkupLine("[yellow]Hash auto-resolution failed; continuing without hash verification.[/]");
                }
            }

            AnsiConsole.MarkupLine($"[dim]URL: {url}[/]");
            AnsiConsole.MarkupLine($"[dim]Threads: {options.Threads}[/]");
            AnsiConsole.MarkupLine($"[dim]Output: {outputPath}[/]");
            AnsiConsole.MarkupLine($"[dim]Max Retries: {options.MaxRetries}[/]");
            if (options.ChaosMode) AnsiConsole.MarkupLine("[yellow]Chaos Mode: ENABLED[/]");
            if (!string.IsNullOrEmpty(expectedHash)) AnsiConsole.MarkupLine($"[dim]Expected Hash: {expectedHash}[/]");
            if (options.NoHashVerify) AnsiConsole.MarkupLine("[yellow]Hash verification disabled (--no-hash-verify).[/]");
            AnsiConsole.WriteLine();

            manager = new DownloadManager(options.ChaosMode, options.MaxRetries);
            bool success = await manager.DownloadAsync(url, outputPath, options.Threads, expectedHash, cts.Token);

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
            manager?.Dispose();
        }
    }

    private static CliOptions ParseArgs(string[] args)
    {
        var options = new CliOptions();
        var positional = new List<string>();

        foreach (string arg in args)
        {
            if (arg == "--chaos")
            {
                options.ChaosMode = true;
            }
            else if (arg == "--list-images")
            {
                options.ListImages = true;
            }
            else if (arg == "--select-image")
            {
                options.SelectImage = true;
            }
            else if (arg == "--no-hash-verify")
            {
                options.NoHashVerify = true;
            }
            else if (arg.StartsWith("--retries=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg.Substring(10), out int retries))
            {
                options.MaxRetries = retries;
            }
            else if (arg.StartsWith("--threads=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg.Substring(10), out int threads))
            {
                options.Threads = threads;
            }
            else if (arg.StartsWith("--hash=", StringComparison.OrdinalIgnoreCase))
            {
                string hashValue = arg.Substring(7).Trim();
                if (hashValue.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    options.AutoHash = true;
                }
                else
                {
                    options.ExplicitHash = hashValue;
                }
            }
            else if (arg.StartsWith("--hash-url=", StringComparison.OrdinalIgnoreCase))
            {
                options.HashUrl = arg.Substring(11).Trim();
            }
            else if (arg.StartsWith("--catalog-url=", StringComparison.OrdinalIgnoreCase))
            {
                options.CatalogUrl = arg.Substring(14).Trim();
            }
            else if (arg.StartsWith("--catalog-prefix=", StringComparison.OrdinalIgnoreCase))
            {
                options.CatalogPrefix = arg.Substring(17).Trim();
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (positional.Count > 0)
        {
            options.Url = positional[0];
        }
        if (positional.Count > 1 && int.TryParse(positional[1], out int positionalThreads))
        {
            options.Threads = positionalThreads;
        }
        if (positional.Count > 2)
        {
            options.OutputPath = positional[2];
        }

        options.Threads = Math.Max(1, options.Threads);
        options.MaxRetries = Math.Max(1, options.MaxRetries);
        return options;
    }

    private static void RenderImageTable(IReadOnlyList<CatalogImage> images)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[cyan]Available VHDX Images[/]");
        table.AddColumn("Name");
        table.AddColumn("Size (GB)");
        table.AddColumn("Last Modified (UTC)");

        foreach (var image in images)
        {
            string size = image.ContentLength.HasValue ? (image.ContentLength.Value / 1024d / 1024d / 1024d).ToString("F2") : "-";
            string modified = image.LastModified?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            table.AddRow(image.Name, size, modified);
        }

        AnsiConsole.Write(table);
    }

    private static string PromptForImageSelection(IReadOnlyList<CatalogImage> images)
    {
        var labelMap = images.ToDictionary(
            i => $"{i.Name} ({(i.ContentLength.HasValue ? (i.ContentLength.Value / 1024d / 1024d / 1024d).ToString("F2") : "-")} GB)",
            i => i.Url,
            StringComparer.OrdinalIgnoreCase);

        string selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Select a VHDX image[/]")
                .PageSize(10)
                .AddChoices(labelMap.Keys));

        return labelMap[selected];
    }

    private static string BuildDefaultOutputPath(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            string fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "downloaded_file.bin";
    }

    private sealed class CliOptions
    {
        public string? Url { get; set; }
        public int Threads { get; set; } = 8;
        public string? OutputPath { get; set; }
        public int MaxRetries { get; set; } = 3;
        public bool ChaosMode { get; set; }
        public bool ListImages { get; set; }
        public bool SelectImage { get; set; }
        public bool NoHashVerify { get; set; }
        public bool AutoHash { get; set; }
        public string? ExplicitHash { get; set; }
        public string? HashUrl { get; set; }
        public string CatalogUrl { get; set; } = DefaultCatalogUrl;
        public string CatalogPrefix { get; set; } = "10.";
    }
}
