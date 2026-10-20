# Changelog

## 1.0.0

First release.

- Merges the AssetRegistry.bin of every mod in `~mods` with the game's registry into
  `~mods/pakchunk999-ModsRegistry_P.pak`, so several item mods work together.
- Window app: first-run setup (SCUM client or SCUM Server, found in Steam), mod list with green/red status, rebuild
  prompt when mods change, settings with game folder, optional AES key, language, updates and "Delete registry".
- Detects added, updated and removed mods and game updates (manifest inside the shared pak).
- Languages: English, Russian, Ukrainian; German, Polish, Czech, Croatian (machine translations, marked AI).
- Console version for servers and scripts.
