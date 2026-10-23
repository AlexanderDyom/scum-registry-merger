// Shared cooking files: mods that add cooking recipes ship the game's recipe list (CookingRecipeRegistry), the recipes
// known by default (CookingSkill) and cookbooks (Book_Cooking_*) as "game + mine". The game reads each of these packages
// from one pak only, the one with the highest priority, so the other mods' new recipes vanish. Here they are merged:
// game list + every mod's additions - every removal (removals only from mods built for the installed game version,
// and only when the game's own files can be read with the AES key). Packages are read and written with ScumModKit.
using System.Security.Cryptography;
using ScumModKit.Package;
using ScumModKit.Properties;

namespace RegistryMerger;

public enum CookingKind
{
    /// <summary>CookingRecipeRegistry._entries: {Enabled, AssetId}. A recipe missing there doesn't exist in game.</summary>
    RecipeList,
    /// <summary>CookingSkill._recipesKnownByDefault.</summary>
    KnownByDefault,
    /// <summary>Book_Cooking_* (the game's own cookbooks): _recipes.</summary>
    Cookbook,
}

/// <summary>One entry of a recipe list: the recipe's FName (text without number + number) and, for the recipe list, Enabled.</summary>
public readonly record struct RecipeEntry(string Text, int Number, bool Enabled)
{
    public string Key => Number == 0 ? Text : $"{Text}_{Number - 1}";
}

/// <summary>A package that several mods ship in different versions and that can't be merged: the game uses one of them.</summary>
/// <param name="Package">Path in the pak without extension, e.g. SCUM/Content/ConZ_Files/Cooking/Recipes/Cook_Dish_Cake_Pancake.</param>
/// <param name="Mods">Display names of the mods, lowest priority first.</param>
/// <param name="Winner">The mod whose version the game uses (highest priority).</param>
public sealed record PackageConflict(string Package, IReadOnlyList<string> Mods, string Winner)
{
    public string AssetName => Package[(Package.LastIndexOf('/') + 1)..];
    public bool IsCooking => Package.Contains("/Cooking/", StringComparison.OrdinalIgnoreCase) || CookingMerge.KindOf(Package) != null;
}

/// <summary>What a cooking package became in the shared pak.</summary>
public sealed record CookingResult(string Package, int Entries, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

public static class CookingMerge
{
    public const string RecipeList = "SCUM/Content/ConZ_Files/Cooking/Data/CookingRecipeRegistry";
    public const string KnownByDefault = "SCUM/Content/ConZ_Files/Skills/CookingSkill";
    private const string BookFolder = "SCUM/Content/ConZ_Files/Items/Books/Cookbooks/";

    /// <summary>The game's own cookbooks (a mod's own books are unique packages and need no merge).</summary>
    public static readonly string[] GameBooks = ["BBQ", "Cake", "Casseroles", "Drinks", "Pasta", "Pizza", "Rice", "Soup", "Stew"];

    /// <summary>Every shared cooking package, as paths in the pak without extension.</summary>
    public static IEnumerable<string> Targets =>
        new[] { RecipeList, KnownByDefault }.Concat(GameBooks.Select(b => BookFolder + "Book_Cooking_" + b));

    public static CookingKind? KindOf(string package) =>
        string.Equals(package, RecipeList, StringComparison.OrdinalIgnoreCase) ? CookingKind.RecipeList
        : string.Equals(package, KnownByDefault, StringComparison.OrdinalIgnoreCase) ? CookingKind.KnownByDefault
        : GameBooks.Any(b => string.Equals(package, BookFolder + "Book_Cooking_" + b, StringComparison.OrdinalIgnoreCase)) ? CookingKind.Cookbook
        : null;

    /// <summary>(export, array property) holding the list.</summary>
    private static (string Export, string Property) ListOf(CookedPackage pkg, CookingKind kind) => kind switch
    {
        CookingKind.RecipeList => ("CookingRecipeRegistry", "_entries"),
        CookingKind.KnownByDefault => ("CookingSkill", "_recipesKnownByDefault"),
        _ => (pkg.Exports.Select(e => pkg.NameText(e.ObjectName)).First(n => n.StartsWith("CookingRecipeCollectionItemTag", StringComparison.Ordinal)), "_recipes"),
    };

    private static Property AssetName(CookedPackage pkg, CookingKind kind, PropertyList element) =>
        kind == CookingKind.RecipeList ? element.Get("AssetId").OpenStruct(pkg).Get("PrimaryAssetName") : element.Get("PrimaryAssetName");

    public static List<RecipeEntry> ReadList(CookedPackage pkg, CookingKind kind)
    {
        var (export, prop) = ListOf(pkg, kind);
        var p = ExportProperties.Read(pkg, export).Properties.Find(prop);
        if (p == null) return [];
        return p.OpenArray(pkg).Elements.Select(e =>
        {
            var n = AssetName(pkg, kind, e).NameValue;
            // a cooked bool that is false may be left out of the tags
            var enabled = kind != CookingKind.RecipeList || e.Find("Enabled") is { } en && en.Bool;
            return new RecipeEntry(pkg.Names[n.Index].Text, n.Number, enabled);
        }).ToList();
    }

    /// <summary>Replaces the list with <paramref name="entries"/> (elements are copies of the first one, names added to the name map).</summary>
    public static void WriteList(CookedPackage pkg, CookingKind kind, IReadOnlyList<RecipeEntry> entries)
    {
        var (export, prop) = ListOf(pkg, kind);
        var ep = ExportProperties.Read(pkg, export);
        var arr = (ep.Properties.Find(prop) ?? throw new InvalidOperationException($"{export} has no {prop}.")).OpenArray(pkg);
        var template = arr.Elements.FirstOrDefault() ?? throw new InvalidOperationException($"{export}.{prop} is empty: nothing to copy an entry from.");
        PackageEdits.RenameNames(pkg, add: entries.Select(e => e.Text), optional: true);
        var elements = new List<PropertyList>(entries.Count);
        foreach (var entry in entries)
        {
            var e = template.Clone();
            AssetName(pkg, kind, e).NameValue = new FName(pkg.FindName(entry.Text), entry.Number);
            if (kind == CookingKind.RecipeList && e.Find("Enabled") is { } en) en.Bool = entry.Enabled;
            elements.Add(e);
        }
        arr.Elements = elements;
        ep.Save();
    }

    /// <summary>One mod's version of a shared cooking package.</summary>
    public sealed record ModPackage(string Mod, bool CurrentGameVersion, byte[] Uasset, byte[] Uexp);

    /// <summary>
    /// Merges the versions of one package. <paramref name="mods"/> lowest priority first; <paramref name="game"/> is the
    /// game's own version (null without the AES key: then only additions are merged). The highest-priority mod's package
    /// is the base of the result (its other settings win). Returns the package bytes, what changed, and the mods whose
    /// other settings (not the list) differ from the game when 2+ mods change them.
    /// </summary>
    public static (byte[] Uasset, byte[] Uexp, CookingResult Result, IReadOnlyList<string> OtherConflict) Merge(
        string package, CookingKind kind, IReadOnlyList<ModPackage> mods, (byte[] Uasset, byte[] Uexp)? game)
    {
        var lists = mods.Select(m => (Mod: m, List: ReadList(CookedPackage.Load(m.Uasset, m.Uexp), kind))).ToList();
        List<RecipeEntry> result;
        var added = new List<string>();
        var removed = new HashSet<string>(StringComparer.Ordinal);
        if (game != null)
        {
            var gamePkg = CookedPackage.Load(game.Value.Uasset, game.Value.Uexp);
            var gameList = ReadList(gamePkg, kind);
            var gameKeys = gameList.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            result = gameList.ToList();
            foreach (var (mod, list) in lists)
            {
                var keys = list.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
                if (mod.CurrentGameVersion)
                {
                    // removals and Enabled changes only from mods built for this game version: an older mod simply
                    // lacks the recipes added since, that's no removal
                    foreach (var g in gameList.Where(g => !keys.Contains(g.Key))) removed.Add(g.Key);
                    foreach (var e in list.Where(e => gameKeys.Contains(e.Key)))
                    {
                        var i = result.FindIndex(r => r.Key == e.Key);
                        if (i >= 0 && result[i].Enabled != e.Enabled)
                        {
                            result[i] = result[i] with { Enabled = e.Enabled };
                            if (!e.Enabled) removed.Add(e.Key);
                        }
                    }
                }
                foreach (var e in list.Where(e => !gameKeys.Contains(e.Key) && !result.Any(r => r.Key == e.Key)))
                {
                    result.Add(e);
                    added.Add(e.Key);
                }
            }
            result.RemoveAll(r => removed.Contains(r.Key) && r.Enabled);
        }
        else
        {
            // no game list: union of the mods' lists (each is "game + mine"), the higher priority decides Enabled
            result = [];
            foreach (var (_, list) in lists)
                foreach (var e in list)
                {
                    var i = result.FindIndex(r => r.Key == e.Key);
                    if (i < 0) result.Add(e);
                    else result[i] = result[i] with { Enabled = e.Enabled };
                }
        }

        var top = mods[^1];
        var basePkg = CookedPackage.Load(top.Uasset, top.Uexp);
        WriteList(basePkg, kind, result);
        var (ua, ue) = basePkg.Save();
        var others = game != null ? OtherChanges(kind, mods, game.Value) : [];
        return (ua, ue, new CookingResult(package, result.Count, added, removed.ToList()), others.Count >= 2 ? others : []);
    }

    /// <summary>Mods that change something else than the list in this package (compared with the game's version).</summary>
    private static List<string> OtherChanges(CookingKind kind, IReadOnlyList<ModPackage> mods, (byte[] Uasset, byte[] Uexp) game)
    {
        var gamePkg = CookedPackage.Load(game.Uasset, game.Uexp);
        var (listExport, listProp) = ListOf(gamePkg, kind);
        var gameData = Fingerprint(gamePkg, listExport, listProp);
        var changed = new List<string>();
        foreach (var m in mods)
        {
            var pkg = CookedPackage.Load(m.Uasset, m.Uexp);
            if (!Fingerprint(pkg, listExport, listProp).SequenceEqual(gameData)) changed.Add(m.Mod);
        }
        return changed;
    }

    /// <summary>Every export's data with the list property left out, per export name.</summary>
    private static List<string> Fingerprint(CookedPackage pkg, string listExport, string listProp)
    {
        var parts = new List<string>();
        foreach (var e in pkg.Exports)
        {
            var name = pkg.NameText(e.ObjectName);
            if (name == listExport)
            {
                var ep = ExportProperties.Read(pkg, e);
                foreach (var p in ep.Properties.Where(p => p.Name != listProp))
                    parts.Add($"{name}.{p.Name}:{Convert.ToHexString(SHA1.HashData([.. p.Header[4..], .. p.Value]))}");
            }
            else parts.Add($"{name}:{Convert.ToHexString(SHA1.HashData(e.Data))}");
        }
        return parts;
    }
}
