# Generates res/raw/watchface.xml for the Nixie face (one PartImage per digit per tube).
# Layout: four big tubes (HH:MM) + a battery bar-graph tube with percent underneath.
$root = Split-Path $PSScriptRoot -Parent
$H = '([IS_24_HOUR_MODE] ? [HOUR_0_23] : [HOUR_1_12])'
$tubes = @(
  @{ x = 25;  y = 86; w = 105; h = 244; img = "nx_d"; expr = "floor($H / 10)" },
  @{ x = 123; y = 86; w = 105; h = 244; img = "nx_d"; expr = "($H % 10)" },
  @{ x = 239; y = 86; w = 105; h = 244; img = "nx_d"; expr = "floor([MINUTE] / 10)" },
  @{ x = 337; y = 86; w = 105; h = 244; img = "nx_d"; expr = "([MINUTE] % 10)" }
)
$ambient = $tubes | ForEach-Object { $a = $_.Clone(); $a.img = "nx_a"; $a }
function Digits($list) {
  $sb = ""
  foreach ($t in $list) { for ($k = 0; $k -le 9; $k++) {
    $e = [System.Security.SecurityElement]::Escape("($($t.expr) == $k) ? 255 : 0")
    $sb += "      <PartImage x=`"$($t.x)`" y=`"$($t.y)`" width=`"$($t.w)`" height=`"$($t.h)`" alpha=`"0`"><Transform target=`"alpha`" value=`"$e`"/><Image resource=`"$($t.img)$k`"/></PartImage>`n" } }
  return $sb
}
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

    <!-- glass, front mesh, highlights -->
    <PartImage name="front" x="0" y="0" width="466" height="466" alpha="255">
      <Variant mode="AMBIENT" target="alpha" value="0"/>
      <Image resource="nx_front"/>
    </PartImage>

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
