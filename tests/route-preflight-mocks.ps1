# Executes only the generated read-only guard against fake route objects.
# Never invokes Windows networking cmdlets.
$ErrorActionPreference = 'Stop'
$guard = [scriptblock]::Create((Get-Content -Raw './route-preflight.ps1'))
function Get-NetRoute { param($AddressFamily,$PolicyStore,$ErrorAction) $script:routes }
function Invoke-Case($name,$items,$mustThrow) {
    $script:routes = @($items)
    $failed = $false
    try { & $guard } catch { $failed = $true }
    if ($failed -ne $mustThrow) { throw "FAIL: $name" }
    Write-Host "PASS: $name"
}
function Route($iface,$hop,$prefix='192.168.1.10/32') {
    [pscustomobject]@{DestinationPrefix=$prefix;InterfaceAlias=$iface;NextHop=$hop}
}
Invoke-Case 'no target route' @() $false
Invoke-Case 'unique intended route' @((Route 'B-LAN' '192.168.31.20')) $false
Invoke-Case 'other-interface VPN route rejected' @((Route 'VPN' '10.8.0.1')) $true
Invoke-Case 'wrong gateway on correct NIC rejected' @((Route 'B-LAN' '192.168.31.99')) $true
Invoke-Case 'parallel intended and conflicting routes rejected' @((Route 'B-LAN' '192.168.31.20'),(Route 'VPN' '10.8.0.1')) $true
Invoke-Case 'duplicate intended routes rejected' @((Route 'B-LAN' '192.168.31.20'),(Route 'B-LAN' '192.168.31.20')) $true
Invoke-Case 'unrelated target route left alone' @((Route 'VPN' '10.8.0.1' '10.10.10.10/32')) $false
function Get-NetRoute { param($AddressFamily,$PolicyStore,$ErrorAction) throw 'route read failed' }
$failed=$false;try{ & $guard }catch{$failed=$true}
if(-not $failed){throw 'Route inspection failure must fail closed'}
Write-Host 'PASS: route read failure fails closed'
