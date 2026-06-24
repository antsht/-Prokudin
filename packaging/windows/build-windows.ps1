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

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$portableZip = Join-Path $OutputDir "Prokudin-$Version-win-x64-portable.zip"
$cliZip = Join-Path $OutputDir "Prokudin-Cli-$Version-win-x64.zip"

Compress-Archive -Path (Join-Path $GuiPublishDir 'Prokudin.exe') -DestinationPath $portableZip -Force
Compress-Archive -Path (Join-Path $CliPublishDir 'prokudin.exe') -DestinationPath $cliZip -Force

if (-not (Test-Path $InnoSetupCompiler)) {
    throw "Inno Setup compiler not found at: $InnoSetupCompiler"
}

$iss = Join-Path $PSScriptRoot 'prokudin.iss'
& $InnoSetupCompiler "/DMyAppVersion=$Version" "/DPublishDir=$GuiPublishDir" $iss

$installerDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..\dist\installer')).Path
Get-ChildItem -Path $installerDir -Filter "Prokudin-$Version-win-x64-setup.exe" | ForEach-Object {
    Copy-Item $_.FullName -Destination $OutputDir -Force
}

Write-Host "Created $portableZip"
Write-Host "Created $cliZip"
Write-Host "Created installer in dist/installer"
