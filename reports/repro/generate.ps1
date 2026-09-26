[CmdletBinding()]
param(
  [ValidateRange(0, 10000)][int] $Calls = 128,
  [switch] $Span,
  [string] $OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
if ($OutputDirectory -eq "") {
  $root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
  $OutputDirectory = Join-Path $root "artifacts\profiles\analyzer-repro"
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <ReportAnalyzer>true</ReportAnalyzer>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $OutputDirectory "AnalyzerRepro.csproj")

# Keep the generated project independent of surrounding build settings.
'<Project />' | Set-Content -Path (Join-Path $OutputDirectory "Directory.Build.props")
'<Project />' | Set-Content -Path (Join-Path $OutputDirectory "Directory.Build.targets")

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("using System;")
$lines.Add("using System.Collections.Generic;")
$lines.Add("public sealed class Encoder")
$lines.Add("{")
$lines.Add("    private readonly List<byte> _bytes = new();")
if ($Span) {
  $lines.Add("    private void Bytes(params ReadOnlySpan<byte> values) => _bytes.AddRange(values);")
} else {
  $lines.Add("    private void Bytes(params byte[] values) => _bytes.AddRange(values);")
}
$lines.Add("    public int Emit()")
$lines.Add("    {")
for ($i = 0; $i -lt $Calls; $i++) {
  $lines.Add("        Bytes(0x48, 0x89, 0xC8, 0x48, 0x89, 0xC8, 0x90, 0x90);")
}
$lines.Add("        return _bytes.Count;")
$lines.Add("    }")
$lines.Add("}")
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory "Encoder.cs"), $lines)
Write-Output $OutputDirectory
