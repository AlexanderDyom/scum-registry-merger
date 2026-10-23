# Changelog

## 1.2.0

- Recipes are merged too. Cooking mods ship the game's shared cooking files as "game + mine": the recipe list
  (`CookingRecipeRegistry`), the recipes known by default (`CookingSkill`) and the game's 9 cookbooks
  (`Book_Cooking_*`). The game used only the highest-priority copy, so other mods' new recipes vanished. Now the shared
  pak carries merged versions: game list + every mod's additions − removals (`Enabled: false` counts as a removal;
  removals only from mods built for the installed game version; without the AES key additions only, with a hint).
- New *Recipes* column, and conflicts: files 2+ mods ship in different versions (e.g. the same game recipe) are listed
  with the mod whose version works in game.
- Cooked packages are read and written with ScumModKit (git submodule `external/modkit`).

## 1.1.3

- Game version references are no longer published or downloaded. The `reference/` folder is gone from the repository.
  With the AES key the game version check works as before. Without it, the app says the version can't be checked,
  unless you made a reference of your game version yourself with `ScumRegistryMerger.Cli make-reference`, which now
  saves it where the app looks for it (`%APPDATA%\ScumRegistryMerger\reference`). The `--offline` option is gone.
- The first-start window and the settings can make that reference too: a *Make reference* button, active once the
  AES key is entered and checked, and a reminder to make it again after every game update. The first-start window
  also got a *Check* button for the key.

## 1.1.2

- Game version check. Each mod's registry is compared with the installed game: a mod built for another game version
  gets an "other game version" badge (with how many records are of another version or missing), and an orange "!"
  until it is in the shared registry. Its outdated records stay out of the shared registry.
- Without the AES key the check uses a published reference of the installed game version (`reference/`, hashes only,
  downloaded once). If every mod is built for another game version, the app asks for the AES key instead of building a
  wrong registry. If there's no reference for the game version yet, it says so.
- Console: `make-reference` writes the reference of the installed game version (needs the AES key); `--reference` and
  `--offline` options.

## 1.1.1

- Zlib-compressed mod paks (packed with UnrealPak or the SCUM editor) can be read now. Before, such a mod stopped the
  whole check with "Zlib decompression failed: not initialized".
- A mod whose registry can't be read no longer stops the check: it is marked "Can't be read", left out of the shared
  registry, and the other mods are merged as usual.

## 1.1.0

- The shared pak now always outranks the mods. SCUM compares pak names as text, so a mod named
  `pakchunk9999999-X_P.pak` used to win over `pakchunk999-ModsRegistry_P.pak` and hide the merged registry. The shared
  pak is now `pakchunk999-ModsRegistry_999_P.pak` (patch number 999, or one above any other pak's), which outranks any
  name. Verified in game.
- Older shared paks are removed after a build.
- New warning: a pak with a registry of its own that still loads after the shared one.
- Clearer third-party notices and a statement on game files: SCUM's assets belong to Gamepires / Jagex, the tool only
  reads the user's own installed files, and no game files or AES key are in the repository or the releases.

## 1.0.0

First release.

- Merges the AssetRegistry.bin of every mod in `~mods` with the game's registry into
  `~mods/pakchunk999-ModsRegistry_P.pak`, so several item mods work together.
- Window app: first-run setup (SCUM client or SCUM Server, found in Steam), mod list with green/red status, rebuild
  prompt when mods change, settings with game folder, optional AES key, language, updates and "Delete registry".
- Detects added, updated and removed mods and game updates (manifest inside the shared pak).
- Languages: English, Russian, Ukrainian; German, Polish, Czech, Croatian (machine translations, marked AI).
- Console version for servers and scripts.
