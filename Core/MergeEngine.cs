// Scans a SCUM Paks folder (game registry, mod paks, the shared registry pak) and builds the shared registry pak.
// The shared pak carries a small manifest (SCUM/ModsRegistry.json) with the hash of every merged mod registry, so a later
// scan sees which mods were added, updated or removed since the last build.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;

namespace RegistryMerger;

public enum ModState
{
    /// <summary>The mod ships no registry, or its registry adds nothing to the game's.</summary>
    NotNeeded,
    /// <summary>Its new records are in the shared registry.</summary>
    Included,
    /// <summary>Its new records are not (all) in the shared registry: rebuild needed.</summary>
    Missing,
}

public sealed class ModInfo
{
    public required string FileName { get; init; }
    public required string PakPath { get; init; }
    public string Name { get; init; } = "";
    public bool HasRegistry { get; set; }
    public int NewRecords { get; set; }
    public ModState State { get; set; }
    /// <summary>Not in the shared registry's manifest (installed after the last build).</summary>
    public bool IsNew { get; set; }
    /// <summary>In the manifest, but its registry changed since the last build.</summary>
    public bool IsUpdated { get; set; }
    /// <summary>A few of its new asset names, for display.</summary>
    public List<string> Sample { get; } = [];
    public string? RegistryHash { get; set; }
}

public sealed class ScanResult
{
    public List<ModInfo> Mods { get; } = [];
    public string Paks { get; init; } = "";
    public string OutputPak { get; init; } = "";
    public bool SharedExists { get; set; }
    public int SharedRecords { get; set; }
    public DateTime? SharedBuiltAt { get; set; }
    /// <summary>The game's own registry could be read (AES key given and correct).</summary>
    public bool GameRegistryReadable { get; set; }
    public int EncryptedPaks { get; set; }
    /// <summary>Mods merged at the last build that are not installed any more.</summary>
    public List<string> RemovedMods { get; } = [];
    /// <summary>The game's registry changed since the last build (game update).</summary>
    public bool GameUpdated { get; set; }
    /// <summary>Last build used a mod's copy of the game records; the game's own registry is readable now.</summary>
    public bool BuiltWithoutKey { get; set; }

    public int MissingCount => Mods.Count(m => m.State == ModState.Missing);
    public bool NeedsBuild => MissingCount > 0 || RemovedMods.Count > 0 || GameUpdated || BuiltWithoutKey;
    public bool AnyRegistryMods => Mods.Any(m => m.State != ModState.NotNeeded);
}

public sealed class BuildManifest
{
    public int Version { get; set; } = 1;
    public string Tool { get; set; } = "ScumRegistryMerger";
    public DateTime BuiltAt { get; set; }
    /// <summary>"game" (the game's own registry) or "mod:&lt;pak&gt;" (a mod's copy of it).</summary>
    public string Base { get; set; } = "game";
    public string? BaseHash { get; set; }
    public int Records { get; set; }
    public List<ManifestMod> Mods { get; set; } = [];
}

public sealed class ManifestMod
{
    public string File { get; set; } = "";
    public string Hash { get; set; } = "";
    public int NewRecords { get; set; }
}

public sealed class BuildOptions
{
    /// <summary>Output pak (default: &lt;Paks&gt;/~mods/pakchunk999-ModsRegistry_P.pak).</summary>
    public string? OutputPak { get; set; }
    /// <summary>Only these mod paks (full paths), in this order; null = every mod pak with a registry, in load order.</summary>
    public List<string>? Mods { get; set; }
    /// <summary>Take only records of packages the mod pak ships.</summary>
    public bool ShipFilter { get; set; } = true;
    /// <summary>Also write the merged AssetRegistry.bin here.</summary>
    public string? RegistryOut { get; set; }
    /// <summary>Merge and check, write nothing (except RegistryOut).</summary>
    public bool DryRun { get; set; }
}

public sealed class BuildResult
{
    public int Records { get; set; }
    public string OutputPak { get; set; } = "";
    public string? SigCopiedFrom { get; set; }
    public bool BaseFromGame { get; set; }
    public List<(string Mod, int Added, int Replaced, int Skipped)> PerMod { get; } = [];
}

public sealed class MergeEngine : IDisposable
{
    public const string RegistryPath = "SCUM/AssetRegistry.bin";
    public const string ManifestPath = "SCUM/ModsRegistry.json";
    public const string OutputName = "pakchunk999-ModsRegistry_P.pak";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly DefaultFileProvider _provider;
    private readonly List<IVfsReader> _all;
    private bool _disposed;

    public string Paks { get; }
    public int EncryptedPaks { get; }
    public string DefaultOutputPak => Path.Combine(Paks, "~mods", OutputName);

    private MergeEngine(string paks, DefaultFileProvider provider)
    {
        Paks = paks;
        _provider = provider;
        _all = provider.MountedVfs.Cast<IVfsReader>().ToList();
        EncryptedPaks = provider.UnloadedVfs.Count;
    }

    /// <summary>Mounts every pak under <paramref name="paks"/>; encrypted game paks only with <paramref name="aesKey"/>.</summary>
    public static MergeEngine Open(string paks, string? aesKey)
    {
        paks = Path.GetFullPath(paks);
        var provider = new DefaultFileProvider(paks, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
        try
        {
            provider.Initialize();
            var key = NormalizeKey(aesKey);
            if (key != null) provider.SubmitKey(new FGuid(), new FAesKey(key));
            else provider.Mount(); // unencrypted paks (mods) only
        }
        catch
        {
            provider.Dispose();
            throw;
        }
        return new MergeEngine(paks, provider);
    }

    /// <summary>"0x" + 64 hex digits, or null for an empty key. Throws FormatException for anything else.</summary>
    public static string? NormalizeKey(string? key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return null;
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) key = key[2..];
        if (!Regex.IsMatch(key, "^[0-9a-fA-F]{64}$")) throw new FormatException("The AES key must be 64 hex digits (optionally starting with 0x).");
        return "0x" + key.ToUpperInvariant();
    }

    /// <summary>Does the key open the game's paks? (an encrypted game pak with a registry mounts and reads)</summary>
    public static bool TestKey(string paks, string key)
    {
        using var e = Open(paks, key);
        var stock = e.StockPak();
        if (stock == null) return false;
        try { return AssetRegistry.Load(Registry(stock)!.Read()).Assets.Count > 0; }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------ paks

    private static GameFile? FileOf(IVfsReader v, string path) =>
        v.Files.TryGetValue(path, out var f) ? f : v.Files.FirstOrDefault(x => string.Equals(x.Key, path, StringComparison.OrdinalIgnoreCase)).Value;

    private static GameFile? Registry(IVfsReader v) => FileOf(v, RegistryPath);

    private static bool IsModPak(IVfsReader v) => Path.GetFileNameWithoutExtension(v.Name).EndsWith("_P", StringComparison.OrdinalIgnoreCase);

    /// <summary>A pak holding nothing but a registry (and our manifest): a merged registry, never a mod.</summary>
    private static bool IsRegistryOnly(IVfsReader v) =>
        v.Files.Count > 0 && v.Files.Keys.All(k => k.Equals(RegistryPath, StringComparison.OrdinalIgnoreCase) || k.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase));

    private IVfsReader? StockPak() => _all.Where(v => !IsModPak(v) && Registry(v) != null).OrderBy(v => ChunkOrder(v.Name)).LastOrDefault();

    private IVfsReader? PakAt(string path) =>
        _all.FirstOrDefault(v => string.Equals(Path.GetFullPath(v.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    private List<IVfsReader> ModPaks(string outputPak) => _all
        .Where(v => IsModPak(v) && !IsRegistryOnly(v) && !string.Equals(Path.GetFullPath(v.Path), Path.GetFullPath(outputPak), StringComparison.OrdinalIgnoreCase))
        .OrderBy(v => ChunkOrder(v.Name)).ToList();

    /// <summary>Load order key: pakchunk number, then name.</summary>
    public static (int, string) ChunkOrder(string name)
    {
        var m = Regex.Match(name, @"pakchunk(\d+)", RegexOptions.IgnoreCase);
        return (m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : -1, name.ToLowerInvariant());
    }

    /// <summary>Display name: pakchunk901-FoodMod_P.pak -> FoodMod.</summary>
    public static string DisplayName(string pakName)
    {
        var n = Path.GetFileNameWithoutExtension(pakName);
        n = Regex.Replace(n, @"^pakchunk\d+(_s\d+)?-", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"_P$", "", RegexOptions.IgnoreCase);
        return n.Length > 0 ? n : pakName;
    }

    /// <summary>Package name -> is it a .uasset/.umap of this pak (/Game/X -> SCUM/Content/X; other roots by the path after the root).</summary>
    private static Func<string, bool> ShipsIn(IVfsReader v)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in v.Files.Keys)
        {
            var ext = Path.GetExtension(f);
            if (!ext.Equals(".uasset", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".umap", StringComparison.OrdinalIgnoreCase)) continue;
            var p = f[..^ext.Length];
            files.Add(p);
            var c = p.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
            if (c >= 0) tails.Add(p[(c + 9)..]);
        }
        return pkg =>
        {
            if (pkg.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)) return files.Contains("SCUM/Content/" + pkg[6..]);
            var s = pkg.IndexOf('/', 1);
            return s > 0 && tails.Contains(pkg[(s + 1)..]);
        };
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA1.HashData(data));

    // ------------------------------------------------------------------ scan

    /// <summary>State of every mod pak against the game's registry and the shared registry pak.</summary>
    public ScanResult Scan(string? outputPak = null, IProgress<string>? log = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        outputPak = Path.GetFullPath(outputPak ?? DefaultOutputPak);
        var result = new ScanResult { Paks = Paks, OutputPak = outputPak, EncryptedPaks = EncryptedPaks };

        // the shared registry pak (from an earlier build)
        BuildManifest? manifest = null;
        HashSet<string>? sharedPaths = null;
        var shared = PakAt(outputPak);
        if (shared != null && Registry(shared) is { } sharedReg)
        {
            log?.Report("shared registry");
            var reg = AssetRegistry.Load(sharedReg.Read());
            sharedPaths = reg.Assets.Select(a => reg.NameText(a.ObjectPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.SharedExists = true;
            result.SharedRecords = reg.Assets.Count;
            result.SharedBuiltAt = File.GetLastWriteTime(outputPak);
            if (FileOf(shared, ManifestPath) is { } mf)
            {
                try { manifest = JsonSerializer.Deserialize<BuildManifest>(mf.Read()); }
                catch (JsonException) { manifest = null; }
                if (manifest != null) result.SharedBuiltAt = manifest.BuiltAt.ToLocalTime();
            }
        }

        // the game's registry
        HashSet<string>? stockPaths = null;
        var stock = StockPak();
        if (stock != null)
        {
            log?.Report("game registry");
            var bytes = Registry(stock)!.Read();
            var reg = AssetRegistry.Load(bytes);
            stockPaths = reg.Assets.Select(a => reg.NameText(a.ObjectPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.GameRegistryReadable = true;
            if (manifest is { Base: "game", BaseHash: not null } && manifest.BaseHash != Hash(bytes)) result.GameUpdated = true;
            if (manifest != null && manifest.Base.StartsWith("mod:", StringComparison.Ordinal)) result.BuiltWithoutKey = true;
        }

        // mods: shipped records of each, and (without the game registry) the records each carries as game records
        var shippedByMod = new Dictionary<ModInfo, List<(string Path, string Asset)>>();
        var carriedAsGame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in ModPaks(outputPak))
        {
            var info = new ModInfo { FileName = v.Name, PakPath = v.Path, Name = DisplayName(v.Name) };
            result.Mods.Add(info);
            if (Registry(v) is not { } rf) continue;
            log?.Report(v.Name);
            var bytes = rf.Read();
            info.HasRegistry = true;
            info.RegistryHash = Hash(bytes);
            var reg = AssetRegistry.Load(bytes);
            var ships = ShipsIn(v);
            var shipped = new List<(string, string)>();
            foreach (var a in reg.Assets)
            {
                var path = reg.NameText(a.ObjectPath);
                if (ships(reg.NameText(a.PackageName))) shipped.Add((path, reg.NameText(a.AssetName)));
                else if (stockPaths == null) carriedAsGame[path] = carriedAsGame.GetValueOrDefault(path) + 1;
            }
            shippedByMod[info] = shipped;
        }

        foreach (var info in result.Mods)
        {
            if (!info.HasRegistry) { info.State = ModState.NotNeeded; continue; }
            var own = shippedByMod[info].Where(r => stockPaths != null ? !stockPaths.Contains(r.Path) : !carriedAsGame.ContainsKey(r.Path)).ToList();
            info.NewRecords = own.Count;
            info.Sample.AddRange(own.Select(r => r.Asset).Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
            if (own.Count == 0) { info.State = ModState.NotNeeded; continue; }

            var entry = manifest?.Mods.FirstOrDefault(m => string.Equals(m.File, info.FileName, StringComparison.OrdinalIgnoreCase));
            if (manifest != null)
            {
                info.IsNew = entry == null;
                info.IsUpdated = entry != null && entry.Hash != info.RegistryHash;
            }
            var inShared = sharedPaths != null && own.All(r => sharedPaths.Contains(r.Path));
            info.State = inShared && !info.IsNew && !info.IsUpdated ? ModState.Included : ModState.Missing;
            if (sharedPaths == null) info.IsNew = true;
        }

        if (manifest != null)
        {
            foreach (var m in manifest.Mods.Where(m => m.NewRecords > 0))
                if (!result.Mods.Any(x => x.State != ModState.NotNeeded && string.Equals(x.FileName, m.File, StringComparison.OrdinalIgnoreCase)))
                    result.RemovedMods.Add(DisplayName(m.File));
        }

        // missing first, then load order
        var order = result.Mods.Select((m, i) => (m, i)).OrderBy(x => x.m.State == ModState.Missing ? 0 : 1).ThenBy(x => x.i).Select(x => x.m).ToList();
        result.Mods.Clear();
        result.Mods.AddRange(order);
        return result;
    }

    // ------------------------------------------------------------------ build

    /// <summary>
    /// Merges the game's registry (or, without the AES key, the lowest mod's copy of it) with every mod registry and writes
    /// the shared pak (+ .sig). The engine is disposed afterwards (the old shared pak must be closed before it is replaced).
    /// </summary>
    public BuildResult Build(BuildOptions? options = null, IProgress<string>? log = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new BuildOptions();
        var outputPak = Path.GetFullPath(options.OutputPak ?? DefaultOutputPak);
        var result = new BuildResult { OutputPak = outputPak };

        List<IVfsReader> mods;
        if (options.Mods is { Count: > 0 })
        {
            mods = [];
            foreach (var m in options.Mods)
            {
                var v = PakAt(m) ?? throw new InvalidOperationException($"{m}: not a readable pak under {Paks}.");
                if (Registry(v) == null) throw new InvalidOperationException($"{m}: has no {RegistryPath}.");
                mods.Add(v);
            }
        }
        else
        {
            mods = ModPaks(outputPak).Where(v => Registry(v) != null).ToList();
        }
        if (mods.Count == 0) throw new InvalidOperationException("No mod pak with an AssetRegistry.bin found.");

        var manifest = new BuildManifest { BuiltAt = DateTime.UtcNow };
        AssetRegistry merged;
        var first = 0;
        var stock = StockPak();
        if (stock != null)
        {
            log?.Report($"base: game registry ({stock.Name})");
            var bytes = Registry(stock)!.Read();
            merged = AssetRegistry.Load(bytes);
            manifest.BaseHash = Hash(bytes);
            result.BaseFromGame = true;
        }
        else
        {
            log?.Report($"base: {mods[0].Name} (no AES key: the game's records come from this mod's copy)");
            var bytes = Registry(mods[0])!.Read();
            merged = AssetRegistry.Load(bytes);
            manifest.Base = "mod:" + mods[0].Name;
            manifest.BaseHash = Hash(bytes);
            manifest.Mods.Add(new ManifestMod { File = mods[0].Name, Hash = Hash(bytes) });
            first = 1;
        }

        var merger = new Merger(merged);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in mods.Skip(first))
        {
            log?.Report(v.Name);
            var bytes = Registry(v)!.Read();
            var reg = AssetRegistry.Load(bytes);
            var (added, replaced, skipped) = merger.Add(reg, options.ShipFilter ? ShipsIn(v) : null, owned);
            result.PerMod.Add((v.Name, added, replaced, skipped));
            manifest.Mods.Add(new ManifestMod { File = v.Name, Hash = Hash(bytes), NewRecords = added + replaced });
        }

        var outBytes = merged.Save();
        if (!AssetRegistry.Load(outBytes).Save().AsSpan().SequenceEqual(outBytes))
            throw new InvalidOperationException("The merged registry does not read back identically; nothing was written.");
        result.Records = merged.Assets.Count;
        manifest.Records = merged.Assets.Count;

        // the lowest mod in no-key mode: its own new records (shipped, not carried by other mods as game records) count too
        if (first == 1 && options.ShipFilter)
        {
            var scan = Scan(outputPak);
            var lowest = scan.Mods.FirstOrDefault(m => m.FileName == mods[0].Name);
            manifest.Mods[0].NewRecords = lowest?.NewRecords ?? 0;
        }

        if (options.RegistryOut != null) File.WriteAllBytes(options.RegistryOut, outBytes);
        Dispose(); // close every pak, including the old shared one
        if (options.DryRun) return result;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPak)!);
        var tmp = outputPak + ".tmp";
        PakWriter.Write(tmp, [(RegistryPath, outBytes), (ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, Json))]);
        File.Move(tmp, outputPak, true);
        // any game .sig works (mods load with signature checks off); take the smallest
        var sig = Directory.EnumerateFiles(Paks, "*.sig").Where(f => !Path.GetFileName(f).Contains("_P.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => new FileInfo(f).Length).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (sig != null)
        {
            File.Copy(sig, Path.ChangeExtension(outputPak, ".sig"), true);
            result.SigCopiedFrom = Path.GetFileName(sig);
        }
        return result;
    }

    /// <summary>Removes the shared registry pak and its .sig (the mods stay).</summary>
    public static void DeleteShared(string outputPak)
    {
        if (File.Exists(outputPak)) File.Delete(outputPak);
        var sig = Path.ChangeExtension(outputPak, ".sig");
        if (File.Exists(sig)) File.Delete(sig);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.Dispose();
    }
}
