using System.Runtime.CompilerServices;
using EpicManifestParser.Objects;
using Serilog;
using Spectre.Console;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.Console()
    .CreateLogger();

var manifestFileorURL = AnsiConsole.Ask<string>("Path or URL to [dodgerblue2].manifest[/] file:").Replace("\"", "");

byte[] manifestData;
if (manifestFileorURL.Contains("://"))
{
    using var webClient = new HttpClient();
    manifestData = await webClient.GetByteArrayAsync(manifestFileorURL);
}
else
{
    manifestData = await File.ReadAllBytesAsync(manifestFileorURL);
}

AnsiConsole.MarkupLine($"Retrieved file of size [aqua]{manifestData.Length} bytes[/]");

var isContent = AnsiConsole.Confirm("Is this manifest a content build?");

var cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManifestDownloader", "cache", "chunks");
var cache = AnsiConsole.Confirm(@"Cache files? ([aqua]Will be cached in %appdata%\ManifestDownloader\cache[/])");
var app = isContent ? "Fortnite/Content" : "Fortnite";

var manifestOptions = new ManifestOptions
{
    ChunkBaseUri = new Uri($"https://epicgames-download1.akamaized.net/Builds/{app}/CloudDir/", UriKind.Absolute)
};

if (cache)
{
    if (!Path.Exists(cachePath)) Directory.CreateDirectory(cachePath);
    manifestOptions.ChunkCacheDirectory = new DirectoryInfo(cachePath);
}

var manifest = new Manifest(manifestData, manifestOptions);

AnsiConsole.WriteLine("Manifest Information:\n" +
                      $"App Name - {manifest.AppName}\n" +
                      $"Version - {manifest.BuildVersion}\n" +
                      $"Launch Command - {manifest.LaunchCommand}");

var outputFolder = AnsiConsole.Ask<string>("Output folder: ").Replace("\"", "");

await AnsiConsole.Progress()
    .Columns(
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn(),
        new TransferSpeedColumn()
    )
    .HideCompleted(true)
    .StartAsync(progress =>
    {
        return Parallel.ForEachAsync(manifest.FileManifests, async (file, _) =>
        {
            var task = progress.AddTask(file.Name, false);

            await Download(task, file, outputFolder);
        });
    });

AnsiConsole.Markup("[green]Finished![/]");

return;

async Task Download(ProgressTask task, FileManifest file, string output)
{
    const int bufferSize = 8192; // Adjust buffer size as needed

    try
    {
        var outputName = Path.Combine(output, file.Name).Replace('/', '\\');
        var outputPath = SubstringBeforeLast(outputName, '\\');

        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

        await using var stream = file.GetStream();
        task.MaxValue(stream.Length);
        task.StartTask();

        await using var fileStream = new FileStream(outputName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                AnsiConsole.MarkupLine($"Download of [u]{file.Name}[/] [green]completed![/]");
                break;
            }

            task.Increment(read);
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
        }
    }
    catch (Exception ex)
    {
        // An error occured
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex}");
    }
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static string SubstringBeforeLast(string s, char delimiter)
{
    var index = s.LastIndexOf(delimiter);
    return index == -1 ? s : s[..index];
}