$d = 'D:\SteamLibrary\steamapps\common\Valheim\BepInEx\plugins\DrawersSaveGuard'
if (Get-Process valheim -ErrorAction SilentlyContinue) { Write-Host 'Valheim is running - close the game'; exit 1 }
Copy-Item "$d\DrawersSaveGuard.dll.new" "$d\DrawersSaveGuard.dll" -Force
Remove-Item "$d\DrawersSaveGuard.dll.new"
Write-Host 'Done'

