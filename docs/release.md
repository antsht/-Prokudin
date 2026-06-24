# Release Process

This document describes how maintainers publish Prokudin binaries to GitHub Releases.

## Prerequisites

- Merge to `main` with green CI (`.github/workflows/ci.yml`)
- Updated `<Version>` in `Directory.Build.props` and entry in `CHANGELOG.md`
- Write access to the GitHub repository

## Standard release

```powershell
git checkout main
git pull

# Confirm version in Directory.Build.props matches CHANGELOG header

git tag v0.9.0
git push origin v0.9.0
```

Pushing a `v*` tag starts `.github/workflows/release.yml`, which:

1. Builds and packages GUI + CLI for `win-x64` and `linux-x64`
2. Uploads artifacts to a new GitHub Release
3. Attaches `SHA256SUMS.txt`

## Canary / manual release

Use `workflow_dispatch` on the **Release** workflow in GitHub Actions. Optionally set the `version` input when not tagging.

For a dry run locally:

```powershell
dotnet publish src/Prokudin.Gui/Prokudin.Gui.csproj -c Release -r win-x64 -o dist/gui
dotnet publish src/Prokudin.Cli/Prokudin.Cli.csproj -c Release -r win-x64 -o dist/cli
./packaging/windows/build-windows.ps1 -Version 0.9.0 -GuiPublishDir dist/gui -CliPublishDir dist/cli -OutputDir dist/release
```

On Linux:

```bash
dotnet publish src/Prokudin.Gui/Prokudin.Gui.csproj -c Release -r linux-x64 -o dist/gui
dotnet publish src/Prokudin.Cli/Prokudin.Cli.csproj -c Release -r linux-x64 -o dist/cli
./packaging/linux/build-portable.sh 0.9.0 dist/gui dist/cli dist/release
./packaging/linux/build-appimage.sh 0.9.0 dist/gui dist/release
```

## Post-release smoke test

| Platform | Check |
| --- | --- |
| Windows | Run `Prokudin-*-win-x64-setup.exe`; launch `Prokudin.exe`; `prokudin.exe --help` |
| Linux | `chmod +x Prokudin-*.AppImage && ./Prokudin-*.AppImage`; `tar -xzf` portable + `./prokudin --help` |
| GUI | Help → Check for updates (should report up to date on current release) |

## Artifact layout

See [`docs/superpowers/specs/2026-06-24-distribution-design.md`](superpowers/specs/2026-06-24-distribution-design.md).

## macOS

Not automated yet. Requires OpenCV osx runtime validation, codesign, and notarization before publishing.
