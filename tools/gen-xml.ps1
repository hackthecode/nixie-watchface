# Generates res/raw/watchface.xml for the Nixie face.
# Layout: four big tubes (HH:MM), two bar-graph tubes (battery, steps) on the base.
# Layers, back to front: background (+ optional wooden base strip) / far cage / cathode stack / lit digits / warm-up cover /
# bar tubes / near cage / glass / reflections / engraved text. The cage layers and reflections slide with the wrist tilt.
$root = Split-Path $PSScriptRoot -Parent
function Esc($s) { [System.Security.SecurityElement]::Escape($s) }

$H  = '([IS_24_HOUR_MODE] ? [HOUR_0_23] : [HOUR_1_12])'
# previous minute's reading, for the one-second ghost of the outgoing digit when the minute rolls over
$Hp = "(([MINUTE] == 0) ? ([IS_24_HOUR_MODE] ? (([HOUR_0_23] + 23) % 24) : ((([HOUR_1_12] + 10) % 12) + 1)) : $H)"
$Mp = '(([MINUTE] + 59) % 60)'
# a = steady brightness of each tube (old tubes never match; keep in sync with NixieRender.Bright)
$tubes = @(
  @{ x = 25;  a = 255; expr = "floor($H / 10)";       prev = "floor($Hp / 10)"; date = "floor([DAY] / 10)" },
  @{ x = 123; a = 235; expr = "($H % 10)";            prev = "($Hp % 10)";      date = "([DAY] % 10)" },
  @{ x = 239; a = 247; expr = "floor([MINUTE] / 10)"; prev = "floor($Mp / 10)"; date = "floor([MONTH] / 10)" },
  @{ x = 337; a = 224; expr = "([MINUTE] % 10)";      prev = "($Mp % 10)";      date = "([MONTH] % 10)" }
)
$TY = 86; $TW = 105; $TH = 244     # tube box
$PAD = 17                          # lit sprites carry this pad on every side (NixieRender.Pad) so the halo can spill through the glass
$RX = 20; $RY = 80; $RW = 426; $RH = 256   # region covering all four tubes (NixieRender.Warm*)
$ORDER = "1608295374"              # cathode stack, rear to front

function Img($x, $y, $w, $h, $res, $alphaExpr, $extra = "") {
  "<PartImage x=`"$x`" y=`"$y`" width=`"$w`" height=`"$h`" alpha=`"0`"$extra><Transform target=`"alpha`" value=`"$(Esc $alphaExpr)`"/><Image resource=`"$res`"/></PartImage>"
}
# time digits: during second 0 the new digit is still coming up and the old one is still fading out
function TimeDigits($pre, $ind) {
  $sb = ""
  foreach ($t in $tubes) { for ($k = 0; $k -le 9; $k++) {
    $e = "($($t.expr) == $k) ? ((([SECOND] == 0) && ($($t.prev) != $k)) ? 150 : $($t.a)) : ((([SECOND] == 0) && ($($t.prev) == $k)) ? 105 : 0)"
    $sb += "$ind$(Img ($t.x - $PAD) ($TY - $PAD) ($TW + 2 * $PAD) ($TH + 2 * $PAD) "nx_$pre$k" $e)`n" } }
  $sb
}
# date digits (DD on the left pair, MM on the right pair), positioned inside the tube region
function DateDigits($pre, $ind) {
  $sb = ""
  foreach ($t in $tubes) { for ($k = 0; $k -le 9; $k++) {
    $sb += "$ind$(Img ($t.x - $PAD - $RX) ($TY - $PAD - $RY) ($TW + 2 * $PAD) ($TH + 2 * $PAD) "nx_$pre$k" "($($t.date) == $k) ? $($t.a) : 0")`n" } }
  $sb
}
function AmbientDigits($ind) {
  $sb = ""
  foreach ($t in $tubes) { for ($k = 0; $k -le 9; $k++) { $sb += "$ind$(Img $t.x $TY $TW $TH "nx_a$k" "($($t.expr) == $k) ? 255 : 0")`n" } }
  $sb
}
function Frames($list, $ind) { ($list | ForEach-Object { "$ind<Image resource=`"$_`"/>`n" }) -join "" }

# everything that depends on the gas: digits, hourly cathode cycling, date on tap
function GasOption($id, $pre) {
  # hourly anti-poisoning routine: every cathode is lit in turn for 2.5 s; the time digits are masked out meanwhile
  $cycleN = 30
  $hide = Frames (@("nx_on") + (1..$cycleN | ForEach-Object { "nx_off" })) "            "
  $cyc = ""
  for ($i = 0; $i -lt 4; $i++) {
    $seq = 0..($cycleN - 1) | ForEach-Object { "nx_$pre$($ORDER[($_ + 3 * $i) % 10])" }
    $cyc += @"
        <PartAnimatedImage x="$($tubes[$i].x - $PAD)" y="$($TY - $PAD)" width="$($TW + 2 * $PAD)" height="$($TH + 2 * $PAD)">
          <Variant mode="AMBIENT" target="alpha" value="0"/>
          <AnimationController play="ON_NEXT_HOUR" loopCount="1" beforePlaying="HIDE" afterPlaying="HIDE"/>
          <SequenceImages loopCount="1" frameRate="12" thumbnail="nx_off">
$(Frames $seq "            ")          </SequenceImages>
        </PartAnimatedImage>

"@
  }
  # date on tap: a glass-shaped mask switches on for 3 s and reveals unlit tube interiors with the date digits over the time
  # (a half-alpha fade frame gets stuck on this runtime, so the mask cuts in and out)
  $mask = Frames (@("nx_w13") + (1..30 | ForEach-Object { "nx_mk" }) + @("nx_w13")) "            "
  @"
      <ListOption id="$id">
        <Group name="time$id" x="0" y="0" width="466" height="466" alpha="255">
          <Variant mode="AMBIENT" target="alpha" value="0"/>
          <PartAnimatedImage x="0" y="0" width="466" height="466" renderMode="MASK">
            <AnimationController play="ON_NEXT_HOUR" loopCount="1" beforePlaying="FIRST_FRAME" afterPlaying="FIRST_FRAME"/>
            <SequenceImages loopCount="1" frameRate="12" thumbnail="nx_on">
$hide            </SequenceImages>
          </PartAnimatedImage>
          <Group x="0" y="0" width="466" height="466" renderMode="SOURCE">
$(TimeDigits $pre "            ")          </Group>
        </Group>

$cyc        <Group name="date$id" x="$RX" y="$RY" width="$RW" height="$RH" alpha="255">
          <Variant mode="AMBIENT" target="alpha" value="0"/>
          <PartAnimatedImage x="0" y="0" width="$RW" height="$RH" renderMode="MASK">
            <AnimationController play="TAP" loopCount="1" beforePlaying="FIRST_FRAME" afterPlaying="FIRST_FRAME"/>
            <SequenceImages loopCount="1" frameRate="10" thumbnail="nx_w13">
$mask            </SequenceImages>
          </PartAnimatedImage>
          <Group x="0" y="0" width="$RW" height="$RH" renderMode="SOURCE">
            <PartImage x="0" y="0" width="$RW" height="$RH"><Image resource="nx_cover"/></PartImage>
$(DateDigits $pre "            ")          </Group>
        </Group>
      </ListOption>

"@
}

# tilt parallax: layers at different depths slide by different amounts as the wrist rolls (x) and pitches (y)
$U = 'clamp([ACCELEROMETER_ANGLE_X] / 30, -1, 1)'
$V = 'clamp([ACCELEROMETER_ANGLE_Y] / 40, -1, 1)'
function Slide($name, $res, $px, $py) {
  @"
    <PartImage name="$name" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Transform target="x" value="$(Esc "$px * $U")"/>
      <Transform target="y" value="$(Esc "$py * $V")"/>
      <Image resource="$res"/>
    </PartImage>

"@
}
function Bar($name, $x, $widthExpr) {
  @"
      <Group name="$name" x="$x" y="382" width="120" height="14">
        <PartDraw x="0" y="0" width="120" height="14" renderMode="MASK">
          <Rectangle x="0" y="0" width="120" height="14"><Transform target="width" value="$(Esc $widthExpr)"/><Fill color="#ffffffff"/></Rectangle>
        </PartDraw>
        <PartImage x="0" y="0" width="120" height="14" renderMode="SOURCE"><Image resource="nx_bar"/></PartImage>
      </Group>

"@
}
function Engraved($x, $tpl, $expr) {
  $body = "<Template>$tpl<Parameter expression=`"$(Esc $expr)`"/></Template>"
  @"
      <PartText x="$x" y="401" width="120" height="24"><Text align="CENTER"><Font family="oswald" size="17" color="#e6000000">$body</Font></Text></PartText>
      <PartText x="$x" y="402" width="120" height="24"><Text align="CENTER"><Font family="oswald" size="17" color="#c8a8aeba">$body</Font></Text></PartText>

"@
}
$warm = Frames (0..13 | ForEach-Object { "nx_w{0:00}" -f $_ }) "        "
$STEPPCT = 'clamp([STEP_COUNT] * 100 / clamp([STEP_GOAL], 1, 100000), 1, 100)'

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- NIXIE: IN-14 style nixie tube clock, Watch Face Format v2, 466x466. Generated by tools/gen-xml.ps1 - edit that, not this. -->
<WatchFace width="466" height="466">
  <Metadata key="CLOCK_TYPE" value="DIGITAL"/>
  <Metadata key="PREVIEW_TIME" value="18:42:00"/>
  <UserConfigurations>
    <ListConfiguration id="base" displayName="cfg_base" defaultValue="0">
      <ListOption id="0" displayName="base_metal"/>
      <ListOption id="1" displayName="base_walnut"/>
      <ListOption id="2" displayName="base_mahogany"/>
    </ListConfiguration>
    <ListConfiguration id="gas" displayName="cfg_gas" defaultValue="0">
      <ListOption id="0" displayName="gas_orange"/>
      <ListOption id="1" displayName="gas_red"/>
      <ListOption id="2" displayName="gas_amber"/>
    </ListConfiguration>
  </UserConfigurations>
  <Scene backgroundColor="#ff000000">

    <PartImage name="bg" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_bg"/>
    </PartImage>

    <!-- wooden bases replace the strip from just above the plinth down -->
    <ListConfiguration id="base">
      <ListOption id="0"></ListOption>
      <ListOption id="1">
        <PartImage x="0" y="340" width="466" height="126" alpha="255"><Variant mode="AMBIENT" target="alpha" value="0"/><Image resource="nx_base1"/></PartImage>
      </ListOption>
      <ListOption id="2">
        <PartImage x="0" y="340" width="466" height="126" alpha="255"><Variant mode="AMBIENT" target="alpha" value="0"/><Image resource="nx_base2"/></PartImage>
      </ListOption>
    </ListConfiguration>

    <!-- far half of the anode cage (slides against the digits), then the cathode stack, rods and mica in the digit plane -->
$(Slide "backMesh" "nx_back" -4 -2)    <PartImage name="inner" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_inner"/>
    </PartImage>

    <!-- lit cathodes, hourly cycling and the date view, in the chosen gas colour -->
    <ListConfiguration id="gas">
$(GasOption 0 "d")$(GasOption 1 "r")$(GasOption 2 "y")    </ListConfiguration>

    <!-- always-on: thin dim wire outlines only, no tubes/glow (minimal lit OLED pixels) -->
    <Group name="ambientDigits" x="0" y="0" width="466" height="466" alpha="0">
      <Variant mode="AMBIENT" target="alpha" value="170"/>
$(AmbientDigits "      ")    </Group>

    <!-- colon neon bulbs (steady: no per-second redraw) -->
    <Group name="colon" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <PartImage x="216" y="176" width="34" height="40" alpha="255"><Image resource="nx_bulb"/></PartImage>
      <PartImage x="216" y="234" width="34" height="40" alpha="255"><Image resource="nx_bulb"/></PartImage>
    </Group>

    <!-- warm-up: dark patches over the glass interiors shrink away once each time the face becomes visible -->
    <PartAnimatedImage name="warmup" x="$RX" y="$RY" width="$RW" height="$RH">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <AnimationController play="ON_VISIBLE" loopCount="1" beforePlaying="FIRST_FRAME" afterPlaying="HIDE"/>
      <SequenceImages loopCount="1" frameRate="20" thumbnail="nx_w13">
$warm      </SequenceImages>
    </PartAnimatedImage>

    <!-- bar-graph tubes: battery on the left, steps toward the daily goal on the right -->
    <Group name="bars" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
$(Bar "batBar" 72 "1.2 * clamp([BATTERY_PERCENT], 1, 100)")$(Bar "stepBar" 274 "1.2 * $STEPPCT")    </Group>

    <!-- near half of the cage, the glass itself, then the reflections that slide across it -->
$(Slide "frontMesh" "nx_mesh" 2 1)    <PartImage name="front" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_front"/>
    </PartImage>

$(Slide "highlights" "nx_hl" 5 3)    <!-- values engraved under the bar tubes -->
    <Group name="engraved" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
$(Engraved 72 "%s%%" "[BATTERY_PERCENT]")$(Engraved 274 "%s" "[STEP_COUNT]")    </Group>

  </Scene>
</WatchFace>
"@
Set-Content (Join-Path $root "res\raw\watchface.xml") $xml -NoNewline -Encoding utf8NoBOM
Write-Host "watchface.xml written"
