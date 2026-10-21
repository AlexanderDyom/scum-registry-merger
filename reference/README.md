# Game version references

One `<fingerprint>.ref` per SCUM build (client and dedicated server separately): gzip-compressed 64-bit hashes of the
record names in the game's own AssetRegistry.bin. No game file names or content.

Made with `ScumRegistryMerger.Cli.exe make-reference` (needs the AES key) after every game update.

| File | Game | Steam build | Records |
|---|---|---|---|
| `a7336f384976f547.ref` | SCUM (client) | 25804202 | 126,942 |
| `1f627336cc7db851.ref` | SCUM Server | 25803875 | 126,711 |
