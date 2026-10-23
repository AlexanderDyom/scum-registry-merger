// Scan and build of the shared cooking packages (see CookingMerge), and conflicts: packages several mods ship in
// different versions, where the game uses only the highest-priority one.
using System.Security.Cryptography;
using CUE4Parse.UE4.VirtualFileSystem;

namespace RegistryMerger;

public sealed partial class MergeEngine
{
    private static (byte[] Uasset, byte[] Uexp)? ReadPackage(IVfsReader v, string package)
    {
        var ua = FileOf(v, package + ".uasset");
        if (ua == null) return null;
        var ue = FileOf(v, package + ".uexp");
        return (PakEntryReader.Read(ua), ue != null ? PakEntryReader.Read(ue) : []);
    }

    /// <summary>The game's own version of a package (needs the AES key), or null.</summary>
    private (byte[] Uasset, byte[] Uexp)? GamePackage(string package)
    {
        foreach (var v in _all.Where(v => !IsModPak(v)).OrderByDescending(v => v, Priority))
            if (FileOf(v, package + ".uasset") != null) return ReadPackage(v, package);
        return null;
    }

    private static List<string> CookingTargetsIn(IVfsReader v) =>
        CookingMerge.Targets.Where(t => FileOf(v, t + ".uasset") != null).ToList();

    private static string CookingHash(IVfsReader v, IEnumerable<string> targets)
    {
        using var sha = SHA1.Create();
        foreach (var t in targets)
        {
            var (ua, ue) = ReadPackage(v, t)!.Value;
            var name = System.Text.Encoding.UTF8.GetBytes(t);
            sha.TransformBlock(name, 0, name.Length, null, 0);
            sha.TransformBlock(ua, 0, ua.Length, null, 0);
            sha.TransformBlock(ue, 0, ue.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>Cooking state of every mod, removed cooking mods, and conflicts (scan part).</summary>
    private void ScanCooking(ScanResult result, BuildManifest? manifest, IVfsReader? shared, string outputPak)
    {
        var paks = ModPaks(outputPak);
        var byFile = paks.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var info in result.Mods)
        {
            if (!byFile.TryGetValue(info.FileName, out var v)) continue;
            var targets = CookingTargetsIn(v);
            info.CookingPackages.AddRange(targets.Select(t => t[(t.LastIndexOf('/') + 1)..]));
            if (targets.Count == 0) { info.Cooking = CookingState.NotNeeded; continue; }
            try
            {
                info.CookingHash = CookingHash(v, targets);
            }
            catch (Exception ex)
            {
                info.Cooking = CookingState.Missing;
                info.Error ??= ex.Message;
                continue;
            }
            var entry = manifest?.CookingMods.FirstOrDefault(m => string.Equals(m.File, info.FileName, StringComparison.OrdinalIgnoreCase));
            var inShared = shared != null && targets.All(t => FileOf(shared, t + ".uasset") != null);
            info.Cooking = entry != null && entry.Hash == info.CookingHash && inShared ? CookingState.Included : CookingState.Missing;
            if (info.Cooking == CookingState.Missing && manifest != null)
            {
                if (entry == null) info.IsNew = true;
                else info.IsUpdated = true;
            }
        }

        if (manifest != null)
            foreach (var m in manifest.CookingMods)
                if (!result.Mods.Any(x => x.Cooking != CookingState.NotNeeded && string.Equals(x.FileName, m.File, StringComparison.OrdinalIgnoreCase))
                    && !result.RemovedMods.Contains(DisplayName(m.File)))
                    result.RemovedMods.Add(DisplayName(m.File));

        result.Conflicts.AddRange(ConflictsOf(paks));
        foreach (var c in result.Conflicts)
            foreach (var info in result.Mods.Where(m => c.Mods.Contains(m.Name)))
                info.Conflicts.Add(c);
    }

    /// <summary>Packages (and other files) that 2+ mods ship with different content, except the merged cooking ones.</summary>
    private static List<PackageConflict> ConflictsOf(List<IVfsReader> paks)
    {
        var owners = new Dictionary<string, List<IVfsReader>>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in paks) // lowest priority first
        {
            foreach (var key in v.Files.Keys)
            {
                var ext = Path.GetExtension(key).ToLowerInvariant();
                if (ext is ".uexp" or ".ubulk" or ".uptnl") continue; // counted with their .uasset
                if (key.Equals(RegistryPath, StringComparison.OrdinalIgnoreCase) || key.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase)) continue;
                var package = ext is ".uasset" or ".umap" ? key[..^ext.Length] : key;
                if (CookingMerge.KindOf(package) != null) continue;
                if (!owners.TryGetValue(package, out var list)) owners[package] = list = [];
                list.Add(v);
            }
        }

        var conflicts = new List<PackageConflict>();
        foreach (var (package, list) in owners.Where(o => o.Value.Count >= 2))
        {
            // the same file in several mods (e.g. one recipe shipped twice) is no conflict
            var versions = list.Select(v => FileHash(v, package)).Distinct(StringComparer.Ordinal).Count();
            if (versions < 2) continue;
            conflicts.Add(new PackageConflict(package, list.Select(v => DisplayName(v.Name)).ToList(), DisplayName(list[^1].Name)));
        }
        return conflicts.OrderBy(c => c.IsCooking ? 0 : 1).ThenBy(c => c.Package, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FileHash(IVfsReader v, string package)
    {
        using var sha = SHA1.Create();
        foreach (var ext in new[] { "", ".uasset", ".umap", ".uexp", ".ubulk" })
        {
            if (FileOf(v, package + ext) is not { } f) continue;
            var d = PakEntryReader.Read(f);
            sha.TransformBlock(d, 0, d.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>Merged cooking packages for the shared pak (build part); fills the manifest and the result.</summary>
    private List<(string Path, byte[] Data)> BuildCooking(string outputPak, BuildManifest manifest, BuildResult result, AssetRegistry? stockRegistry,
        IProgress<string>? log)
    {
        var paks = ModPaks(outputPak);
        var game = stockRegistry != null ? GameReference.FromRegistry(stockRegistry, "") : null;
        var current = new Dictionary<IVfsReader, bool>();
        bool Current(IVfsReader v)
        {
            if (current.TryGetValue(v, out var c)) return c;
            c = true; // without the game's registry, or without a registry of its own, it can't be told: trust it
            if (game != null && Registry(v) is { } rf)
            {
                try { c = CompareWithGame(AssetRegistry.Load(PakEntryReader.Read(rf)), ShipsIn(v), game) is (0, 0); }
                catch (Exception) { c = true; }
            }
            return current[v] = c;
        }

        var files = new List<(string Path, byte[] Data)>();
        foreach (var target in CookingMerge.Targets)
        {
            var shippers = paks.Where(v => FileOf(v, target + ".uasset") != null).ToList();
            if (shippers.Count == 0) continue;
            var asset = target[(target.LastIndexOf('/') + 1)..];
            log?.Report(asset);
            try
            {
                var mods = shippers.Select(v =>
                {
                    var (ua, ue) = ReadPackage(v, target)!.Value;
                    return new CookingMerge.ModPackage(DisplayName(v.Name), Current(v), ua, ue);
                }).ToList();
                var gamePkg = stockRegistry != null ? GamePackage(target) : null;
                var (uasset, uexp, merged, other) = CookingMerge.Merge(target, CookingMerge.KindOf(target)!.Value, mods, gamePkg);
                files.Add((target + ".uasset", uasset));
                files.Add((target + ".uexp", uexp));
                result.Cooking.Add(merged);
                if (other.Count > 0) result.CookingConflicts.Add(new PackageConflict(target, other, other[^1]));
            }
            catch (Exception ex)
            {
                // left out: the game then uses the highest-priority mod's version, as without the merger
                result.CookingErrors.Add($"{asset}: {ex.Message}");
            }
        }
        foreach (var v in paks)
        {
            var targets = CookingTargetsIn(v);
            if (targets.Count > 0) manifest.CookingMods.Add(new ManifestMod { File = v.Name, Hash = CookingHash(v, targets) });
        }
        return files;
    }
}
