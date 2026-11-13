param([switch]$Test)
$ErrorActionPreference = 'Stop'
$csc = @('Framework64', 'Framework') |
    ForEach-Object { Join-Path $env:WINDIR "Microsoft.NET\$_\v4.0.30319\csc.exe" } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$csc) { throw 'The Windows .NET Framework 4.x compiler was not found.' }

$bin = Join-Path $PSScriptRoot 'dist'
$null = New-Item -ItemType Directory -Path $bin -Force
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
$options = @('/nologo', '/noconfig', '/optimize+', '/warnaserror+', '/codepage:65001',
    '/platform:anycpu', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
$app = Join-Path $bin 'PeripheralCharge.exe'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
& $csc @options /target:winexe "/win32manifest:$manifest" "/out:$app" @sources
if ($LASTEXITCODE) { throw "Application compilation failed ($LASTEXITCODE)." }
Write-Output $app

if ($Test) {
    $checks = Join-Path $bin 'ProtocolTests.exe'
    $testSource = Join-Path $PSScriptRoot 'tests\ProtocolTests.cs'
    & $csc @options /target:exe /main:PeripheralCharge.ProtocolTests "/out:$checks" @sources $testSource
    if ($LASTEXITCODE) { throw "Test compilation failed ($LASTEXITCODE)." }
    & $checks
    if ($LASTEXITCODE) { throw "Tests failed ($LASTEXITCODE)." }
}
