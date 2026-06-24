param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $GuiPublishDir,

    [Parameter(Mandatory = $true)]
    [string] $CliPublishDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputDir,

    [string] $InnoSetupCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$GuiPublishDir = (Resolve-Path $GuiPublishDir).Path
$CliPublishDir = (Resolve-Path $CliPublishDir).Path

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

$guiExe = Join-Path $GuiPublishDir 'Prokudin.exe'
$cliExe = Join-Path $CliPublishDir 'prokudin.exe'
if (-not (Test-Path $guiExe)) {
    throw "GUI publish output not found: $guiExe"
}

if (-not (Test-Path $cliExe)) {
    throw "CLI publish output not found: $cliExe"
}

$portableZip = Join-Path $OutputDir "Prokudin-$Version-win-x64-portable.zip"
$cliZip = Join-Path $OutputDir "Prokudin-Cli-$Version-win-x64.zip"

Compress-Archive -Path $guiExe -DestinationPath $portableZip -Force
Compress-Archive -Path $cliExe -DestinationPath $cliZip -Force

if (-not (Test-Path $InnoSetupCompiler)) {
    throw "Inno Setup compiler not found at: $InnoSetupCompiler"
}

$installerDir = Join-Path $repoRoot 'dist\installer'
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

$iss = Join-Path $PSScriptRoot 'prokudin.iss'
& $InnoSetupCompiler "/DMyAppVersion=$Version" "/DPublishDir=$GuiPublishDir" "/DOutputDir=$installerDir" $iss

$setupExe = Join-Path $installerDir "Prokudin-$Version-win-x64-setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Installer was not created: $setupExe"
}

Copy-Item $setupExe -Destination $OutputDir -Force

Write-Host "Created $portableZip"
Write-Host "Created $cliZip"
Write-Host "Created $setupExe"
