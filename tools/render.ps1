# Compiles NixieRender.cs and renders every bitmap into res/drawable.
Add-Type -AssemblyName System.Drawing
$src = Get-Content (Join-Path $PSScriptRoot "NixieRender.cs") -Raw
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing, System.Drawing.Common, System.Drawing.Primitives, System.Runtime.InteropServices, System.Collections, System.Runtime, System.Private.Windows.GdiPlus, System.Private.Windows.Core, System.ComponentModel.Primitives, System.Runtime.Extensions -IgnoreWarnings
$out = (Resolve-Path (Join-Path $PSScriptRoot "..\res\drawable")).Path
[NixieRender]::RenderAll($out)
Write-Host "rendered to $out"
