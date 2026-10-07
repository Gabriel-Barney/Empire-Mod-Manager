# Publishing Empire Mod Manager source

The source package contains the C# application, project file, icon artwork and rebuild script, documentation, project website, Git configuration files, and GitHub build/release workflows. It excludes generated binaries, build caches, diagnostic output, personal settings, and game/mod files.

## Build from the source ZIP

1. Extract the ZIP into a writable folder on Windows.
2. Install the **.NET 9 SDK** from [Microsoft's .NET downloads](https://dotnet.microsoft.com/download/dotnet/9.0).
3. Open PowerShell in the extracted folder containing `EmpireModManager.csproj`.
4. Run:

```powershell
dotnet build EmpireModManager.csproj -c Release
dotnet publish EmpireModManager.csproj -c Release -o dist
$check = Start-Process -FilePath .\dist\EmpireModManager.exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Application self-tests failed. Inspect dist/self-test-results.txt and dist/error.log.' }
```

Open `dist/EmpireModManager.exe` to use the development build. The .NET 9 Windows Desktop Runtime must be installed to run it; the SDK includes that runtime. No game installation is needed to build or run the self-tests. Using the app to launch games requires your own game and installed mods.

## Make another source package

```powershell
./package-source.ps1
```

This writes `releases/EmpireModManager-<version>-source.zip` and its `.zip.sha256` checksum. The script verifies every packaged file after extraction and preserves `.github`, `.gitignore`, and `.gitattributes`. Intermediate copies stay in `releases/source-build-*` for inspection. Distribute the final ZIP and checksum.

All root-level `.cs` files are included. If you add source subdirectories or other required assets, update the input list in `package-source.ps1` before packaging.

## Publish the source

For a downloadable source release, attach the source ZIP and checksum to a GitHub release. For a source repository, extract the archive and commit the **contents of its top-level folder** at the repository root, including the dotfiles and `.github` directory. The archive is a source snapshot and does not contain Git history.

The existing updater points to `Gabriel-Barney/Empire-Mod-Manager`. If publishing a fork that will offer its own application updates, change `GithubUpdater.Repository` in `Updater.cs`, the repository label in `UpdateUi.cs`, and the documentation links before distributing that build.

No project `LICENSE` file was present when this source package was prepared. Choose and add your license if you want to grant permission for others to reuse or redistribute the project. The source packager includes `LICENSE`, `LICENSE.md`, or `LICENSE.txt` automatically when present.

## Publish a Windows application release

```powershell
./package-release.ps1
./package-source.ps1
```

The Windows packager builds a self-contained x64 app, includes runtime notices, verifies its archive, and runs the built-in self-tests and UI smoke checks. It needs internet access for runtime downloads when they are not cached and an interactive Windows desktop for the UI checks.

Upload the four final assets from `releases/`:

- `EmpireModManager-<version>-win-x64.zip`
- `EmpireModManager-<version>-win-x64.zip.sha256`
- `EmpireModManager-<version>-source.zip`
- `EmpireModManager-<version>-source.zip.sha256`

Use a release tag matching the project version, such as `v1.2.4` for the current snapshot. For a new version, update `Version`, `AssemblyVersion`, and `FileVersion` in `EmpireModManager.csproj`, update `RELEASE-NOTES.md` and versioned links in `README.md`, then commit and tag that version.

With GitHub Actions enabled, the included `Publish Windows release` workflow builds and tests the application and attaches both packages and their checksums when a matching version tag is pushed. Ordinary pushes to `main` and pull requests run the CI workflow and upload both packages as build artifacts. See [the workflows](.github/workflows) for their exact triggers.
