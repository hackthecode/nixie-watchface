# Generates res/raw/watchface.xml for the Nixie face (one PartImage per digit per tube).
# Layout: four big tubes (HH:MM) + a battery bar-graph tube with percent underneath.
$root = Split-Path $PSScriptRoot -Parent
$H = '([IS_24_HOUR_MODE] ? [HOUR_0_23] : [HOUR_1_12])'
# previous minute's reading, for the one-second ghost of the outgoing digit when the minute rolls over
$Hp = "(([MINUTE] == 0) ? ([IS_24_HOUR_MODE] ? (([HOUR_0_23] + 23) % 24) : ((([HOUR_1_12] + 10) % 12) + 1)) : $H)"
$Mp = '(([MINUTE] + 59) % 60)'
# a = steady brightness of each tube (old tubes never match; keep in sync with NixieRender.Bright)
$tubes = @(
  @{ x = 25;  y = 86; w = 105; h = 244; img = "nx_d"; a = 255; expr = "floor($H / 10)";        prev = "floor($Hp / 10)" },
  @{ x = 123; y = 86; w = 105; h = 244; img = "nx_d"; a = 235; expr = "($H % 10)";             prev = "($Hp % 10)" },
  @{ x = 239; y = 86; w = 105; h = 244; img = "nx_d"; a = 247; expr = "floor([MINUTE] / 10)";  prev = "floor($Mp / 10)" },
  @{ x = 337; y = 86; w = 105; h = 244; img = "nx_d"; a = 224; expr = "([MINUTE] % 10)";       prev = "($Mp % 10)" }
)
$ambient = $tubes | ForEach-Object { $a = $_.Clone(); $a.img = "nx_a"; $a.prev = $null; $a.a = 255; $a }
# lit sprites carry a 17 px pad on every side (NixieRender.Pad) so the halo can spill through the glass
foreach ($t in $tubes) { $t.x -= 17; $t.y -= 17; $t.w += 34; $t.h += 34 }
# tilt parallax: layers at different depths slide by different amounts as the wrist rolls
$U = 'clamp([ACCELEROMETER_ANGLE_X] / 30, -1, 1)'
function Slide($name, $res, $px) {
  $e = [System.Security.SecurityElement]::Escape("$px * $U")
  "    <PartImage name=`"$name`" x=`"0`" y=`"0`" width=`"466`" height=`"466`" alpha=`"255`">`n      <Variant mode=`"AMBIENT`" target=`"alpha`" value=`"0`"/>`n      <Transform target=`"x`" value=`"$e`"/>`n      <Image resource=`"$res`"/>`n    </PartImage>`n"
}
function Digits($list) {
  $sb = ""
  foreach ($t in $list) { for ($k = 0; $k -le 9; $k++) {
    # during second 0 the new digit is still coming up and the old one is still fading out
    $raw = if ($t.prev) { "($($t.expr) == $k) ? ((([SECOND] == 0) && ($($t.prev) != $k)) ? 150 : $($t.a)) : ((([SECOND] == 0) && ($($t.prev) == $k)) ? 105 : 0)" } else { "($($t.expr) == $k) ? 255 : 0" }
    $e = [System.Security.SecurityElement]::Escape($raw)
    $sb += "      <PartImage x=`"$($t.x)`" y=`"$($t.y)`" width=`"$($t.w)`" height=`"$($t.h)`" alpha=`"0`"><Transform target=`"alpha`" value=`"$e`"/><Image resource=`"$($t.img)$k`"/></PartImage>`n" } }
  return $sb
}
$warm = ""; for ($n = 0; $n -le 13; $n++) { $warm += "        <Image resource=`"nx_w{0:00}`"/>`n" -f $n }
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- NIXIE: photoreal IN-14 style nixie tube clock, Watch Face Format v2, 466x466 -->
<WatchFace width="466" height="466">
  <Metadata key="CLOCK_TYPE" value="DIGITAL"/>
  <Metadata key="PREVIEW_TIME" value="18:42:00"/>
  <Scene backgroundColor="#ff000000">

    <PartImage name="bg" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_bg"/>
    </PartImage>

    <!-- far half of the anode cage (slides against the digits), then the cathode stack, rods and mica in the digit plane -->
$(Slide "backMesh" "nx_back" -4)
    <PartImage name="inner" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_inner"/>
    </PartImage>

    <!-- hours + minutes: lit cathodes with full glow (interactive only) -->
    <Group name="bigDigits" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
$(Digits $tubes)    </Group>

    <!-- always-on: thin dim wire outlines only, no tubes/glow (minimal lit OLED pixels) -->
    <Group name="ambientDigits" x="0" y="0" width="466" height="466" alpha="0">
      <Variant mode="AMBIENT" target="alpha" value="170"/>
$(Digits $ambient)    </Group>

    <!-- colon neon bulbs (steady: no per-second redraw) -->
    <Group name="colon" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <PartImage x="216" y="176" width="34" height="40" alpha="255"><Image resource="nx_bulb"/></PartImage>
      <PartImage x="216" y="234" width="34" height="40" alpha="255"><Image resource="nx_bulb"/></PartImage>
    </Group>

    <!-- warm-up: dark covers over the glass interiors flicker away once each time the face becomes visible -->
    <PartAnimatedImage name="warmup" x="20" y="80" width="426" height="256">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <AnimationController play="ON_VISIBLE" loopCount="1" beforePlaying="FIRST_FRAME" afterPlaying="HIDE"/>
      <SequenceImages loopCount="1" frameRate="20" thumbnail="nx_w13">
$warm      </SequenceImages>
    </PartAnimatedImage>

    <!-- bar-graph tube: battery -->
    <Group name="bars" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Group name="batBar" x="173" y="382" width="120" height="14">
        <PartDraw x="0" y="0" width="120" height="14" renderMode="MASK">
          <Rectangle x="0" y="0" width="120" height="14"><Transform target="width" value="1.2 * clamp([BATTERY_PERCENT], 1, 100)"/><Fill color="#ffffffff"/></Rectangle>
        </PartDraw>
        <PartImage x="0" y="0" width="120" height="14" renderMode="SOURCE"><Image resource="nx_bar"/></PartImage>
      </Group>
    </Group>

    <!-- near half of the cage, the glass itself, then the reflections that slide across it -->
$(Slide "frontMesh" "nx_mesh" 2)
    <PartImage name="front" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_front"/>
    </PartImage>

$(Slide "highlights" "nx_hl" 5)
    <!-- battery percent engraved into the metal under the bar tube -->
    <Group name="engraved" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <PartText x="173" y="401" width="120" height="24">
        <Text align="CENTER"><Font family="oswald" size="17" color="#e6000000"><Template>%s%%<Parameter expression="[BATTERY_PERCENT]"/></Template></Font></Text>
      </PartText>
      <PartText x="173" y="402" width="120" height="24">
        <Text align="CENTER"><Font family="oswald" size="17" color="#c8a8aeba"><Template>%s%%<Parameter expression="[BATTERY_PERCENT]"/></Template></Font></Text>
      </PartText>
    </Group>

  </Scene>
</WatchFace>
"@
Set-Content (Join-Path $root "res\raw\watchface.xml") $xml -NoNewline -Encoding utf8NoBOM
Write-Host "watchface.xml written"
