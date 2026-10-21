namespace RegistryMerger;

/// <summary>
/// Game references on this computer: %APPDATA%\ScumRegistryMerger\reference\&lt;fingerprint&gt;.ref. Nothing is published
/// or downloaded: a player with the AES key makes the reference of their game version with
/// `ScumRegistryMerger.Cli make-reference`, and can copy the .ref file to other computers with the same game version.
/// </summary>
public static class ReferenceStore
{
    public static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRegistryMerger", "reference");

    private static string CachePath(string fingerprint) => Path.Combine(CacheDir, GameReference.FileName(fingerprint));

    /// <summary>The reference of this game version from the local folder, or null.</summary>
    public static GameReference? Find(string fingerprint)
    {
        try
        {
            var file = CachePath(fingerprint);
            if (!File.Exists(file)) return null;
            var reference = GameReference.Load(file);
            return reference.Fingerprint == fingerprint ? reference : null;
        }
        catch (Exception)
        {
            return null; // damaged file: as if there were none
        }
    }

    public static string Save(GameReference reference)
    {
        Directory.CreateDirectory(CacheDir);
        var file = CachePath(reference.Fingerprint);
        reference.Save(file);
        return file;
    }
}
