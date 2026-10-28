param([string]$ExecutablePath = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Tudot\Tudot.exe'
}
$desktopPath = [Environment]::GetFolderPath('DesktopDirectory')
if ([string]::IsNullOrWhiteSpace($desktopPath)) {
    throw 'Cannot resolve the current user desktop directory.'
}
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Published executable does not exist. Publish the latest application first: $ExecutablePath"
}
$launcherPath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetExtension($launcherPath) -ne '.exe') {
    throw 'The shortcut must target a published .exe, not a build or launch script.'
}
$applicationDirectory = Split-Path -Parent $launcherPath

$shortcutPath = Join-Path $desktopPath 'Tudot.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $backupDirectory = Join-Path $projectRoot '.agent\shortcut-backups'
    $backupPath = Join-Path $backupDirectory 'Tudot.original.lnk'
    if (-not (Test-Path -LiteralPath $backupPath)) {
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        Copy-Item -LiteralPath $shortcutPath -Destination $backupPath
    }
}

$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $launcherPath
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $applicationDirectory
$shortcut.IconLocation = "$launcherPath,0"
$shortcut.Description = 'Launch the latest published Tudot application.'
$shortcut.WindowStyle = 1
$shortcut.Save()

$verified = $shortcutShell.CreateShortcut($shortcutPath)
if ($verified.TargetPath -ne $launcherPath -or $verified.WorkingDirectory -ne $applicationDirectory -or $verified.Arguments -ne '') {
    throw 'Desktop shortcut verification failed.'
}
Write-Output "Shortcut: $shortcutPath"
Write-Output "Target: $($verified.TargetPath)"
Write-Output "Working directory: $($verified.WorkingDirectory)"
Write-Output "Icon: $($verified.IconLocation)"
