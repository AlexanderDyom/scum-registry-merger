// ScumRegistryMerger: merges the AssetRegistry.bin of every SCUM mod pak that ships one into a single registry pak
// (pakchunk999-ModsRegistry_P.pak), so several mods that each ship a full registry keep all their new items/recipes.
using RegistryMerger;

try
{
    return Cli.Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 1;
}

static class Cli
{
    const string RegistryPath = MergeEngine.RegistryPath;

    const string Usage = """
        ScumRegistryMerger.Cli - merge the AssetRegistry.bin of several SCUM mods into one registry pak.

        Usage:
          ScumRegistryMerger.Cli [merge] [--paks <SCUM/Content/Paks>] [--out <file.pak>] [--aes <hex> | --aes-file <file>]
                                     [--mods <pak> ...] [--no-ship-filter] [--registry-out <AssetRegistry.bin>] [--dry-run]
          ScumRegistryMerger.Cli verify <AssetRegistry.bin>     parse + re-save, compare byte for byte
          ScumRegistryMerger.Cli list <AssetRegistry.bin> [--tags]  print ObjectPath, AssetClass, PackageName of every record
          ScumRegistryMerger.Cli pack <AssetRegistry.bin> <out.pak>  pack a registry as SCUM/AssetRegistry.bin (pak v11)
          ScumRegistryMerger.Cli make-reference [--paks <game>] (--aes <hex> | --aes-file <file>) [--out <folder>]
                       write <fingerprint>.ref: what the installed game version's registry contains (hashes only), so
                       players without the AES key can check their mods against it (publish it in reference/)

        merge (default):
          --paks       the game's Paks folder (default: found from the current folder or the program's folder)
          --out        output pak (default: <Paks>/~mods/pakchunk999-ModsRegistry_P.pak); a .sig is copied next to it
          --aes        the game's AES key (hex), or --aes-file <file with the key>, or env SCUM_AES_KEY.
                       With the key the game's own registry is the base; without it the base is the game part of the
                       lowest mod's registry (fine as long as that mod was built for the current game version).
          --mods       mod paks to merge (default: every *_P.pak under Paks that has SCUM/AssetRegistry.bin, except --out
                       and registry-only paks), in load order: later = higher priority
          --no-ship-filter  take every record a mod registry adds, not only those of packages its pak ships
          --reference  a game reference (.ref file, or a folder with <fingerprint>.ref) for checks without the AES key;
                       default: the cache in %APPDATA%\ScumRegistryMerger\reference, then the project's GitHub
          --offline    don't download a game reference
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            if (args.Length > 0) return 0;
            // started by double-click: merge with defaults, keep the window open
            int code;
            try { code = Merge([]); }
            catch (Exception ex) { Console.Error.WriteLine("error: " + ex.Message); code = 1; }
            if (!Console.IsInputRedirected) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
            return code;
        }
        return args[0] switch
        {
            "verify" when args.Length == 2 => Verify(args[1]),
            "list" when args.Length == 2 => List(args[1], false),
            "list" when args.Length == 3 && args[2] == "--tags" => List(args[1], true),
            "merge" => Merge(args[1..]),
            "pack" when args.Length == 3 => Pack(args[1], args[2]),
            "make-reference" => MakeReference(args[1..]),
            _ when args[0].StartsWith("--") => Merge(args),
            _ => Fail(Usage),
        };
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    static int Verify(string path)
    {
        var d = File.ReadAllBytes(path);
        var r = AssetRegistry.Load(d);
        var same = r.Save().AsSpan().SequenceEqual(d);
        Console.WriteLine($"{path}: version {r.Version}, {r.Names.Count} names, {r.Assets.Count} assets, {r.Nodes.Count} depends nodes, {r.Packages.Count} package data; re-save {(same ? "identical" : "DIFFERENT")}");
        return same ? 0 : 1;
    }

    static int List(string path, bool tags)
    {
        var r = AssetRegistry.Load(File.ReadAllBytes(path));
        using var o = new StreamWriter(Console.OpenStandardOutput());
        foreach (var a in r.Assets)
        {
            o.WriteLine($"{r.NameText(a.ObjectPath)}\t{r.NameText(a.AssetClass)}\t{r.NameText(a.PackageName)}");
            if (!tags) continue;
            foreach (var (k, v) in r.Tags(a.TagHandle)) o.WriteLine($"\t{k}={v}");
            o.WriteLine($"\tbundles={a.Bundles.Count} chunks=[{string.Join(",", a.ChunkIds)}] flags=0x{a.PackageFlags:X}");
        }
        o.WriteLine($"{r.Assets.Count} assets");
        return 0;
    }

    static int Pack(string registry, string pak)
    {
        PakWriter.Write(pak, [(RegistryPath, File.ReadAllBytes(registry))]);
        Console.WriteLine($"wrote {pak}");
        return 0;
    }

    static int Merge(string[] args)
    {
        string? paks = null, aes = Environment.GetEnvironmentVariable("SCUM_AES_KEY"), reference = null;
        var offline = false;
        var options = new BuildOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new InvalidOperationException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--paks": paks = Next(); break;
                case "--out": options.OutputPak = Path.GetFullPath(Next()); break;
                case "--aes": aes = Next(); break;
                case "--aes-file": aes = File.ReadLines(Next()).FirstOrDefault(); break;
                case "--registry-out": options.RegistryOut = Next(); break;
                case "--no-ship-filter": options.ShipFilter = false; break;
                case "--dry-run": options.DryRun = true; break;
                case "--reference": reference = Next(); break;
                case "--offline": offline = true; break;
                case "--mods":
                    options.Mods ??= [];
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) options.Mods.Add(Path.GetFullPath(args[++i]));
                    break;
                default: return Fail($"unknown option {args[i]}\n\n{Usage}");
            }
        }

        paks = paks != null ? GameLocator.ResolvePaks(paks) ?? paks : GameLocator.FindPaksNearby()
            ?? throw new InvalidOperationException("SCUM Paks folder not found: pass --paks <SCUM/SCUM/Content/Paks>.");
        Console.WriteLine($"Paks: {paks}");
        var log = new Progress<string>(s => Console.WriteLine("  " + s));
        using var engine = MergeEngine.Open(paks, aes);
        var fingerprint = GameReference.GameFingerprint(paks);
        if (reference != null)
            engine.Reference = GameReference.Load(Directory.Exists(reference) ? Path.Combine(reference, GameReference.FileName(fingerprint)) : reference);
        else if (string.IsNullOrWhiteSpace(aes))
            engine.Reference = ReferenceStore.FindAsync(fingerprint, download: !offline).GetAwaiter().GetResult();
        var scan = engine.Scan(options.OutputPak);
        Console.WriteLine($"game registry: {(scan.GameRegistryReadable ? "readable" : $"not readable ({scan.EncryptedPaks} encrypted paks{(string.IsNullOrWhiteSpace(aes) ? ", no AES key given" : "")})")}");
        Console.WriteLine($"game version {fingerprint}: " + scan.VersionCheck switch
        {
            VersionCheck.Game => "mods checked against the game's own registry",
            VersionCheck.Reference => "mods checked against the published reference of this game version",
            _ => "no reference for this game version: mods can't be checked without the AES key",
        });
        foreach (var m in scan.Mods)
            Console.WriteLine(m.State == ModState.Unreadable
                ? $"  {m.FileName}: registry can't be read ({m.Error}), left out"
                : $"  {m.FileName}: {(m.HasRegistry ? $"{m.NewRecords} new records" : "no registry")}, {m.State}{(m.IsNew ? ", new" : "")}{(m.IsUpdated ? ", updated" : "")}"
                  + (m.VersionMismatch ? $", BUILT FOR ANOTHER GAME VERSION ({m.StaleRecords} records of another version, {m.MissingGameRecords} game records missing)" : ""));
        if (scan.NeedsKey) Console.WriteLine("  the AES key is needed for a correct build: pass --aes or --aes-file");
        foreach (var r in scan.RemovedMods) Console.WriteLine($"  removed since last build: {r}");
        if (scan.GameUpdated) Console.WriteLine("  the game's registry changed since the last build");
        foreach (var o in scan.Outranking) Console.WriteLine($"  {o} ships its own registry and loads after {scan.SharedPak}: the game uses its registry");

        var result = engine.Build(options);
        foreach (var (mod, added, replaced, skipped) in result.PerMod)
            Console.WriteLine($"  {mod}: {added} added" + (replaced > 0 ? $", {replaced} replace another mod's" : "") +
                              (skipped > 0 ? $", {skipped} ignored (their package is not in this pak)" : ""));
        Console.WriteLine($"merged: {result.Records} records (base: {(result.BaseFromGame ? "game registry" : "a mod's copy of the game registry")})");
        if (options.RegistryOut != null) Console.WriteLine($"wrote {options.RegistryOut}");
        foreach (var (mod, error) in result.Unreadable) Console.WriteLine($"  {mod}: left out, registry can't be read ({error})");
        foreach (var r in result.Removed) Console.WriteLine($"removed older shared pak {r}");
        if (!options.DryRun)
            Console.WriteLine($"wrote {result.OutputPak}" + (result.SigCopiedFrom != null ? $" (+ .sig, copy of {result.SigCopiedFrom})" : " (no .sig found to copy)"));
        return 0;
    }

    static int MakeReference(string[] args)
    {
        string? paks = null, aes = Environment.GetEnvironmentVariable("SCUM_AES_KEY"), outDir = ".";
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new InvalidOperationException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--paks": paks = Next(); break;
                case "--aes": aes = Next(); break;
                case "--aes-file": aes = File.ReadLines(Next()).FirstOrDefault(); break;
                case "--out": outDir = Next(); break;
                default: return Fail($"unknown option {args[i]}\n\n{Usage}");
            }
        }
        paks = paks != null ? GameLocator.ResolvePaks(paks) ?? paks : GameLocator.FindPaksNearby()
            ?? throw new InvalidOperationException("SCUM Paks folder not found: pass --paks <game folder>.");
        if (string.IsNullOrWhiteSpace(aes)) throw new InvalidOperationException("make-reference needs the AES key (--aes or --aes-file).");
        using var engine = MergeEngine.Open(paks, aes);
        var reference = engine.MakeReference() ?? throw new InvalidOperationException("The game's registry can't be read with this key.");
        Directory.CreateDirectory(outDir);
        var file = Path.Combine(outDir, GameReference.FileName(reference.Fingerprint));
        reference.Save(file);
        Console.WriteLine($"{paks}: game version {reference.Fingerprint}, {reference.Count} records -> {file} ({new FileInfo(file).Length:N0} bytes)");
        return 0;
    }
}
