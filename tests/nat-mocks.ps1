$ErrorActionPreference='Stop'
$script:Rows=@()
function Get-NetNat { param($ErrorAction); if($script:FailQuery){throw 'query failed'}; return $script:Rows }
$code=[scriptblock]::Create((Get-Content ./nat-preflight.ps1 -Raw))
function Test-Case($name,$rows,$expectBlocked) {
 $script:Rows=$rows; $blocked=$false
 try { & $code } catch { $blocked=$true }
 if($blocked -ne $expectBlocked){throw "FAIL $name blocked=$blocked"}
 "PASS $name blocked=$blocked"
}
function Nat($name,$prefix,$external=''){[pscustomobject]@{Name=$name;InternalIPInterfaceAddressPrefix=$prefix;ExternalIPInterfaceAddressPrefix=$external}}
Test-Case 'empty permits creation' @() $false
Test-Case 'exact compatible reused' @((Nat 'RobotNetTargetNat' '192.168.31.0/24')) $false
Test-Case 'unrelated HyperV preserved' @((Nat 'HyperV' '172.20.0.0/16')) $true
Test-Case 'same name wrong prefix blocked' @((Nat 'RobotNetTargetNat' '10.0.0.0/24')) $true
Test-Case 'compatible plus unrelated blocked' @((Nat 'RobotNetTargetNat' '192.168.31.0/24'),(Nat 'Container' '172.20.0.0/16')) $true
Test-Case 'external prefix constrained blocked' @((Nat 'RobotNetTargetNat' '192.168.31.0/24' '10.0.0.0/8')) $true
$script:FailQuery=$true
Test-Case 'query failure fails closed' @() $true
'7/7 NAT script mock cases passed; no network cmdlets invoked.'
