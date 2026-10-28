param([string]$SourceDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $projectRoot 'publish\local-updates'
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $SourceDirectory 'latest.json') | ConvertFrom-Json
$package = Join-Path $SourceDirectory $manifest.packageFile
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TudotUpdateSmoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$install = Join-Path $testRoot 'Tudot'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($package, $install)
function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
function Assert([bool]$Value, [string]$Name) {
    if (-not $Value) { throw "FAIL: $Name" }
    Write-Output "PASS: $Name"
}
$oldInfo = @{ appId = 'Tudot'; buildId = ([long]$manifest.buildId - 1); publishedUtc = [DateTime]::UtcNow.ToString('o') }
Write-Json (Join-Path $install 'build.json') $oldInfo
Write-Json (Join-Path $install 'tudot-install.json') $oldInfo

# Exercise the production offline bundle reader before the real updater starts.
$offlineBundle = Join-Path $SourceDirectory "Tudot-$($manifest.buildId)-update.tudotupdate"
$offlineStaging = Join-Path $testRoot 'offline-staging'
& dotnet run --project (Join-Path $projectRoot 'Tests\UpdateChecks\UpdateChecks.csproj') -c Release -p:NuGetAudit=false -- --stage-offline $offlineBundle $install $offlineStaging
if ($LASTEXITCODE -ne 0) { throw 'Production offline update package validation failed.' }
$package = Join-Path $offlineStaging "$($manifest.buildId)\package.zip"
Assert (Test-Path -LiteralPath $package) 'Offline bundle staged by production update core'

function Run-Updater($CaseManifest, [string]$CasePackage, [string]$CaseName) {
    $attempt = Join-Path $testRoot $CaseName
    New-Item -ItemType Directory -Path $attempt | Out-Null
    $helperPath = Join-Path $attempt 'Tudot.Updater.exe'
    Copy-Item -LiteralPath (Join-Path $install 'Tudot.Updater.exe') -Destination $helperPath
    $planPath = Join-Path $attempt 'plan.json'
    Write-Json $planPath @{
        installDirectory = $install; packagePath = $CasePackage; manifest = $CaseManifest
        parentProcessId = [int]::MaxValue; parentStartTimeUtcTicks = 1
        ackPath = (Join-Path $attempt 'ready.json')
    }
    $env:TUDOT_STARTUP_REPORT = Join-Path $attempt 'startup.json'
    $helper = Start-Process -FilePath $helperPath -ArgumentList ('--plan "' + $planPath + '"') -WorkingDirectory $attempt -WindowStyle Hidden -PassThru
    if (-not $helper.WaitForExit(45000)) {
        $helper.Kill()
        throw 'Test updater timed out.'
    }
    $exitCode = $helper.ExitCode
    # The diagnostic app closes itself three seconds after rendering.
    for ($poll = 0; $poll -lt 40; $poll++) {
        if (Test-Path -LiteralPath (Join-Path $attempt 'startup.json')) {
            $report = Get-Content -Raw -LiteralPath (Join-Path $attempt 'startup.json')
            if ($report -match 'Window.Settled') { break }
        }
        Start-Sleep -Milliseconds 250
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Attempt = $attempt }
}
$previousReport = $env:TUDOT_STARTUP_REPORT
$previousSilent = $env:TUDOT_UPDATER_SILENT
try {
    $env:TUDOT_UPDATER_SILENT = '1'
    $success = Run-Updater $manifest $package 'success'
    Assert ($success.ExitCode -eq 0) 'External updater exited successfully'
    $installed = Get-Content -Raw -LiteralPath (Join-Path $install 'build.json') | ConvertFrom-Json
    Assert ([long]$installed.buildId -eq [long]$manifest.buildId) 'New build activated in the isolated installation'
    $ack = Get-Content -Raw -LiteralPath (Join-Path $success.Attempt 'ready.json') | ConvertFrom-Json
    Assert ([long]$ack.buildId -eq [long]$manifest.buildId) 'Real WPF app acknowledged a rendered main window'
    Assert ((Get-Content -Raw -LiteralPath (Join-Path $success.Attempt 'update.log')) -match 'SUCCESS') 'Updater recorded confirmation'

    $badPayload = Join-Path $testRoot 'bad-payload'
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $badPayload)
    # A valid executable that exits without startup acknowledgement avoids Windows
    # compatibility dialogs for corrupt PE files blocking unattended CI tests.
    Copy-Item -LiteralPath (Join-Path $badPayload 'Tudot.Updater.exe') -Destination (Join-Path $badPayload 'Tudot.exe') -Force
    $badInfo = @{ appId = 'Tudot'; buildId = ([long]$manifest.buildId + 1); publishedUtc = [DateTime]::UtcNow.ToString('o') }
    Write-Json (Join-Path $badPayload 'build.json') $badInfo
    Write-Json (Join-Path $badPayload 'tudot-install.json') $badInfo
    $badZip = Join-Path $testRoot 'bad-build.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($badPayload, $badZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $badManifest = @{
        appId = 'Tudot'; buildId = $badInfo.buildId; publishedUtc = $badInfo.publishedUtc
        packageFile = 'bad-build.zip'; sha256 = (Get-FileHash -LiteralPath $badZip -Algorithm SHA256).Hash
        packageSize = (Get-Item -LiteralPath $badZip).Length; notes = 'failure injection'
    }
    $failure = Run-Updater $badManifest $badZip 'rollback'
    Assert ($failure.ExitCode -eq 1) 'A new build without startup acknowledgement fails installation confirmation'
    $restored = Get-Content -Raw -LiteralPath (Join-Path $install 'build.json') | ConvertFrom-Json
    Assert ([long]$restored.buildId -eq [long]$manifest.buildId) 'Previously working executable and metadata restored'
    Assert ((Get-Content -Raw -LiteralPath (Join-Path $failure.Attempt 'update.log')) -match 'ROLLBACK') 'Rollback restarted the previous WPF app'
    Assert ((Get-Content -Raw -LiteralPath (Join-Path $failure.Attempt 'startup.json')) -match 'Window.Settled') 'Restored app rendered and closed normally in diagnostic mode'
    Write-Output "Smoke test files retained: $testRoot"
}
finally {
    $env:TUDOT_STARTUP_REPORT = $previousReport
    $env:TUDOT_UPDATER_SILENT = $previousSilent
}
