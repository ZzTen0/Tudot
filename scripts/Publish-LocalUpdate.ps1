param(
    [switch]$Install,
    [string]$Notes = 'Local build',
    [string]$SourceDirectory = '',
    [string]$Version = '',
    [switch]$SkipShortcut
)

$ErrorActionPreference = 'Stop'
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch format.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$taggedBuild = -not [string]::IsNullOrWhiteSpace($Version)
[xml]$versionProperties = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$sourceVersion = [string]$versionProperties.Project.PropertyGroup.Version
if ($Version -and $Version -ne $sourceVersion) { throw 'Release version must match Directory.Build.props.' }
$Version = $sourceVersion
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $projectRoot 'publish\local-updates'
}
$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Tudot'
if ($Install -and (Test-Path -LiteralPath $installDirectory)) {
    throw "An installation already exists at $installDirectory. Publish without -Install and restart Tudot to update."
}

function Write-AtomicJson([string]$Path, $Value) {
    $temporary = $Path + '.tmp-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, $Path + '.previous') }
    else { [IO.File]::Move($temporary, $Path) }
}

New-Item -ItemType Directory -Path $SourceDirectory -Force | Out-Null
$buildId = [DateTime]::UtcNow.Ticks
$latestPath = Join-Path $SourceDirectory 'latest.json'
if (Test-Path -LiteralPath $latestPath) {
    $previous = Get-Content -Raw -LiteralPath $latestPath | ConvertFrom-Json
    if ($buildId -le [long]$previous.buildId) { $buildId = [long]$previous.buildId + 1 }
}
$buildDirectory = Join-Path $projectRoot "publish\local-staging\$buildId"
$appDirectory = Join-Path $buildDirectory 'app'
$updaterDirectory = Join-Path $buildDirectory 'updater'
$publishArguments = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=false', '-p:NuGetAudit=false')
if ($Version) { $publishArguments += "-p:Version=$Version" }
& dotnet publish (Join-Path $projectRoot 'Tudot\Tudot.csproj') @publishArguments -o $appDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publishing Tudot failed.' }
& dotnet publish (Join-Path $projectRoot 'Tudot.Updater\Tudot.Updater.csproj') @publishArguments -o $updaterDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publishing the updater failed.' }
Copy-Item -LiteralPath (Join-Path $updaterDirectory 'Tudot.Updater.exe') -Destination (Join-Path $appDirectory 'Tudot.Updater.exe')

$publishedUtc = [DateTime]::UtcNow.ToString('o')
$buildInfo = @{ appId = 'Tudot'; buildId = $buildId; publishedUtc = $publishedUtc }
if ($Version) { $buildInfo.version = $Version }
Write-AtomicJson (Join-Path $appDirectory 'build.json') $buildInfo
Write-AtomicJson (Join-Path $appDirectory 'tudot-install.json') $buildInfo
if (-not $taggedBuild) { Write-AtomicJson (Join-Path $appDirectory 'update-source.json') @{ sourceDirectory = $SourceDirectory } }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageFile = "Tudot-$buildId-win-x64.zip"
$packagePath = Join-Path $SourceDirectory $packageFile
$packageTemporary = $packagePath + '.tmp-' + [Guid]::NewGuid().ToString('N')
[IO.Compression.ZipFile]::CreateFromDirectory($appDirectory, $packageTemporary, [IO.Compression.CompressionLevel]::Optimal, $false)
[IO.File]::Move($packageTemporary, $packagePath)
$manifest = @{
    appId = 'Tudot'; buildId = $buildId; publishedUtc = $publishedUtc
    packageFile = $packageFile; sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    packageSize = (Get-Item -LiteralPath $packagePath).Length; notes = $Notes
}
# Offline bundle embeds the exact same manifest and full payload as the feed.
# No extraction into the installation directory occurs before updater validation.
$bundlePath = Join-Path $SourceDirectory "Tudot-$buildId-update.tudotupdate"
$bundleTemporary = $bundlePath + '.tmp-' + [Guid]::NewGuid().ToString('N')
$bundle = [IO.Compression.ZipFile]::Open($bundleTemporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle, $packagePath, 'payload.zip', [IO.Compression.CompressionLevel]::NoCompression)
    $metadata = $bundle.CreateEntry('manifest.json')
    $writer = New-Object IO.StreamWriter($metadata.Open(), (New-Object Text.UTF8Encoding($false)))
    try { $writer.Write(($manifest | ConvertTo-Json -Depth 8)) } finally { $writer.Dispose() }
} finally { $bundle.Dispose() }
[IO.File]::Move($bundleTemporary, $bundlePath)
# Publish metadata only after both complete artifacts are ready.
Write-AtomicJson $latestPath $manifest

if ($Install) {
    $installParent = Split-Path -Parent $installDirectory
    New-Item -ItemType Directory -Path $installParent -Force | Out-Null
    $candidate = $installDirectory + '.bootstrap-' + [Guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path $candidate | Out-Null
    Get-ChildItem -LiteralPath $appDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $candidate -Recurse
    }
    [IO.Directory]::Move($candidate, $installDirectory)
}
if (-not $SkipShortcut -and (Test-Path -LiteralPath (Join-Path $installDirectory 'Tudot.exe'))) {
    & (Join-Path $PSScriptRoot 'Sync-DesktopShortcut.ps1') -ExecutablePath (Join-Path $installDirectory 'Tudot.exe')
}
Write-Output "Build: $buildId"
Write-Output "Local update source: $SourceDirectory"
Write-Output "Package: $packagePath"
Write-Output "Offline update package: $bundlePath"
Write-Output "Installed application: $installDirectory"
