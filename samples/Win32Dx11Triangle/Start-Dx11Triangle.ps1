[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Warp,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$outputDirectory = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\..\artifacts\bin\samples\Win32Dx11Triangle\$Configuration"))
$driver = if ($Warp) { 'WARP' } else { 'hardware' }
$sampleMode = 'event-driven resize'
$executableName = if ($Warp) { 'triangle-warp-resize.exe' } else { 'triangle-hardware-resize.exe' }
$executablePath = Join-Path $outputDirectory $executableName
$shaderPath = Join-Path $outputDirectory 'triangle.hlsl'

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "The selected $driver pipeline executable was not found: $executablePath"
}

if (-not (Test-Path -LiteralPath $shaderPath -PathType Leaf)) {
    throw "The shader asset adjacent to the selected executable was not found: $shaderPath"
}

Write-Output "Selected driver: $driver"
Write-Output "Selected mode: $sampleMode"
Write-Output "Executable: $executablePath"
Write-Output "Shader: $shaderPath"

if ($PSCmdlet.ShouldProcess($executablePath, "Start the $driver $sampleMode DX11 sample")) {
    Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory
}
