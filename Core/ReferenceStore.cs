namespace RegistryMerger;

/// <summary>
/// Where game references live: a local cache (%APPDATA%\ScumRegistryMerger\reference) and the project's GitHub
/// repository (reference/&lt;fingerprint&gt;.ref), from where a missing one is downloaded once.
/// </summary>
public static class ReferenceStore
{
    public const string RemoteBase = "https://raw.githubusercontent.com/AlexanderDyom/scum-registry-merger/main/reference/";

    public static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRegistryMerger", "reference");

    private static string CachePath(string fingerprint) => Path.Combine(CacheDir, GameReference.FileName(fingerprint));

    public static GameReference? FromCache(string fingerprint)
    {
        try
        {
            var file = CachePath(fingerprint);
            return File.Exists(file) ? GameReference.Load(file) : null;
        }
        catch (Exception)
        {
            return null; // damaged cache file: download again
        }
    }

    public static void Save(GameReference reference)
    {
        Directory.CreateDirectory(CacheDir);
        reference.Save(CachePath(reference.Fingerprint));
    }

    /// <summary>From the cache, else from GitHub (then cached); null when there is none for this game version or offline.</summary>
    public static async Task<GameReference?> FindAsync(string fingerprint, bool download = true)
    {
        var cached = FromCache(fingerprint);
        if (cached != null || !download) return cached;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScumRegistryMerger");
            using var resp = await http.GetAsync(RemoteBase + GameReference.FileName(fingerprint)).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var reference = GameReference.Load(await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
            if (reference.Fingerprint != fingerprint) return null;
            Save(reference);
            return reference;
        }
        catch (Exception)
        {
            return null; // offline or not published yet
        }
    }
}
