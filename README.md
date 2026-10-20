# SCUM Registry Merger

**Use several SCUM item mods at the same time without their items disappearing.**

[Русская версия](README.ru.md)

## The problem

A SCUM mod that adds new items, recipes or placeables has to ship a full `SCUM/AssetRegistry.bin`: the game's own
records plus the mod's new ones. Without those records the game doesn't find the new assets.

The game reads only one registry: the one in the pak with the highest priority. Install two such mods in
`SCUM/Content/Paks/~mods` and only one registry is used. **The other mod's new items silently vanish**: they can't be
spawned or crafted, and traders don't sell them.

## The fix

SCUM Registry Merger reads the registry of every installed mod and merges it with the game's registry. It writes the
result as one extra pak, `~mods/pakchunk999-ModsRegistry_P.pak` (plus a `.sig`). That pak loads after the mods, so the
game uses its registry, and it has every mod's records.

Your mods stay untouched. To undo, delete that one pak, or press *Delete registry* in the settings.

## Download and use

1. Download `ScumRegistryMerger.exe` from [Releases](https://github.com/AlexanderDyom/scum-registry-merger/releases).
   It is one file, there's nothing to install, and you don't need .NET.
2. Start it. On first start, pick **SCUM** (client) or **SCUM Server**. Both are found in your Steam libraries, or you
   can choose any folder. The game's AES key is optional (see below).
3. The window lists every mod in `~mods`:
   - ✔ green: the mod's new items are in the shared registry, or the mod doesn't need it;
   - ✖ red: the mod's new items are missing. Press **Build registry**.
4. Close the game (or stop the server) before building: SCUM keeps its paks open.

**Run it again whenever you add, update or remove a mod, and after every game update.** On start it compares the mods
with the last build. If something changed, it offers to rebuild.

### The AES key (optional, recommended)

The game's own paks are encrypted; mod paks are not.

- **Without the key**, the game's records come from a copy inside one of your mods. That works as long as the mod was
  built for the current game version.
- **With the key**, the registry is built from the game's own records, and game updates are detected.

The key is stored encrypted for your Windows user (DPAPI) and never leaves your computer.

### Dedicated servers

Pick *SCUM Server* on first start. On a server without a desktop, use the console version: it does the same scan and
build.

```
ScumRegistryMerger.Cli.exe --paks "C:\SCUMServer" [--aes-file key.txt] [--dry-run]
```

## Languages

English, Русский, Українська. Deutsch, Polski, Čeština and Hrvatski are machine translations and are marked **AI** in
the app. Corrections are welcome: one JSON file per language in [`Gui/Strings`](Gui/Strings).

## Safety and fair play

- The tool only reads the game's paks and writes one pak into `~mods`. It doesn't touch your mods or the game files.
- Modded paks are for single player and servers that allow them. Don't join official or protected servers with mods
  installed.
- This is a fan-made tool, not affiliated with Gamepires or Jagex.

## How it works (for modders)

- `Core/AssetRegistry.cs` reads and writes the cooked UE 4.27 AssetRegistry (FAssetRegistryVersion 8, FixedTags)
  completely. Every SCUM registry re-saves byte-identical.
- `Core/Merger.cs` keeps the game's records as they are. A mod record whose object path is new is added with all its
  tags, bundles, chunk ids and flags. Only records whose package actually ships in that mod's pak are taken, so stale
  game records from older game versions don't come back.
- `Core/PakWriter.cs` writes an uncompressed pak v11 with the path hash index and the full directory index (the same
  bytes as `repak pack --version V11`).
- `Core/MergeEngine.cs` scans and builds. The shared pak also carries `SCUM/ModsRegistry.json` with the SHA-1 of every
  merged mod registry, which is how added, updated and removed mods are detected.
- `Gui/` is the WPF window. `Cli/` is the console version, which also has `verify`, `list` and `pack` commands for
  registries.

## Build from source

Needs the .NET 8 SDK on Windows:

```
dotnet build RegistryMerger.sln -c Release
dotnet publish Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
dotnet publish Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

The icons come from [`icons/`](icons), a tiny pixel-art generator.

## Credits and license

MIT, see [LICENSE](LICENSE). Built on [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (Apache-2.0). The registry
layout follows [SCUM Modding Studio](https://github.com/ALLAWI32/SCUM-Modding-Studio) (MIT). The pak layout matches
[repak](https://github.com/trumank/repak). Details: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
