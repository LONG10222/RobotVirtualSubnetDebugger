$ErrorActionPreference='Stop'
function Get-NetRoute {param($DestinationPrefix); @()}
$global:trace=@();$failed=$false
try { & ./export-mock.ps1 } catch { $failed=$true }
if(-not $failed -or ($global:trace -join ',') -ne 'forward,natUndo,restore'){throw 'export rollback order failed'}
'PASS exported script restores attempted mutations in reverse after NAT failure'
$global:trace=@();$failed=$false
try { & ./manual-export-mock.ps1 } catch { $failed=$true }
if(-not $failed -or ($global:trace -join ',') -ne 'restore'){throw 'manual export must continue after uncertain NAT'}
'PASS manual exported rollback continues after uncertain item and reports incomplete'
