using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace RegistryMerger.Gui;

/// <summary>Checks the latest GitHub release and replaces the running exe with its .exe asset.</summary>
public static class Updater
{
    /// <summary>GitHub "owner/repo" whose releases carry ScumRegistryMerger.exe. Empty = update checks are off.</summary>
    public const string Repo = "AlexanderDyom/scum-registry-merger";

    public sealed record Release(Version Version, string Tag, string Notes, string? ExeUrl, string PageUrl, DateTime Published);

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public static bool Configured => Repo.Length > 0;

    private static HttpClient Client()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ScumRegistryMerger/" + CurrentText);
        return c;
    }

    /// <summary>The latest release, or null when none is configured or published.</summary>
    public static async Task<Release?> LatestAsync()
    {
        if (!Configured) return null;
        using var c = Client();
        using var resp = await c.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        string? exe = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            // the window app is ScumRegistryMerger.exe; the release may also carry ScumRegistryMerger.Cli.exe
            if (name.Equals("ScumRegistryMerger.exe", StringComparison.OrdinalIgnoreCase)) { exe = a.GetProperty("browser_download_url").GetString(); break; }
        }
        var published = root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var d) ? d : DateTime.MinValue;
        return new Release(v, tag, root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", exe,
            root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repo}/releases", published);
    }

    public static bool IsNewer(Release r) => r.Version > new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));

    /// <summary>Downloads the new exe next to the current one, swaps them and starts the new one. The caller then exits.</summary>
    public static async Task InstallAsync(Release r)
    {
        if (r.ExeUrl == null) throw new InvalidOperationException("The release has no .exe file.");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown program path.");
        var dir = Path.GetDirectoryName(exe)!;
        var fresh = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".new.exe");
        var old = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
        using (var c = Client())
        using (var s = await c.GetStreamAsync(r.ExeUrl))
        using (var f = File.Create(fresh))
            await s.CopyToAsync(f);
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        File.Move(fresh, exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Removes the previous exe left by an update.</summary>
    public static void CleanUp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            var old = Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception)
        {
            // still locked: next start
        }
    }
}
