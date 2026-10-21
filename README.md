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
result as one extra pak, `~mods/pakchunk999-ModsRegistry_999_P.pak` (plus a `.sig`). That pak takes priority over
every mod (see [Which pak wins](#which-pak-wins)), so the game uses its registry, and it has every mod's records.

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

### Game version check

Each mod's registry also carries the game's records of the game version the mod was built for. With the AES key the
app compares them with the installed game. A mod built for another version gets an *other game version* badge, and an
orange **!** until it is in the shared registry. Its outdated records never get into the shared registry, but the mod
itself may not work right until its author updates it.

Without the key the game version can't be checked, and the app says so. To check without entering the key in the app
every time (for example on another PC with the same game version), make a **reference** of your game version once:
with *Make reference* in the first-start window or the settings (active once the key is checked), or in the console:

```
ScumRegistryMerger.Cli.exe make-reference --paks "<SCUM or SCUM Server folder>" --aes-file key.txt
```

It goes to `%APPDATA%\ScumRegistryMerger\reference\<fingerprint>.ref`. The window app and the console tool use it
from there automatically when no key is set; you can copy that file to other computers with the same game version.
A reference holds only 64-bit hashes of the game's record names, no game file names or content, and is never
uploaded. Make a new one after each game update. If every installed mod is built for another game version, the app
then asks for the AES key instead of building a registry from outdated records.

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
- SCUM and its assets are the property of Gamepires / Jagex. This is a fan-made tool, not affiliated with them. It
  reads only your own installed game files and never redistributes them; the AES key is never stored in this
  repository or in the releases. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Which pak wins

SCUM (Unreal Engine 4.27) decides which pak a file comes from by two rules:

1. **Pak order.** A `*_P.pak` gets `100 × patch version`, where the patch version is `N+1` for a name ending in
   `_N_P.pak` and 1 otherwise.
2. **Path.** At equal order, the pak whose path sorts last as text wins. Names compare as strings, not numbers:
   `pakchunk9999999-X_P.pak` beats `pakchunk999-Y_P.pak`, and `pakchunk999-Y_P.pak` beats `pakchunk1000-Z_P.pak`.

So the shared pak carries a patch number: `_999_P`, or one above the highest patch number any other pak uses. That
puts it above every mod, whatever the names. Older shared paks are removed after a build. If some pak with a registry
of its own still loads after the shared one, the app says so, and a rebuild fixes it.

## How it works (for modders)

- `Core/AssetRegistry.cs` reads and writes the cooked UE 4.27 AssetRegistry (FAssetRegistryVersion 8, FixedTags)
  completely. Every SCUM registry re-saves byte-identical.
- `Core/Merger.cs` keeps the game's records as they are. A mod record whose object path is new is added with all its
  tags, bundles, chunk ids and flags. Only records whose package actually ships in that mod's pak are taken, so stale
  game records from older game versions don't come back.
- `Core/PakEntryReader.cs` reads pak entries. Zlib-compressed ones are inflated with .NET's own ZLibStream, so no native
  zlib library is needed.
- `Core/PakWriter.cs` writes an uncompressed pak v11 with the path hash index and the full directory index (the same
  bytes as `repak pack --version V11`).
- `Core/PakPriority.cs` implements the pak priority rules above.
- `Core/GameReference.cs` covers the game version: a fingerprint of the installed game (names and sizes of its own
  paks) and the record-hash reference. `Core/ReferenceStore.cs` handles the cache and the download.
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
