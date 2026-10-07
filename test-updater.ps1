param([Parameter(Mandatory = $true)][string]$Archive)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = [IO.Path]::GetFullPath($PSScriptRoot)
$work = Join-Path $workspace ("test-results\updater-integration-" + [guid]::NewGuid().ToString('N'))
$unpacked = Join-Path $work 'unpacked'
$stage = Join-Path $work 'package'
$target = Join-Path $work 'target'
$parent = $null
$helper = $null
$restarted = $null
try {
    Expand-Archive -LiteralPath $Archive -DestinationPath $unpacked
    $packages = @(Get-ChildItem -LiteralPath $unpacked -Directory)
    if ($packages.Count -ne 1) { throw 'Expected one release folder.' }
    Move-Item -LiteralPath $packages[0].FullName -Destination $stage
    $manifest = Get-Content -LiteralPath (Join-Path $stage 'update-manifest.json') -Raw | ConvertFrom-Json
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $target -Recurse
    # An old payload makes it possible to prove the helper waited and replaced it.
    Set-Content -LiteralPath (Join-Path $target 'EmpireModManager.dll') -Value 'old test payload' -Encoding ascii
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $target 'EmpireModManager.dll')).Hash
    New-Item -ItemType Directory -Path (Join-Path $target 'data') -Force | Out-Null
    $saved = @{
        'presets.json' = '{"Settings":{"GameRoot":"","WorkshopRoots":[]},"Presets":[{"Name":"Updater integration preset","Game":"FoC","Mods":[]}],"SelectedPreset":0}'
        'updates.json' = '{"CheckOnLaunch":false}'
        'listings.json' = '{}'
        'organization.json' = '[{"Id":"uncategorized","Name":"Uncategorized","Paths":[]}]'
    }
    foreach ($entry in $saved.GetEnumerator()) {
        Set-Content -LiteralPath (Join-Path $target "data\$($entry.Key)") -Value $entry.Value -Encoding utf8 -NoNewline
    }
    $dataHashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $target 'data') -File) {
        $dataHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    $parent = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList '-NoProfile -Command "Start-Sleep -Seconds 30"' -WindowStyle Hidden -PassThru
    $jobPath = Join-Path $work 'job.json'
    @{ Stage = $stage; Target = $target; Work = $work; Version = $manifest.Version; ParentId = $parent.Id; ParentStartedUtc = $parent.StartTime.ToUniversalTime().Ticks } |
        ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding utf8
    $helper = Start-Process -FilePath (Join-Path $stage 'EmpireModManager.exe') -ArgumentList @('--apply-update', ('"{0}"' -f $jobPath)) -WorkingDirectory $stage -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    if ($helper.HasExited -or (Get-FileHash -LiteralPath (Join-Path $target 'EmpireModManager.dll')).Hash -ne $oldHash) {
        throw 'The helper did not wait for the original process to exit.'
    }
    Stop-Process -Id $parent.Id
    if (-not $helper.WaitForExit(30000)) { throw 'The updater helper timed out.' }
    if ($helper.ExitCode -ne 0) { throw "The helper failed. Inspect $work" }
    $expectedExe = Join-Path $target 'EmpireModManager.exe'
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $restarted = Get-Process -Name EmpireModManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $expectedExe } | Select-Object -First 1
        if ($restarted -and $restarted.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $restarted -or $restarted.MainWindowHandle -eq 0) { throw 'The updated application did not open after installation.' }
    foreach ($property in $manifest.Files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $property.Name)).Hash -ne $property.Value) { throw "Installed hash mismatch: $($property.Name)" }
    }
    foreach ($entry in $dataHashes.GetEnumerator()) {
        if ((Get-FileHash -LiteralPath (Join-Path $target "data\$($entry.Key)")).Hash -ne $entry.Value) { throw "User data changed: $($entry.Key)" }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $work 'backup\EmpireModManager.dll')).Hash -ne $oldHash) { throw 'The previous payload was not backed up.' }
    @(
        'PASS: The helper waits for the original process to exit.'
        'PASS: The helper installs every verified file and keeps a backup.'
        'PASS: The installed application restarts and opens its main window.'
        'PASS: Presets, listings, organization, and updater preferences survive unchanged.'
    ) | Set-Content -LiteralPath (Join-Path $work 'integration-results.txt')
    Write-Output "Updater integration passed: $work"
} finally {
    # Stop only processes created by this test or launched from its unique target.
    if ($parent -and -not $parent.HasExited) { Stop-Process -Id $parent.Id -ErrorAction SilentlyContinue }
    if ($restarted -and -not $restarted.HasExited) { Stop-Process -Id $restarted.Id -ErrorAction SilentlyContinue }
    if ($helper -and -not $helper.HasExited) { Stop-Process -Id $helper.Id -ErrorAction SilentlyContinue }
}
