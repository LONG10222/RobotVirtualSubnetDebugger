$ErrorActionPreference='Stop'
function Get-NetRoute {param($DestinationPrefix); @()}
$dir=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RobotNet.Windows.Wpf/operations'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$held=[System.IO.File]::Open((Join-Path $dir 'network-operation.lock'),[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
try {
 foreach($script in @('./export-mock.ps1','./manual-export-mock.ps1')) {
  $global:trace=@();$blocked=$false
  try { & $script } catch {$blocked=$true}
  if(-not $blocked -or $global:trace.Count -ne 0) {throw "Concurrent script was not blocked: $script"}
  "PASS concurrent script blocked before mutation: $script"
 }
} finally {$held.Dispose()}
$released=[System.IO.File]::Open((Join-Path $dir 'network-operation.lock'),[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
$released.Dispose()
'PASS lock can be acquired after release'
