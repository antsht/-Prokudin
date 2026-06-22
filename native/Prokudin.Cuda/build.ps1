param(
    [string] $Configuration = "Release",
    [string] $CudaArchitecture = "sm_86"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $root "bin"
New-Item -ItemType Directory -Force -Path $output | Out-Null

$nvcc = (Get-Command nvcc -ErrorAction Stop).Source
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "vswhere.exe not found. Install Visual Studio C++ build tools."
}

$vsInstall = & $vswhere -version "[17.0,18.0)" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsInstall) {
    throw "Visual Studio 2022 C++ build tools not found."
}

$vcvars = Join-Path $vsInstall "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw "vcvars64.bat not found at $vcvars."
}

$source = Join-Path $root "ProkudinCuda.cu"
$dll = Join-Path $output "Prokudin.Cuda.dll"
$optimization = if ($Configuration -ieq "Debug") { "-G" } else { "-O3" }
$command = "`"$vcvars`" && `"$nvcc`" $optimization -arch=$CudaArchitecture -cudart shared -Xcompiler /MD -shared -o `"$dll`" `"$source`""

cmd /c $command
if ($LASTEXITCODE -ne 0) {
    throw "nvcc failed with exit code $LASTEXITCODE."
}

Write-Host "Built $dll"
