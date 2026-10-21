// What the game's own registry contains, without the game's files: a fingerprint of the installed game version and
// 64-bit hashes of every record's object path. Made once per game version by someone with the AES key
// (`ScumRegistryMerger.Cli make-reference`) and shared, so users without the key can still tell which mods were built
// for another game version. Only hashes are stored: no game file names or content.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RegistryMerger;

public sealed class GameReference
{
    private const string Magic = "SRMREF1";
    private readonly HashSet<ulong> _hashes;

    public string Fingerprint { get; }
    public int Count => _hashes.Count;

    private GameReference(string fingerprint, HashSet<ulong> hashes)
    {
        Fingerprint = fingerprint;
        _hashes = hashes;
    }

    public bool Contains(string objectPath) => _hashes.Contains(Hash(objectPath));
    public bool Contains(ulong hash) => _hashes.Contains(hash);
    public IEnumerable<ulong> Hashes => _hashes;

    /// <summary>FNV-1a 64 of the lower-cased object path (UTF-8).</summary>
    public static ulong Hash(string objectPath)
    {
        var h = 0xcbf29ce484222325UL;
        foreach (var b in Encoding.UTF8.GetBytes(objectPath.ToLowerInvariant()))
        {
            h ^= b;
            h *= 0x100000001b3UL;
        }
        return h;
    }

    /// <summary>
    /// The installed game version as seen without the AES key: SHA-1 of the names and sizes of the game's own paks in
    /// Content/Paks (not the mod paks, not subfolders). Client and server get different fingerprints.
    /// </summary>
    public static string GameFingerprint(string paks)
    {
        var lines = Directory.EnumerateFiles(paks, "*.pak", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_P", StringComparison.OrdinalIgnoreCase))
            .Select(f => $"{Path.GetFileName(f).ToLowerInvariant()}\t{new FileInfo(f).Length}")
            .OrderBy(l => l, StringComparer.Ordinal);
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static GameReference FromRegistry(AssetRegistry stock, string fingerprint) =>
        new(fingerprint, stock.Assets.Select(a => Hash(stock.NameText(a.ObjectPath))).ToHashSet());

    /// <summary>File name of the reference for a fingerprint.</summary>
    public static string FileName(string fingerprint) => fingerprint + ".ref";

    public void Save(string file)
    {
        using var fs = File.Create(file);
        using var gz = new GZipStream(fs, CompressionLevel.SmallestSize);
        using var w = new BinaryWriter(gz);
        w.Write(Encoding.ASCII.GetBytes(Magic + "\0"));
        w.Write(Fingerprint);
        w.Write(_hashes.Count);
        foreach (var h in _hashes.Order()) w.Write(h);
    }

    public static GameReference Load(string file) => Load(File.ReadAllBytes(file));

    public static GameReference Load(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length + 1)) != Magic + "\0") throw new FormatException("Not a game reference file.");
        var fingerprint = r.ReadString();
        var n = r.ReadInt32();
        if (n is < 0 or > 10_000_000) throw new FormatException("Bad game reference file.");
        var set = new HashSet<ulong>(n);
        for (var i = 0; i < n; i++) set.Add(r.ReadUInt64());
        return new GameReference(fingerprint, set);
    }
}
