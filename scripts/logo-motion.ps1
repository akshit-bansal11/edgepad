# Writes the Edgepad logo animations as animated SVG (SMIL, plays in a browser and inside an <img>), and with -Frames
# one still SVG per frame for .github/workflows/logo-motion.yml to turn into MP4 and GIF.
#   assets/brand/edgepad/motion/edgepad-logo-pop-{light,dark}.svg    0.75 s: the short one, for the APK and the laptops
#   assets/brand/edgepad/motion/edgepad-logo-dial-{light,dark}.svg   2.65 s: the long one, for the website, video, README
# Run: pwsh -File scripts/logo-motion.ps1 [-Frames <dir>] [-Fps 60]   (pure maths, any OS)
#
# The motion is the one the owner chose on 2026-11-24 (artifact "Edgepad Logo Motion"):
#   pop   the arc, then each tick in angle order 60 ms apart, springs out from the pivot (420 ms each)
#   dial  the pop, then a press; the whole dial scrolls round the pivot as one ruler (white, white, blue, repeating,
#         a tick every 22.5 deg) the way a thumb slides a corner dial in the app (surface/Dial.kt), forward, back,
#         forward; the release snaps it by whole repeats (67.5 deg) so it lands exactly on the logo.
# Geometry is the logo's own (scripts/logo.ps1): pivot 881.8, stroke 88, arc r 288, ticks r 424 to 656, blue to 720.
param([string]$Frames = '', [int]$Fps = 60)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets/brand/edgepad/motion'
New-Item -ItemType Directory -Force $out | Out-Null

$P = 881.8; $R_ARC = 288; $R_IN = 424; $R_OUT = 656; $R_NEEDLE = 720; $W = 88
$MARKS = @(
  @{ id = 'tick-1'; a = 180.0; r1 = $R_OUT }, @{ id = 'tick-2'; a = 202.5; r1 = $R_OUT }, @{ id = 'needle'; a = 225.0; r1 = $R_NEEDLE },
  @{ id = 'tick-3'; a = 247.5; r1 = $R_OUT }, @{ id = 'tick-4'; a = 270.0; r1 = $R_OUT })
$C = @{
  light = @{ tile = '#FFFFFF'; ink = '#16171A'; accent = '#0068D6' }
  dark  = @{ tile = '#1B1C20'; ink = '#F2F3F5'; accent = '#4DA3FF' }
}
$TILE = 'M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 252.53 0 354.65 0Z'

function N([double]$v) { $r = [math]::Round($v, 2); if ([math]::Abs($r) -lt 0.005) { $r = 0 }; $r.ToString('0.##', $inv) }
function Clamp([double]$x, [double]$a = 0, [double]$b = 1) { [math]::Min($b, [math]::Max($a, $x)) }
function Seg([double]$t, [double]$start, [double]$dur) { Clamp (($t - $start) / $dur) }
function Out([double]$x) { 1 - [math]::Pow(1 - $x, 3) }
function InOut([double]$x) { if ($x -lt 0.5) { 4 * $x * $x * $x } else { 1 - [math]::Pow(-2 * $x + 2, 3) / 2 } }
function Spring([double]$x) { if ($x -ge 1) { 1 } else { 1 - [math]::Exp(-6 * $x) * [math]::Cos(9 * $x) } }
function Pt([double]$r, [double]$deg) { $a = $deg * [math]::PI / 180; "$(N ($P + $r * [math]::Cos($a))) $(N ($P + $r * [math]::Sin($a)))" }

# One frame of the logo as a list of strokes in fixed slots: arc, the five marks, and eight ruler slots for the dial.
# Every slot always has the same path commands, so SMIL can interpolate its `d` from frame to frame.
function Frame([string]$kind, [double]$t) {
  $f = [ordered]@{}
  $f['arc'] = @{ r = $R_ARC; op = 1.0; blue = $true; w = $W; arc = $true }
  foreach ($m in $MARKS) { $f[$m.id] = @{ a = $m.a; r0 = [double]$R_IN; r1 = [double]$m.r1; op = 1.0; blue = ($m.id -eq 'needle'); w = $W } }
  for ($i = 0; $i -lt 8; $i++) { $f["ruler-$i"] = @{ a = 180.0; r0 = [double]$R_IN; r1 = [double]$R_OUT; op = 0.0; blue = $false; w = $W } }
  # The pop: arc first, then the marks in angle order.
  $order = @('arc', 'tick-1', 'tick-2', 'needle', 'tick-3', 'tick-4')
  for ($i = 0; $i -lt $order.Count; $i++) {
    $x = Seg $t ($i * 60) 420; $k = 0.55 + 0.45 * (Spring $x); $op = Clamp ($x / 0.25)
    $s = $f[$order[$i]]
    if ($order[$i] -eq 'arc') { $s.r = $R_ARC * $k } else { $s.r0 = $R_IN * $k; $s.r1 = $s.r1 * $k }
    $s.op = $op
  }
  if ($kind -eq 'dial' -and $t -ge 900) {
    $off = Scroll $t
    foreach ($m in $MARKS) { $f[$m.id].op = 0.0 }
    $held = Clamp ((Seg $t 860 80) - (Seg $t 2450 140))
    for ($k = -9; $k -le 6; $k++) {
      $a = 180 + 22.5 * $k + $off
      $op = (Clamp (($a - 168.75) / 11.25)) * (Clamp ((281.25 - $a) / 11.25))
      if ($op -le 0) { continue }
      $blue = ((($k % 3) + 3) % 3) -eq 2
      $f["ruler-$(((($k % 8) + 8) % 8))"] = @{ a = $a; r0 = [double]$R_IN; r1 = [double]($(if ($blue) { $R_NEEDLE } else { $R_OUT })); op = $op; blue = $blue; w = $W + 8 * $held }
    }
  }
  $f
}
$DRAG = @(@(900, 0), @(1750, 150), @(2150, 122), @(2450, 142))
function Scroll([double]$t) {
  if ($t -le $DRAG[0][0]) { return 0 }
  for ($i = 1; $i -lt $DRAG.Count; $i++) {
    $t1 = $DRAG[$i][0]; $a1 = $DRAG[$i][1]; $t0 = $DRAG[$i - 1][0]; $a0 = $DRAG[$i - 1][1]
    if ($t -le $t1) { return $a0 + ($a1 - $a0) * (InOut (($t - $t0) / ($t1 - $t0))) }
  }
  $last = $DRAG[-1][1]; $snapped = [math]::Round($last / 67.5) * 67.5
  $last + ($snapped - $last) * (Out (Seg $t 2450 180))
}
function D($s) { if ($s.arc) { "M$(Pt $s.r 180)A$(N $s.r) $(N $s.r) 0 0 1 $(Pt $s.r 270)" } else { "M$(Pt $s.r0 $s.a)L$(Pt $s.r1 $s.a)" } }

$ANIM = @{ pop = 760; dial = 2650 }
foreach ($kind in 'pop', 'dial') {
  $total = $ANIM[$kind]
  $n = [int][math]::Ceiling($total / 1000 * $Fps)
  $samples = for ($i = 0; $i -le $n; $i++) { , (Frame $kind ([math]::Min($total, $i * 1000.0 / $Fps))) }
  # Five decimals: at 2 the dial's 160 samples (6 ms apart) collapse onto duplicate key times and the timing jitters.
  $keyTimes = (0..$n | ForEach-Object { ([math]::Round($_ / $n, 5)).ToString('0.#####', $inv) }) -join ';'
  foreach ($theme in 'light', 'dark') {
    $col = $C[$theme]
    $dur = "$(N ($total / 1000))s"
    $slots = foreach ($id in $samples[0].Keys) {
      $vals = @{ d = @(); op = @(); stroke = @(); w = @() }
      foreach ($fr in $samples) {
        $s = $fr[$id]
        $vals.d += D $s; $vals.op += N $s.op; $vals.w += N $s.w
        $vals.stroke += $(if ($s.blue) { $col.accent } else { $col.ink })
      }
      $first = $samples[0][$id]
      $tags = @(
        "<animate attributeName=`"d`" dur=`"$dur`" fill=`"freeze`" calcMode=`"linear`" keyTimes=`"$keyTimes`" values=`"$($vals.d -join ';')`"/>"
        "<animate attributeName=`"opacity`" dur=`"$dur`" fill=`"freeze`" calcMode=`"linear`" keyTimes=`"$keyTimes`" values=`"$($vals.op -join ';')`"/>"
      )
      if (($vals.w | Select-Object -Unique).Count -gt 1) { $tags += "<animate attributeName=`"stroke-width`" dur=`"$dur`" fill=`"freeze`" calcMode=`"linear`" keyTimes=`"$keyTimes`" values=`"$($vals.w -join ';')`"/>" }
      if (($vals.stroke | Select-Object -Unique).Count -gt 1) { $tags += "<animate attributeName=`"stroke`" dur=`"$dur`" fill=`"freeze`" calcMode=`"discrete`" keyTimes=`"$keyTimes`" values=`"$($vals.stroke -join ';')`"/>" }
      "    <path id=`"$id`" d=`"$(D $first)`" opacity=`"$(N $first.op)`" stroke=`"$($vals.stroke[0])`" stroke-width=`"$(N $first.w)`">$($tags -join '')</path>"
    }
    $svg = @(
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">'
      '  <title>Edgepad</title>'
      "  <!-- The Edgepad logo, animated ($kind, $dur, plays once and holds). Generated by scripts/logo-motion.ps1. -->"
      "  <path id=`"bg`" fill=`"$($col.tile)`" d=`"$TILE`"/>"
      '  <g fill="none" stroke-linecap="round">'
      ($slots -join "`n")
      '  </g>'
      '</svg>', '') -join "`n"
    [IO.File]::WriteAllText((Join-Path $out "edgepad-logo-$kind-$theme.svg"), $svg, [Text.UTF8Encoding]::new($false))
    "edgepad-logo-$kind-$theme.svg: $($n + 1) frames, $([math]::Round($svg.Length / 1024)) KB"

    if ($Frames) {
      # One still SVG per frame, for the MP4 and GIF renders.
      $dir = Join-Path $Frames "$kind-$theme"
      New-Item -ItemType Directory -Force $dir | Out-Null
      for ($i = 0; $i -le $n; $i++) {
        $fr = $samples[$i]
        $paths = foreach ($id in $fr.Keys) {
          $s = $fr[$id]
          if ($s.op -le 0.001) { continue }
          "<path d=`"$(D $s)`" opacity=`"$(N $s.op)`" stroke=`"$(if ($s.blue) { $col.accent } else { $col.ink })`" stroke-width=`"$(N $s.w)`"/>"
        }
        $still = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 1024 1024`" width=`"1024`" height=`"1024`"><path fill=`"$($col.tile)`" d=`"$TILE`"/><g fill=`"none`" stroke-linecap=`"round`">$($paths -join '')</g></svg>"
        [IO.File]::WriteAllText((Join-Path $dir ('{0:D4}.svg' -f $i)), $still, [Text.UTF8Encoding]::new($false))
      }
    }
  }
}


# Android's launch screen plays only an AnimatedVectorDrawable (Android 12 splash API, at most 1 s), so the short
# animation is written as one too: drawable/splash_logo.xml (light) and drawable-night/splash_logo.xml (dark).
# Same canvas as the launcher foreground (scripts/logo.ps1): the 1024-unit paths in a group scaled by 33/512 and
# moved by 21, so every stroke edge stays inside the 66 dp safe circle of the 108 dp icon.
# Each part sits in a group pivoting on P. The group's scale k shrinks the part toward the pivot as the SVG does;
# the stroke width is animated to 88 / k so the line keeps its width, exactly like the SVG's moving end points.
$POP = 760
$keys = [int][math]::Ceiling($POP / 1000 * $Fps)
$parts = @('arc', 'tick-1', 'tick-2', 'needle', 'tick-3', 'tick-4')
foreach ($theme in 'light', 'dark') {
  $col = $C[$theme]
  $groups = @(); $targets = @()
  for ($j = 0; $j -lt $parts.Count; $j++) {
    $id = $parts[$j]; $name = $id -replace '-', '_'
    $scale = @(); $width = @(); $alpha = @()
    for ($i = 0; $i -le $keys; $i++) {
      $t = [math]::Min($POP, $i * 1000.0 / $Fps)
      $x = Seg $t ($j * 60) 420; $k = 0.55 + 0.45 * (Spring $x)
      $frac = N ($t / $POP)
      $scale += "        <keyframe android:fraction=`"$frac`" android:value=`"$(N $k)`" />"
      $width += "        <keyframe android:fraction=`"$frac`" android:value=`"$(N ($W / $k))`" />"
      $alpha += "        <keyframe android:fraction=`"$frac`" android:value=`"$(N (Clamp ($x / 0.25)))`" />"
    }
    $d = if ($id -eq 'arc') { "M$(Pt $R_ARC 180)A$R_ARC $R_ARC 0 0 1 $(Pt $R_ARC 270)" } else { $m = $MARKS | Where-Object { $_.id -eq $id }; "M$(Pt $R_IN $m.a)L$(Pt $m.r1 $m.a)" }
    $stroke = if ($id -eq 'arc' -or $id -eq 'needle') { $col.accent } else { $col.ink }
    $groups += @(
      "      <group android:name=`"g_$name`" android:pivotX=`"$P`" android:pivotY=`"$P`" android:scaleX=`"0.55`" android:scaleY=`"0.55`">"
      "        <path android:name=`"p_$name`" android:fillColor=`"#00000000`" android:pathData=`"$d`" android:strokeAlpha=`"0`""
      "          android:strokeColor=`"$stroke`" android:strokeLineCap=`"round`" android:strokeWidth=`"$(N ($W / 0.55))`" />"
      '      </group>') -join "`n"
    $holder = { param($prop, $kf) "      <propertyValuesHolder android:propertyName=`"$prop`" android:valueType=`"floatType`">`n$($kf -join "`n")`n      </propertyValuesHolder>" }
    $animator = { param($inner) "  <aapt:attr name=`"android:animation`">`n    <objectAnimator android:duration=`"$POP`" android:interpolator=`"@android:interpolator/linear`">`n$($inner -join "`n")`n    </objectAnimator>`n  </aapt:attr>" }
    $targets += "<target android:name=`"g_$name`">`n$(& $animator @((& $holder 'scaleX' $scale), (& $holder 'scaleY' $scale)))`n</target>"
    $targets += "<target android:name=`"p_$name`">`n$(& $animator @((& $holder 'strokeWidth' $width), (& $holder 'strokeAlpha' $alpha)))`n</target>"
  }
  $xml = @(
    '<?xml version="1.0" encoding="utf-8"?>'
    "<!-- The launch-screen logo ($theme): the 0.75 s pop, sampled at $Fps fps. Generated by scripts/logo-motion.ps1. -->"
    '<animated-vector xmlns:android="http://schemas.android.com/apk/res/android" xmlns:aapt="http://schemas.android.com/aapt">'
    '  <aapt:attr name="android:drawable">'
    '    <vector android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">'
    '      <group android:translateX="21" android:translateY="21" android:scaleX="0.064" android:scaleY="0.064">'
    ($groups -join "`n")
    '      </group>'
    '    </vector>'
    '  </aapt:attr>'
    ($targets -join "`n")
    '</animated-vector>', '') -join "`n"
  $dir = if ($theme -eq 'light') { 'drawable' } else { 'drawable-night' }
  $path = Join-Path $root "android/app/src/main/res/$dir/splash_logo.xml"
  [IO.File]::WriteAllText($path, $xml, [Text.UTF8Encoding]::new($false))
  "android $dir/splash_logo.xml: $($keys + 1) keyframes per property, $([math]::Round($xml.Length / 1024)) KB"
}
