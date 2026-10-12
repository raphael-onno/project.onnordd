using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MediaspotIngest.Core.Configuration;

namespace MediaspotIngest.Core.Files;

/// <summary>
/// Uploads a file or folder with IBM Aspera's command-line client, the way Yan's tools did
/// (<c>ascp -d -l 150m "source" user@host:/dir</c>, password in ASPERA_SCP_PASS), plus resume and no-overwrite.
/// </summary>
public sealed partial class AsperaUploader(AsperaSettings settings)
{
    public static readonly string[] AscpCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "IBM", "Aspera Connect", "bin", "ascp.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Aspera", "Aspera Connect", "bin", "ascp.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IBM", "Aspera CLI", "sdk", "ascp.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Aspera", "Enterprise Server", "bin", "ascp.exe"),
    ];

    public string? AscpPath => !string.IsNullOrWhiteSpace(settings.AscpPath)
        ? settings.AscpPath
        : AscpCandidates.FirstOrDefault(File.Exists);

    /// <summary>Display form of the destination, e.g. <c>asp-…@host:/temp/file.mov</c>.</summary>
    public string Destination(string name) => $"{settings.Login}@{settings.Host}:{RemoteDirectory()}/{name}";

    /// <summary>The ascp arguments, without the password (passed through the environment).</summary>
    public IReadOnlyList<string> BuildArguments(string source, bool removeSource)
    {
        var args = new List<string>
        {
            "-d",                       // create the remote directory if needed
            "-k", "2",                  // resume an interrupted transfer (sparse checksum)
            "--overwrite=never",        // never replace a file already in temp
            "-l", string.IsNullOrWhiteSpace(settings.TargetRate) ? "500m" : settings.TargetRate.Trim(),
        };
        if (settings.SshPort is { } port)
            args.AddRange(["-P", port.ToString(CultureInfo.InvariantCulture)]);
        if (removeSource)
            args.AddRange(["--remove-after-transfer", "--remove-empty-directories"]);

        args.Add(source);
        args.Add($"{settings.Login}@{settings.Host}:{RemoteDirectory()}");
        return args;
    }

    public async Task UploadAsync(string source, bool removeSource, string password, IProgress<double>? progress, CancellationToken ct)
    {
        var ascp = AscpPath ?? throw new FileNotFoundException("ascp.exe introuvable : installer IBM Aspera Connect ou indiquer son chemin dans la configuration.");

        var start = new ProcessStartInfo(ascp)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in BuildArguments(source, removeSource))
            start.ArgumentList.Add(arg);
        start.Environment["ASPERA_SCP_PASS"] = password;

        using var process = Process.Start(start) ?? throw new IOException("Impossible de lancer ascp.");
        var errors = new StringBuilder();
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                errors.AppendLine(line);
        }, ct);
        var stdout = Task.Run(() => ReadProgress(process.StandardOutput, progress, errors, ct), ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // ascp resumes (-k 2) the next time: stopping it mid-file loses nothing.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(stderr, stdout);
        if (process.ExitCode != 0)
        {
            var message = errors.ToString().Trim();
            throw new IOException(string.IsNullOrEmpty(message) ? $"ascp a échoué (code {process.ExitCode})." : message);
        }

        progress?.Report(1);
    }

    /// <summary>Checks the client is installed and the server answers on its SSH port.</summary>
    public async Task<string> CheckAsync(CancellationToken ct = default)
    {
        var ascp = AscpPath ?? throw new FileNotFoundException("ascp.exe introuvable : installer IBM Aspera Connect.");
        if (string.IsNullOrWhiteSpace(settings.Host))
            throw new InvalidOperationException("Hôte Aspera non renseigné.");

        var port = settings.SshPort ?? 33001;
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await tcp.ConnectAsync(settings.Host.Trim(), port, timeout.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            throw new IOException($"Le serveur Aspera {settings.Host}:{port} ne répond pas ({e.Message}).");
        }

        return $"ascp trouvé ({ascp}) et serveur {settings.Host}:{port} joignable.";
    }

    private string RemoteDirectory()
    {
        var directory = (settings.RemoteDirectory ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        return directory.StartsWith('/') ? directory : "/" + directory;
    }

    /// <summary>ascp redraws one progress line with carriage returns: "file  45%  1.2GB  480Mb/s  02:10 ETA".</summary>
    private static async Task ReadProgress(StreamReader output, IProgress<double>? progress, StringBuilder errors, CancellationToken ct)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        int read;
        while ((read = await output.ReadAsync(buffer, ct)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is not ('\r' or '\n'))
                {
                    line.Append(c);
                    continue;
                }

                var text = line.ToString();
                line.Clear();
                var match = PercentRegex().Match(text);
                if (match.Success)
                    progress?.Report(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0);
                else if (text.Contains("error", StringComparison.OrdinalIgnoreCase))
                    errors.AppendLine(text.Trim());
            }
        }
    }

    [GeneratedRegex(@"\s(\d{1,3})%\s")]
    private static partial Regex PercentRegex();
}
