namespace RegistryMerger;

/// <summary>
/// Merges mod registries into a base (the game's) registry: every base record stays as it is; a mod record whose object
/// path the merged registry does not have yet is added (the higher-priority mod's copy wins when two mods add the same
/// one). Depends nodes and package data are merged the same way (cooked SCUM registries carry none, but a mod's could).
/// </summary>
public sealed class Merger(AssetRegistry merged)
{
    private readonly Dictionary<string, int> _assets = Index(merged);
    private readonly Dictionary<string, int> _nodes = merged.Nodes.Select((n, i) => (merged.IdKey(n.Id), i))
        .ToDictionary(x => x.Item1, x => x.i, StringComparer.Ordinal);
    private readonly HashSet<string> _packages = merged.Packages.Select(p => merged.NameText(p.PackageName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public AssetRegistry Result => merged;

    private static Dictionary<string, int> Index(AssetRegistry r)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < r.Assets.Count; i++) d.TryAdd(r.NameText(r.Assets[i].ObjectPath), i);
        return d;
    }

    /// <summary>
    /// Adds what <paramref name="mod"/> has and the merged registry lacks. <paramref name="ships"/> filters records by
    /// package name (null = take all): a mod registry also carries the game's records of the game version the mod was
    /// built for, and only the records of packages the mod pak itself ships are really the mod's.
    /// Returns (records added, records replaced, records skipped because their package does not ship).
    /// </summary>
    public (int Added, int Replaced, int Skipped) Add(AssetRegistry mod, Func<string, bool>? ships, ISet<string> modOwned)
    {
        int added = 0, replaced = 0, skipped = 0;
        foreach (var a in mod.Assets)
        {
            var path = mod.NameText(a.ObjectPath);
            if (_assets.TryGetValue(path, out var at))
            {
                // a record another (lower-priority) mod added: the higher-priority mod's copy wins
                if (modOwned.Contains(path) && ships?.Invoke(mod.NameText(a.PackageName)) != false)
                {
                    var copy = merged.ImportAsset(mod, a);
                    merged.Assets.RemoveAt(merged.Assets.Count - 1);
                    merged.Assets[at] = copy;
                    replaced++;
                }
                continue;
            }
            if (ships != null && !ships(mod.NameText(a.PackageName))) { skipped++; continue; }
            merged.ImportAsset(mod, a);
            _assets[path] = merged.Assets.Count - 1;
            modOwned.Add(path);
            added++;
        }

        // depends nodes: new nodes are added (only those of shipped packages), edges of the mod graph are added where missing
        if (mod.Nodes.Count > 0)
        {
            var map = new int[mod.Nodes.Count];
            for (var i = 0; i < mod.Nodes.Count; i++)
            {
                var n = mod.Nodes[i];
                var key = mod.IdKey(n.Id);
                if (_nodes.TryGetValue(key, out var at)) { map[i] = at; continue; }
                var pkg = (n.Id.Bits & 1) != 0 ? mod.NameText(n.Id.PackageName) : null;
                if (ships != null && pkg != null && !ships(pkg)) { map[i] = -1; continue; }
                merged.Nodes.Add(new DependsNode { Id = merged.ImportId(mod, n.Id) });
                map[i] = _nodes[key] = merged.Nodes.Count - 1;
            }
            for (var i = 0; i < mod.Nodes.Count; i++)
            {
                if (map[i] < 0) continue;
                var src = mod.Nodes[i];
                var dst = merged.Nodes[map[i]];
                Union(dst.Package, src.Package, map);
                Union(dst.Name, src.Name, map);
                Union(dst.Manage, src.Manage, map);
                Union(dst.Referencers, src.Referencers, map);
            }
        }

        foreach (var p in mod.Packages)
        {
            var name = mod.NameText(p.PackageName);
            if (_packages.Contains(name) || (ships != null && !ships(name))) continue;
            merged.Packages.Add(new PackageRecord
            {
                PackageName = merged.Import(mod, p.PackageName), DiskSize = p.DiskSize, Guid = p.Guid, HashValid = p.HashValid, Hash = p.Hash,
            });
            _packages.Add(name);
        }

        return (added, replaced, skipped);
    }

    private static void Union(List<(int Node, uint Flags)> dst, List<(int Node, uint Flags)> src, int[] map)
    {
        var have = dst.Select(e => e.Node).ToHashSet();
        var changed = false;
        foreach (var (node, flags) in src)
        {
            var m = map[node];
            if (m >= 0 && have.Add(m)) { dst.Add((m, flags)); changed = true; }
        }
        if (changed) dst.Sort((x, y) => x.Node.CompareTo(y.Node));
    }
}
