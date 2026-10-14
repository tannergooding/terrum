[CmdletBinding()]
param(
    [switch] $Warp,
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$artifactDirectory = Join-Path $repositoryRoot "artifacts"
$outputDirectory = Join-Path $artifactDirectory "dx11-pipeline"
$compilerPath = Join-Path $artifactDirectory "bin\sources\terrumc\$Configuration\net11.0\terrumc.dll"

if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw "Terrum compiler not found at '$compilerPath'. Build sources\terrumc\terrumc.csproj first."
}

$sourceName = if ($Warp) { "main.terrum" } else { "main-hardware.terrum" }
$executableName = if ($Warp) { "triangle-warp-resize.exe" } else { "triangle-hardware-resize.exe" }

$sourcePath = Join-Path $PSScriptRoot $sourceName
$shaderPath = Join-Path $PSScriptRoot "triangle.hlsl"
$executablePath = Join-Path $outputDirectory $executableName

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Terrum entry point not found at '$sourcePath'."
}
if (-not (Test-Path -LiteralPath $shaderPath -PathType Leaf)) {
    throw "DX11 shader not found at '$shaderPath'."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
& dotnet $compilerPath build-native-import $sourcePath -o $executablePath --runtime checked-exceptions
if ($LASTEXITCODE -ne 0) {
    throw "Terrum failed to build '$sourceName' (exit code $LASTEXITCODE)."
}

Copy-Item -LiteralPath $shaderPath -Destination (Join-Path $outputDirectory "triangle.hlsl") -Force
Write-Output "Built '$executablePath' and staged 'triangle.hlsl'."
