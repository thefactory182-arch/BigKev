using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PadPilot.Services;

public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size);

public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/thefactory182-arch/BigKev/releases/latest";
    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(LatestReleaseApi, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version) || version <= CurrentVersion) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), "BigKev.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            var size = asset.GetProperty("size").GetInt64();
            if (url is null || size is < 1_000_000 or > 250_000_000) continue;
            var uri = new Uri(url);
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) continue;
            return new UpdateInfo(version, url, size);
        }
        throw new InvalidDataException("The latest release does not contain a valid BigKev.exe asset.");
    }

    public static async Task DownloadAndRestartAsync(UpdateInfo update, CancellationToken token = default)
    {
        var currentExe = Environment.ProcessPath;
        if (currentExe is null || !Path.GetFileName(currentExe).Equals("BigKev.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic updates are available only in the packaged BigKev.exe release.");
        var directory = Path.Combine(Path.GetTempPath(), $"BigKev-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var downloaded = Path.Combine(directory, "BigKev-new.exe");
        var updater = Path.Combine(directory, "BigKev-updater.exe");
        using (var client = CreateClient())
        using (var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length is > 250_000_000 || (length.HasValue && length.Value != update.Size))
                throw new InvalidDataException("The downloaded update size does not match the GitHub release.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(downloaded, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token);
        }
        if (new FileInfo(downloaded).Length != update.Size)
            throw new InvalidDataException("The update download was incomplete.");
        File.Copy(currentExe, updater);
        var start = new ProcessStartInfo(updater) { UseShellExecute = true };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(downloaded);
        start.ArgumentList.Add(currentExe);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start);
        System.Windows.Application.Current.Shutdown();
    }

    public static bool HandleStartupArguments(string[] args)
    {
        if (args.Length >= 4 && args[0] == "--apply-update")
        {
            ApplyUpdate(args[1], args[2], int.Parse(args[3]));
            return true;
        }
        if (args.Length >= 3 && args[0] == "--cleanup-update")
        {
            _ = Task.Run(() => CleanupAsync(args[1], int.Parse(args[2])));
        }
        return false;
    }

    private static void ApplyUpdate(string downloaded, string target, int oldProcessId)
    {
        try
        {
            WaitForExit(oldProcessId);
            Exception? last = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { File.Copy(downloaded, target, true); last = null; break; }
                catch (IOException ex) { last = ex; Thread.Sleep(250); }
            }
            if (last is not null) throw last;
            var restart = new ProcessStartInfo(target) { UseShellExecute = true };
            restart.ArgumentList.Add("--cleanup-update");
            restart.ArgumentList.Add(Path.GetDirectoryName(downloaded)!);
            restart.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(restart);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"BigKev could not apply the update.\n\n{ex.Message}", "Update failed");
            if (File.Exists(target)) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }

    private static async Task CleanupAsync(string directory, int updaterProcessId)
    {
        await Task.Run(() => WaitForExit(updaterProcessId));
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void WaitForExit(int processId)
    {
        try { Process.GetProcessById(processId).WaitForExit(30_000); }
        catch (ArgumentException) { }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BigKev/{CurrentVersionText}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
