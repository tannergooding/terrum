[CmdletBinding()]
param(
  [ValidateRange(1, 10000)][int] $Conditions = 256,
  [ValidateRange(2, 100)][int] $StateWidth = 80,
  [string] $OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
if ($OutputDirectory -eq "") {
  $root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
  $OutputDirectory = Join-Path $root "artifacts\profiles\ca1508-repro"
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <Nullable>enable</Nullable>
    <ReportAnalyzer>true</ReportAnalyzer>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $OutputDirectory "AnalyzerRepro.csproj")
'<Project />' | Set-Content -Path (Join-Path $OutputDirectory "Directory.Build.props")
'<Project />' | Set-Content -Path (Join-Path $OutputDirectory "Directory.Build.targets")

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace AnalyzerRepro;")
$lines.Add("public static class Conditions")
$lines.Add("{")
$lines.Add("    public static int Evaluate(int input)")
$lines.Add("    {")
$lines.Add("        var score = 0;")
for ($j = 0; $j -lt $StateWidth; $j++) {
  $lines.Add("        object? v$j = input;")
}
$last = $StateWidth - 1
for ($i = 0; $i -lt $Conditions; $i++) {
  $lines.Add("        if (input == $i)")
  $lines.Add("        {")
  $lines.Add("            v0 = new object();")
  for ($j = 1; $j -lt $StateWidth; $j++) {
    $previous = $j - 1
    $lines.Add("            v$j = v$previous;")
  }
  $lines.Add("        }")
  $lines.Add("        else")
  $lines.Add("        {")
  $lines.Add("            v0 = v$last;")
  for ($j = 1; $j -lt $StateWidth; $j++) {
    $previous = $j - 1
    $lines.Add("            v$j = v$previous;")
  }
  $lines.Add("        }")
  $lines.Add("        if (v$last == v0)")
  $lines.Add("        {")
  $lines.Add("            score++;")
  $lines.Add("        }")
}
$lines.Add("        return score;")
$lines.Add("    }")
$lines.Add("}")
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory "Conditions.cs"), $lines)
Write-Output $OutputDirectory
