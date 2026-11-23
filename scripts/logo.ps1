# Writes the Edgepad logo set to assets/brand/edgepad/. Every coordinate is computed here; none is typed by hand.
#   edgepad-logo-{light,dark}.svg            the mark on the rounded-square tile (the app icon)
#   edgepad-wordmark-{light,dark}.svg        the logo and "Edgepad" side by side
#   edgepad-logo-{light,dark}.ico            the logo at 16, 24, 32, 48, 64, 128 and 256 px
#   png/edgepad-logo-<theme>-<size>.png      the logo at 16 to 1024 px
#   png/edgepad-wordmark-<theme>-<height>.png  the wordmark at 128, 256 and 512 px high
# Each part is its own path with an id (bg, arc, tick-1, tick-2, needle, tick-3, tick-4, letter-N-x) for animation.
# Run: pwsh -STA -File scripts/logo.ps1   (Windows only: WPF outlines the type and draws the PNGs; -STA because WPF drawing needs it.)
#
# Construction on the 1024 tile, angles measured from the pivot P (0 deg = right, 90 deg = down, as on screen):
#   The mark spans exactly 90 deg, 180 (left) to 270 (up). Five ticks sit on it 22.5 deg apart, centre to centre:
#   tick-1 180, tick-2 202.5, needle 225, tick-3 247.5, tick-4 270. All are the same width, STROKE, with round caps.
#   The four side ticks run from R_IN to R_OUT. The needle (centre, accent) is exactly 20% longer than a side tick,
#   measured end to end with its caps: (R_OUT - R_IN + STROKE) * 1.2, so its outer end is R_NEEDLE below.
#   The arc is a quarter circle of radius R_ARC from 180 to 270 deg, centred on P, accent colour.
#   The gap between the arc's outer edge and the ticks' inner caps is R_IN - STROKE - R_ARC = 48.
#   P starts where the strokes' centre of mass sits on the tile centre (790), then moves 7.5% of the tile (76.8 px) down
#   and right, to 866.8, at the owner's request: the exact centre read as sitting high and left.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$inv = [Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets/brand/edgepad'
New-Item -ItemType Directory -Force $out | Out-Null

$STROKE = 88; $R_ARC = 288; $R_IN = 424; $R_OUT = 656
$TICK = $R_OUT - $R_IN + $STROKE                      # a side tick end to end: 320
$R_NEEDLE = $R_IN + 1.2 * $TICK - $STROKE             # needle end to end = 1.2 x 320 = 384, so it ends at 720
$ANGLES = @{ 'tick-1' = 180; 'tick-2' = 202.5; 'needle' = 225; 'tick-3' = 247.5; 'tick-4' = 270 }

function N([double]$v) { $r = [math]::Round($v, 3); if ([math]::Abs($r) -lt 0.0005) { $r = 0 }; $r.ToString('0.###', $inv) }

$C = @{
  light = @{ tile = '#FFFFFF'; ink = '#16171A'; accent = '#0068D6' }
  dark  = @{ tile = '#1B1C20'; ink = '#F2F3F5'; accent = '#4DA3FF' }
}

# Centre of mass of the strokes, pivot at the origin: each piece weighted by its centreline length, projected on the
# 225 deg diagonal (the mark is symmetric about it). Caps add the same half-disc to every end and are left out.
$arcLen = $R_ARC * [math]::PI / 2
$arcR = $R_ARC * [math]::Sin([math]::PI / 4) / ([math]::PI / 4)
$m = $arcLen * $arcR; $L = $arcLen
foreach ($k in $ANGLES.Keys) {
  $r1 = if ($k -eq 'needle') { $R_NEEDLE } else { $R_OUT }
  $len = $r1 - $R_IN; $off = [math]::Abs($ANGLES[$k] - 225) * [math]::PI / 180
  $m += $len * ($R_IN + $r1) / 2 * [math]::Cos($off); $L += $len
}
$perAxis = $m / $L / [math]::Sqrt(2)
# The owner (2026-11-23) wanted the mark down and right of the exact centre of mass by 7.5% of the tile: 76.8 px.
$PIVOT = [math]::Round(512 + $perAxis) + 0.075 * 1024
"Side tick $TICK, needle $(N (1.2 * $TICK)) end to end (ratio $(N ((($R_NEEDLE - $R_IN) + $STROKE) / $TICK))); needle ends at r $(N $R_NEEDLE)"
"Centre of mass $(N $perAxis) from P per axis -> P = $(N (512 + $perAxis)), used $PIVOT (off centre by $(N ($PIVOT - 512 - $perAxis)))"

# The mark at scale k with its pivot at (px, py).
function Mark([double]$k, [double]$px, [double]$py, $col) {
  $pt = { param($r, $deg) $a = $deg * [math]::PI / 180; ,@(($px + $k * $r * [math]::Cos($a)), ($py + $k * $r * [math]::Sin($a))) }
  $line = { param($id, $stroke)
    $r1 = if ($id -eq 'needle') { $R_NEEDLE } else { $R_OUT }
    $a = & $pt $R_IN $ANGLES[$id]; $b = & $pt $r1 $ANGLES[$id]
    "    <path id=`"$id`" stroke=`"$stroke`" d=`"M$(N $a[0]) $(N $a[1])L$(N $b[0]) $(N $b[1])`"/>" }
  $a0 = & $pt $R_ARC 180; $a1 = & $pt $R_ARC 270
  @(
    "  <g fill=`"none`" stroke-width=`"$(N ($k * $STROKE))`" stroke-linecap=`"round`">"
    "    <path id=`"arc`" stroke=`"$($col.accent)`" d=`"M$(N $a0[0]) $(N $a0[1])A$(N ($k * $R_ARC)) $(N ($k * $R_ARC)) 0 0 1 $(N $a1[0]) $(N $a1[1])`"/>"
    (& $line 'tick-1' $col.ink)
    (& $line 'tick-2' $col.ink)
    (& $line 'needle' $col.accent)
    (& $line 'tick-3' $col.ink)
    (& $line 'tick-4' $col.ink)
    '  </g>'
  ) -join "`n"
}

$BG = @{
  square  = 'M0 0H1024V1024H0Z'
  # Continuous-corner (squircle) tile, unchanged from the 2.0 app icon.
  rounded = 'M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 252.53 0 354.65 0Z'
  circle  = 'M512 0A512 512 0 1 1 512 1024A512 512 0 1 1 512 0Z'
}

function Write-Svg($path, $vw, $vh, $w, $h, $note, $body) {
  $svg = @(
    "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 $(N $vw) $(N $vh)`" width=`"$(N $w)`" height=`"$(N $h)`">"
    '  <title>Edgepad</title>'
    "  <!-- $note Generated by scripts/logo.ps1; edit the script, not this file. -->"
    $body
    '</svg>'
  ) -join "`n"
  [IO.File]::WriteAllText((Join-Path $out $path), "$svg`n", [Text.UTF8Encoding]::new($false))
}

# Wordmark glyphs: Lato Bold, sized so the E's stem equals STROKE, tracking -1% of the size.
$gt = [Windows.Media.GlyphTypeface]::new([Uri]::new((Join-Path $root 'assets/video/brand/Lato-Bold.ttf')))
$em = 1000.0
$probe = $gt.GetGlyphOutline($gt.CharacterToGlyphMap[[int][char]'E'], $em, $em)
$bx = $probe.Bounds; $y = $bx.Bottom - 0.3 * $bx.Height; $x = $bx.Left
while ($probe.FillContains([Windows.Point]::new($x + 0.005, $y))) { $x += 0.005 }
$size = $STROKE / (($x - $bx.Left) / $em)
$cap = $gt.CapsHeight * $size

function Path-Data($geo, [double]$dx, [double]$dy) {
  $pg = [Windows.Media.PathGeometry]::CreateFromGeometry($geo)
  $p = { param($q) "$(N ($q.X + $dx)) $(N ($q.Y + $dy))" }
  $sb = [Text.StringBuilder]::new()
  foreach ($f in $pg.Figures) {
    [void]$sb.Append("M$(& $p $f.StartPoint)")
    foreach ($s in $f.Segments) {
      switch ($s.GetType().Name) {
        'LineSegment' { [void]$sb.Append("L$(& $p $s.Point)") }
        'PolyLineSegment' { foreach ($q in $s.Points) { [void]$sb.Append("L$(& $p $q)") } }
        'BezierSegment' { [void]$sb.Append("C$(& $p $s.Point1) $(& $p $s.Point2) $(& $p $s.Point3)") }
        'PolyBezierSegment' { for ($i = 0; $i -lt $s.Points.Count; $i += 3) { [void]$sb.Append("C$(& $p $s.Points[$i]) $(& $p $s.Points[$i+1]) $(& $p $s.Points[$i+2])") } }
        'QuadraticBezierSegment' { [void]$sb.Append("Q$(& $p $s.Point1) $(& $p $s.Point2)") }
        'PolyQuadraticBezierSegment' { for ($i = 0; $i -lt $s.Points.Count; $i += 2) { [void]$sb.Append("Q$(& $p $s.Points[$i]) $(& $p $s.Points[$i+1])") } }
        default { throw "Unhandled segment $_" }
      }
    }
    if ($f.IsClosed) { [void]$sb.Append('Z') }
  }
  $sb.ToString()
}

$glyphs = @(); $x = 0.0; $word = 'Edgepad'
for ($i = 0; $i -lt $word.Length; $i++) {
  $gi = $gt.CharacterToGlyphMap[[int]$word[$i]]
  $glyphs += @{ ch = $word[$i]; geo = $gt.GetGlyphOutline($gi, $size, $size); x = $x }
  $x += $gt.AdvanceWidths[$gi] * $size - 0.01 * $size
}
$inkLeft = ($glyphs | ForEach-Object { $_.x + $_.geo.Bounds.Left } | Measure-Object -Minimum).Minimum
$inkRight = ($glyphs | ForEach-Object { $_.x + $_.geo.Bounds.Right } | Measure-Object -Maximum).Maximum
$inkBottom = ($glyphs | ForEach-Object { $_.geo.Bounds.Bottom } | Measure-Object -Maximum).Maximum
"Wordmark: Lato Bold $(N $size), cap height $(N $cap), ink width $(N ($inkRight - $inkLeft))"

# Lockup: the rounded tile, a 256 gap, then the word with its cap height centred on the tile's centre line.
$GAP = 256
$baseline = 512 + $cap / 2
$lockW = 1024 + $GAP + ($inkRight - $inkLeft)
$lockH = [math]::Max(1024, $baseline + $inkBottom)
$letters = for ($i = 0; $i -lt $glyphs.Count; $i++) {
  $g = $glyphs[$i]
  "    <path id=`"letter-$($i + 1)-$($g.ch)`" d=`"$(Path-Data $g.geo (1024 + $GAP + $g.x - $inkLeft) $baseline)`"/>"
}

foreach ($t in 'light', 'dark') {
  $col = $C[$t]
  $tile = { param($shape) "  <path id=`"bg`" fill=`"$($col.tile)`" d=`"$($BG[$shape])`"/>`n" }
  Write-Svg "edgepad-logo-$t.svg" 1024 1024 1024 1024 "Rounded square (continuous corners), mark at full size, pivot ($PIVOT, $PIVOT)." ((& $tile 'rounded') + (Mark 1 $PIVOT $PIVOT $col))
  Write-Svg "edgepad-wordmark-$t.svg" $lockW $lockH $lockW $lockH "The logo, gap $GAP, then Lato Bold $(N $size) (E stem = stroke $STROKE, tracking -1%), cap height centred on the logo." (
    (& $tile 'rounded') + (Mark 1 $PIVOT $PIVOT $col) + "`n  <g id=`"wordmark`" fill=`"$($col.ink)`">`n" + ($letters -join "`n") + "`n  </g>")
}

# Every other copy of the mark in the repo, written from the same geometry so none can drift.
function Write-Text($rel, $text) { [IO.File]::WriteAllText((Join-Path $root $rel), $text, [Text.UTF8Encoding]::new($false)) }
$markD = { param($col) [regex]::Matches((Mark 1 $PIVOT $PIVOT $col), '<path id="([^"]+)" stroke="([^"]+)" d="([^"]+)"/>') }
foreach ($t in 'light', 'dark') {
  $logo = [IO.File]::ReadAllText((Join-Path $out "edgepad-logo-$t.svg"))
  # The build reads these: scripts/icons.sh (exe, tray, .icns, Linux, touch icon), linux/packaging/pack.sh, the README.
  Write-Text "assets/edgepad-$t.svg" $logo
  # The website header reads the light file's paths and recolours them with its theme tokens.
  if ($t -eq 'light') { Write-Text 'site/public/edgepad-logo.svg' $logo }
  # Android's in-app logo (MarkView, drawn with AndroidSVG).
  Write-Text "android/app/src/main/res/raw/mark_$t.svg" $logo
  # macOS Icon Composer glyph: the mark alone on the 1024 canvas; icon.json supplies the tile colour.
  Write-Text "macos/Resources/edgepad.icon/Assets/glyph-$t.svg" (@(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">'
    '  <!-- The mark without its tile. Generated by scripts/logo.ps1. -->'
    (Mark 1 $PIVOT $PIVOT $C[$t]), '</svg>', '') -join "`n")
}

# Android adaptive icon: the 1024 tile's paths on the 108dp canvas, scaled so every stroke edge stays inside the
# 66dp safe circle that every launcher mask keeps. The farthest edge is found by walking each centreline.
$far = 0
foreach ($id in $ANGLES.Keys) {
  $r1 = if ($id -eq 'needle') { $R_NEEDLE } else { $R_OUT }; $a = $ANGLES[$id] * [math]::PI / 180
  foreach ($r in $R_IN, $r1) { $far = [math]::Max($far, [math]::Sqrt([math]::Pow($PIVOT + $r * [math]::Cos($a) - 512, 2) + [math]::Pow($PIVOT + $r * [math]::Sin($a) - 512, 2))) }
}
for ($deg = 180; $deg -le 270; $deg += 0.5) { $a = $deg * [math]::PI / 180; $far = [math]::Max($far, [math]::Sqrt([math]::Pow($PIVOT + $R_ARC * [math]::Cos($a) - 512, 2) + [math]::Pow($PIVOT + $R_ARC * [math]::Sin($a) - 512, 2))) }
$far += $STROKE / 2
$s = 33 / $far; $o = 54 - 512 * $s
"Android: farthest stroke edge $(N $far) from the tile centre -> scale $(N $s), offset $(N $o)"
$vector = { param($col, $mono, $note)
  $paths = foreach ($m in (& $markD $col)) {
    $colour = if ($mono) { '#000000' } else { $m.Groups[2].Value }
    @("    <path", "      android:name=`"$($m.Groups[1].Value)`"", "      android:fillColor=`"#00000000`"", "      android:pathData=`"$($m.Groups[3].Value)`"",
      "      android:strokeWidth=`"$STROKE`"", "      android:strokeColor=`"$colour`"", '      android:strokeLineCap="round" />') -join "`n"
  }
  @('<?xml version="1.0" encoding="utf-8"?>'
    "<!-- $note The 1024-unit paths of assets/edgepad-*.svg, scaled into the 66dp safe circle. Generated by scripts/logo.ps1. -->"
    '<vector xmlns:android="http://schemas.android.com/apk/res/android"'
    '  android:width="108dp"', '  android:height="108dp"', '  android:viewportWidth="108"', '  android:viewportHeight="108">'
    "  <group android:translateX=`"$(N $o)`" android:translateY=`"$(N $o)`" android:scaleX=`"$(N $s)`" android:scaleY=`"$(N $s)`">"
    ($paths -join "`n"), '  </group>', '</vector>', '') -join "`n"
}
Write-Text 'android/app/src/main/res/drawable/ic_launcher_foreground.xml' (& $vector $C.light $false 'Launcher foreground, light: ink and accent on the white background.')
Write-Text 'android/app/src/main/res/drawable-night/ic_launcher_foreground.xml' (& $vector $C.dark $false 'Launcher foreground, dark: ink and accent on the dark background.')
Write-Text 'android/app/src/main/res/drawable/ic_launcher_monochrome.xml' (& $vector $C.light $true 'Single colour: Android tints the monochrome layer for themed icons.')

# Site favicon: the mark alone, ink and accent following the browser's colour scheme.
$fav = foreach ($m in (& $markD $C.light)) { $cls = if ($m.Groups[2].Value -eq $C.light.accent) { 'a' } else { 'i' }; "    <path id=`"$($m.Groups[1].Value)`" class=`"$cls`" d=`"$($m.Groups[3].Value)`"/>" }
Write-Text 'site/app/icon.svg' (@('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">', '  <title>Edgepad</title>'
  '  <!-- The Edgepad mark without its tile, ink and accent switched for a dark browser tab bar. Generated by scripts/logo.ps1. -->'
  '  <style>', "    .i { stroke: $($C.light.ink) } .a { stroke: $($C.light.accent) }", "    @media (prefers-color-scheme: dark) { .i { stroke: $($C.dark.ink) } .a { stroke: $($C.dark.accent) } }", '  </style>'
  "  <g fill=`"none`" stroke-width=`"$STROKE`" stroke-linecap=`"round`">", ($fav -join "`n"), '  </g>', '</svg>', '') -join "`n")

# The social card's logo (assets/social-preview.svg; the CI icons job renders the PNG).
$card = Join-Path $root 'assets/social-preview.svg'
$cardText = [IO.File]::ReadAllText($card)
$logoBody = ([IO.File]::ReadAllText((Join-Path $out 'edgepad-logo-light.svg')) -split "`n" | Where-Object { $_ -match '^\s+<(path|g|/g)' }) -join "`n"
$cardNew = [regex]::Replace($cardText, '(?s)<g transform="translate\(64 40\) scale\(0\.05078125\)">.*?</g></g>', { "<g transform=`"translate(64 40) scale(0.05078125)`">`n$logoBody</g>" })
if ($cardNew -eq $cardText -and $cardText -notmatch [regex]::Escape($logoBody)) { throw 'social-preview.svg: logo block not found' }
[IO.File]::WriteAllText($card, $cardNew, [Text.UTF8Encoding]::new($false))

# PNGs, drawn from the SVG files just written (so a PNG can never drift from its SVG) with WPF's renderer.
# Only the shapes these files use are read: path fill, path stroke, and the group's stroke width with round caps.
function Render-Png($svgName, [int]$w, [int]$h, $pngPath) {
  $svg = [IO.File]::ReadAllText((Join-Path $out $svgName))
  $vb = [regex]::Match($svg, 'viewBox="0 0 ([\d.]+) ([\d.]+)"')
  $sx = $w / [double]$vb.Groups[1].Value; $sy = $h / [double]$vb.Groups[2].Value
  $sw = [double]([regex]::Match($svg, 'stroke-width="([\d.]+)"').Groups[1].Value)
  $groupFill = [regex]::Match($svg, '<g id="wordmark" fill="([^"]+)"').Groups[1].Value
  $dv = [Windows.Media.DrawingVisual]::new()
  $dc = $dv.RenderOpen()
  $dc.PushTransform([Windows.Media.ScaleTransform]::new($sx, $sy))
  foreach ($p in [regex]::Matches($svg, '<path [^>]*/>')) {
    $d = [regex]::Match($p.Value, ' d="([^"]+)"').Groups[1].Value
    $geo = [Windows.Media.Geometry]::Parse("F1 $d")
    $fill = [regex]::Match($p.Value, ' fill="([^"]+)"').Groups[1].Value
    $stroke = [regex]::Match($p.Value, ' stroke="([^"]+)"').Groups[1].Value
    if ($stroke) {
      $pen = [Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString($stroke), $sw)
      $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
      $dc.DrawGeometry($null, $pen, $geo)
    } else {
      if (-not $fill) { $fill = $groupFill }
      $dc.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString($fill), $null, $geo)
    }
  }
  $dc.Pop(); $dc.Close()
  $bmp = [Windows.Media.Imaging.RenderTargetBitmap]::new($w, $h, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($dv)
  $enc = [Windows.Media.Imaging.PngBitmapEncoder]::new()
  $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = [IO.MemoryStream]::new(); $enc.Save($ms)
  if ($pngPath) { [IO.File]::WriteAllBytes((Join-Path $out $pngPath), $ms.ToArray()) }
  , $ms.ToArray()
}

New-Item -ItemType Directory -Force (Join-Path $out 'png') | Out-Null
$ICO_SIZES = 16, 24, 32, 48, 64, 128, 256
foreach ($t in 'light', 'dark') {
  foreach ($px in 16, 24, 32, 48, 64, 128, 180, 192, 256, 512, 1024) {
    $null = Render-Png "edgepad-logo-$t.svg" $px $px "png/edgepad-logo-$t-$px.png"
  }
  foreach ($hh in 128, 256, 512) {
    $ww = [int][math]::Round($hh * $lockW / $lockH)
    $null = Render-Png "edgepad-wordmark-$t.svg" $ww $hh "png/edgepad-wordmark-$t-$hh.png"
  }
  # ICO: one PNG-compressed image per size (Windows Vista and later read these), 256 written as 0 in the header.
  $images = foreach ($px in $ICO_SIZES) { , (Render-Png "edgepad-logo-$t.svg" $px $px $null) }
  $ms = [IO.MemoryStream]::new(); $bw = [IO.BinaryWriter]::new($ms)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$ICO_SIZES.Count)
  $offset = 6 + 16 * $ICO_SIZES.Count
  for ($i = 0; $i -lt $ICO_SIZES.Count; $i++) {
    $px = $ICO_SIZES[$i]; $dim = if ($px -ge 256) { 0 } else { $px }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
  }
  foreach ($img in $images) { $bw.Write($img) }
  $bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $out "edgepad-logo-$t.ico"), $ms.ToArray())
}

# Self-check: the angles and lengths as drawn, read back from the light logo file.
$svg = [IO.File]::ReadAllText((Join-Path $out 'edgepad-logo-light.svg'))
$prev = $null
foreach ($id in 'tick-1', 'tick-2', 'needle', 'tick-3', 'tick-4') {
  $mm = [regex]::Match($svg, "id=`"$id`"[^>]*d=`"M([\d.]+) ([\d.]+)L([\d.]+) ([\d.]+)`"")
  $x0, $y0, $x1, $y1 = 1..4 | ForEach-Object { [double]$mm.Groups[$_].Value }
  $ang = [math]::Atan2($y1 - $y0, $x1 - $x0) * 180 / [math]::PI; if ($ang -lt 0) { $ang += 360 }
  $len = [math]::Sqrt(($x1 - $x0) * ($x1 - $x0) + ($y1 - $y0) * ($y1 - $y0)) + $STROKE
  $step = if ($prev) { " (+$(N ($ang - $prev)))" } else { '' }
  "  $id  angle $(N $ang)$step  length with caps $(N $len)"
  $prev = $ang
}
