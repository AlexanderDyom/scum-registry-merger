# Pixel-art utility icons in one house style: gold pixels with a 1px bevel (light top/left, dark bottom/right),
# a dark outline, optional cracks, on an optional dark rounded plate.
# A design is a 16x16 text map ('g' = gold, '.' = empty); 32+ px sizes use it doubled (32x32 grid), where cracks live.
# Usage: powershell -File make_icon.ps1 -Def rm.txt -Ico out.ico [-Preview out.png]
# Def file: "plate: yes|no", "cracks: x,y x,y ..." (32-grid coords), then 16 map lines; '#' lines are comments.
param([Parameter(Mandatory)][string]$Def, [Parameter(Mandatory)][string]$Ico, [string]$Preview)
Add-Type -AssemblyName System.Drawing

$gold = @{
  hl    = [System.Drawing.Color]::FromArgb(255, 255, 224, 138)
  mid   = [System.Drawing.Color]::FromArgb(255, 227, 169, 59)
  sh    = [System.Drawing.Color]::FromArgb(255, 168, 112, 30)
  out   = [System.Drawing.Color]::FromArgb(255, 43, 26, 7)
  crack = [System.Drawing.Color]::FromArgb(255, 122, 78, 20)
  plate = [System.Drawing.Color]::FromArgb(255, 38, 43, 47)
  rim   = [System.Drawing.Color]::FromArgb(255, 110, 82, 34)
}

# ---- read the definition
$map = @(); $plate = $true; $cracks = @()
foreach ($line in Get-Content -LiteralPath $Def -Encoding UTF8) {
  $t = $line.Trim()
  if ($t -eq '' -or $t.StartsWith('#')) { continue }
  if ($t -match '^plate:\s*(\w+)') { $plate = $Matches[1] -in 'yes', 'true', '1'; continue }
  if ($t -match '^cracks:(.*)$') { $cracks = $Matches[1].Trim() -split '\s+' | Where-Object { $_ } | ForEach-Object { ,($_ -split ',' | ForEach-Object { [int]$_ }) }; continue }
  $map += $t
}
if ($map.Count -ne 16 -or ($map | Where-Object { $_.Length -ne 16 })) { throw "$Def needs 16 map lines of 16 characters." }

function Upscale([string[]]$m) {
  $res = @()
  foreach ($row in $m) { $r = -join ($row.ToCharArray() | ForEach-Object { "$_$_" }); $res += $r; $res += $r }
  return ,$res
}
$map32 = Upscale $map
foreach ($c in $cracks) {
  $chars = $map32[$c[1]].ToCharArray()
  if ($chars[$c[0]] -eq 'g') { $chars[$c[0]] = 'c'; $map32[$c[1]] = -join $chars }
}

# ---- render one grid to a bitmap of its own size
function Render([string[]]$m) {
  $h = $m.Count; $w = $m[0].Length
  $b = New-Object System.Drawing.Bitmap $w, $h
  $isG = { param($x, $y) $x -ge 0 -and $y -ge 0 -and $x -lt $w -and $y -lt $h -and ($m[$y][$x] -eq 'g' -or $m[$y][$x] -eq 'c') }
  for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) {
    if (& $isG $x $y) {
      $col = $gold.mid
      if ($m[$y][$x] -eq 'c') { $col = $gold.crack }
      elseif (-not (& $isG $x ($y - 1)) -or -not (& $isG ($x - 1) $y)) { $col = $gold.hl }
      elseif (-not (& $isG $x ($y + 1)) -or -not (& $isG ($x + 1) $y)) { $col = $gold.sh }
      $b.SetPixel($x, $y, $col)
    } else {
      foreach ($d in @(@(-1,0),@(1,0),@(0,-1),@(0,1),@(-1,-1),@(1,1),@(-1,1),@(1,-1))) {
        if (& $isG ($x + $d[0]) ($y + $d[1])) { $b.SetPixel($x, $y, $gold.out); break }
      }
    }
  } }
  return $b
}

# ---- an icon image of a given size: plate (anti-aliased) + art scaled with nearest neighbour
function IconImage($art, [int]$size) {
  $o = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($o)
  if ($plate) {
    $g.SmoothingMode = 'AntiAlias'
    $r = [Math]::Max(2, [int]($size * 0.18))
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $e = $size - 1
    $p.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $p.AddArc($e - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $p.AddArc($e - $r * 2, $e - $r * 2, $r * 2, $r * 2, 0, 90); $p.AddArc(0, $e - $r * 2, $r * 2, $r * 2, 90, 90)
    $p.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $gold.plate), $p)
    $pen = New-Object System.Drawing.Pen -ArgumentList $gold.rim, ([single][Math]::Max(1, [int]($size / 32)))
    $g.DrawPath($pen, $p)
  }
  $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
  $g.DrawImage($art, 0, 0, $size, $size)
  return $o
}

# 32-bit DIB frame (BITMAPINFOHEADER, bottom-up BGRA, empty AND mask): every reader understands it, unlike PNG frames
function Dib($img) {
  $n = $img.Width
  $ms = New-Object System.IO.MemoryStream
  $w = New-Object System.IO.BinaryWriter $ms
  $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
  for ($y = $n - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $n; $x++) {
    $c = $img.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
  } }
  $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
  $w.Write((New-Object byte[] ($maskRow * $n)))
  $w.Flush()
  return ,$ms.ToArray()
}

$art16 = Render $map
$art32 = Render $map32
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  # 16 and 48 from the 16-grid (exact multiples), the rest from the 32-grid (24 is a 0.75 downscale of it)
  $art = if ($s -eq 16 -or $s -eq 48) { $art16 } else { $art32 }
  $img = IconImage $art $s
  $ms = New-Object System.IO.MemoryStream
  $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , @($s, $ms.ToArray(), (Dib $img))
}

# ---- .ico: DIB frames up to 64 px, PNG frames for 128 and 256 (keeps the file small)
$fs = [System.IO.File]::Create($Ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
  $s = $e[0]; $data = if ($s -ge 128) { $e[1] } else { $e[2] }
  $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($e in $pngs) { if ($e[0] -ge 128) { $bw.Write([byte[]]$e[1]) } else { $bw.Write([byte[]]$e[2]) } }
$bw.Close()
"wrote $Ico ($($sizes -join ', ') px)"

if ($Preview) {
  [int]$W = 16; foreach ($s in $sizes) { $W += $s + 16 }
  $sheet = New-Object System.Drawing.Bitmap $W, 288
  $g = [System.Drawing.Graphics]::FromImage($sheet)
  $g.Clear([System.Drawing.Color]::FromArgb(255, 27, 31, 34))
  $x = 16
  foreach ($e in $pngs) {
    $ms = New-Object System.IO.MemoryStream -ArgumentList (, [byte[]]$e[1])
    $img = [System.Drawing.Image]::FromStream($ms)
    $g.DrawImage($img, $x, 144 - [int]($e[0] / 2), $e[0], $e[0])
    $x += $e[0] + 16
  }
  $sheet.Save([System.IO.Path]::GetFullPath($Preview), [System.Drawing.Imaging.ImageFormat]::Png)
  "wrote $Preview"
}
