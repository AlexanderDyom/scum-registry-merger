# Utility icons

One look for all our utilities: gold pixel art with a 1px bevel (light top/left, dark bottom/right), a dark outline and a
few cracks, like gold pixel lettering. Usually it sits on a dark rounded plate with a thin bronze rim.

- Window programs: letters (e.g. `RM` = Registry Merger) -> `rm.txt`.
- Console programs: a small picture of what the tool does (e.g. two paks merging) -> `merge.txt`.

A design is a 16x16 text map (`g` = gold). The 32 px and larger sizes use it doubled, and cracks are given in that
32-grid. Build an icon with all sizes (16-256, DIB frames up to 64 px, PNG frames for 128/256):

```
powershell -ExecutionPolicy Bypass -File make_icon.ps1 -Def rm.txt -Ico ..\Gui\app.ico -Preview preview.png
powershell -ExecutionPolicy Bypass -File make_icon.ps1 -Def merge.txt -Ico ..\Cli\app.ico
```

For a new utility, copy `rm.txt`, draw the new letters (2 px strokes, about 10 rows high), and move the cracks.
