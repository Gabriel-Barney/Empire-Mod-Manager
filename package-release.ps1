param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
$oldCliHome = $env:DOTNET_CLI_HOME
$oldPackages = $env:NUGET_PACKAGES
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.buildhome'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.buildhome\.nuget\packages'
    [xml]$project = Get-Content 'EmpireModManager.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version in the project.' }
    $name = "EmpireModManager-$version-win-x64"
    $releaseRoot = Join-Path $PSScriptRoot 'releases'
    $work = Join-Path $releaseRoot ("build-" + [guid]::NewGuid().ToString('N'))
    $stage = Join-Path $work $name
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    & dotnet publish EmpireModManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    Copy-Item README.md, RELEASE-NOTES.md -Destination $stage

    # Preserve the license and third-party notices from the exact runtime packages used.
    $runtime = Get-Content (Join-Path $stage 'EmpireModManager.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $package = "$($framework.name).Runtime.win-x64".ToLowerInvariant()
        $packageDir = Join-Path $env:NUGET_PACKAGES "$package\$($framework.version)"
        $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match 'LICENSE|ThirdPartyNotices|THIRD-PARTY' })
        if ($notices.Count -eq 0) { throw "Missing runtime notices: $package" }
        $noticeDir = Join-Path $stage ("third-party-notices\$package-$($framework.version)")
        New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
        $notices | Copy-Item -Destination $noticeDir
    }
    # The updater verifies every application file and never replaces data/.
    $manifestFiles = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        $manifestFiles[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    @{ Version = $version; Files = $manifestFiles } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'update-manifest.json') -Encoding utf8
    $candidate = Join-Path $work "$name.zip"
    Compress-Archive -LiteralPath $stage -DestinationPath $candidate -CompressionLevel Optimal
    $verifyRoot = Join-Path $work 'verification'
    Expand-Archive -LiteralPath $candidate -DestinationPath $verifyRoot
    $verified = Join-Path $verifyRoot $name
    $unexpected = @(Get-ChildItem -LiteralPath $verified -Recurse | Where-Object { $_.Name -eq 'data' -or $_.Extension -in '.pdb', '.log' -or $_.Name -match 'report|preview|test-results' })
    if ($unexpected.Count) { throw 'Unexpected personal data or diagnostic files in release.' }
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $verified $relative)).Hash) {
            throw "Archive verification failed: $relative"
        }
    }
    foreach ($check in '--self-test', '--smoke-test') {
        Write-Host "Running $check..."
        $process = Start-Process -FilePath (Join-Path $verified 'EmpireModManager.exe') -ArgumentList $check -WorkingDirectory $verified -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "$check timed out." }
        if ($process.ExitCode -ne 0) {
            $errorLog = Join-Path $verified 'error.log'
            if (Test-Path -LiteralPath $errorLog) { Write-Host (Get-Content -LiteralPath $errorLog -Raw) }
            throw "$check failed with exit code $($process.ExitCode); inspect $verified"
        }
        Write-Host "$check passed."
    }
    $archive = Join-Path $releaseRoot "$name.zip"
    Move-Item -LiteralPath $candidate -Destination $archive -Force
    $checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    Write-Host "Verified release: $archive"
    Write-Host "Check results: $verified"
} finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldPackages
    Pop-Location
}
