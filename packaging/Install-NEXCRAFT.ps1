$ErrorActionPreference = "Stop"
$sourceExe = Join-Path $PSScriptRoot "NEXCRAFT.exe"
if (-not (Test-Path $sourceExe)) {
    [System.Windows.MessageBox]::Show("Не найден NEXCRAFT.exe. Распакуйте весь архив и запустите установщик ещё раз.", "NEXCRAFT — установка", "OK", "Error") | Out-Null
    exit 1
}
$installDir = Join-Path $env:LOCALAPPDATA "Programs\NEXCRAFT"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$installedExe = Join-Path $installDir "NEXCRAFT.exe"
Copy-Item $sourceExe $installedExe -Force
$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "NEXCRAFT.lnk"
$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Description = "NEXCRAFT Minecraft Launcher"
$shortcut.Save()
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NEXCRAFT"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$startShortcut = $ws.CreateShortcut((Join-Path $startMenuDir "NEXCRAFT.lnk"))
$startShortcut.TargetPath = $installedExe
$startShortcut.WorkingDirectory = $installDir
$startShortcut.IconLocation = "$installedExe,0"
$startShortcut.Description = "NEXCRAFT Minecraft Launcher"
$startShortcut.Save()
Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show("NEXCRAFT установлен! Ярлык создан на рабочем столе и в меню Пуск.", "NEXCRAFT", "OK", "Information") | Out-Null
Start-Process $installedExe
