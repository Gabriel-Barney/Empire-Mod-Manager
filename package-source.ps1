param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EmpireModManager.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version in the project.' }
$name = "EmpireModManager-$version-source"
$releaseRoot = Join-Path $PSScriptRoot 'releases'
$work = Join-Path $releaseRoot ("source-build-" + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $work $name

# Include only project inputs. Never recurse through the workspace, build caches,
# installed games, personal settings, or previous release/test output.
$files = @(
    'EmpireModManager.csproj'
    'README.md'
    'PUBLISHING.md'
    'RELEASE-NOTES.md'
    'package-release.ps1'
    'package-source.ps1'
    'test-updater.ps1'
    'index.html'
    '.github/workflows/ci.yml'
    '.github/workflows/release.yml'
    'assets/build-icon.ps1'
    'assets/empire-mod-manager.ico'
    'assets/empire-mod-manager.png'
    'assets/icon-notes.md'
)
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File)
if ($sources.Count -eq 0) { throw 'No C# source files were found.' }
$files += $sources.Name
# Git metadata helps consumers manage a checkout but is not required to build
# the application. Include it when available, including in partial uploads.
foreach ($optional in '.gitignore', '.gitattributes', 'global.json', 'LICENSE', 'LICENSE.md', 'LICENSE.txt', 'NOTICE', 'NOTICE.md', 'NOTICE.txt') {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $optional) -PathType Leaf) { $files += $optional }
}
$files = @($files | Sort-Object -Unique)

foreach ($relative in $files) {
    $source = Join-Path $PSScriptRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing source package input: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

$candidate = Join-Path $work "$name.zip"
# ZipFile includes dotfiles and .github, which must survive extraction for GitHub.
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $candidate, [IO.Compression.CompressionLevel]::Optimal, $true)
$verifyRoot = Join-Path $work 'verification'
[IO.Compression.ZipFile]::ExtractToDirectory($candidate, $verifyRoot)
$verified = Join-Path $verifyRoot $name
$extractedFiles = @(Get-ChildItem -LiteralPath $verified -Recurse -Force -File)
if ($extractedFiles.Count -ne $files.Count) { throw 'The source archive contains an unexpected file count.' }
foreach ($relative in $files) {
    $expected = Join-Path $stage $relative
    $actual = Join-Path $verified $relative
    if (-not (Test-Path -LiteralPath $actual -PathType Leaf) -or
        (Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash) {
        throw "Source archive verification failed: $relative"
    }
}

$archive = Join-Path $releaseRoot "$name.zip"
Move-Item -LiteralPath $candidate -Destination $archive -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Host "Verified source archive: $archive"
Write-Host "Included files: $($files.Count)"
Write-Host "Extracted source: $verified"
