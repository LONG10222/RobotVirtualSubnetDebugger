using System.Net;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Logging;
class Program {
static NetworkAdapterInfo Ad(string name,string ip,bool lan=false,bool target=false)=>new(){Name=name,IPv4Address=ip,SubnetMask="255.255.255.0",Status="Up",Gateway=lan?ip[..ip.LastIndexOf('.')]+".1":"",IsLanCandidate=lan,IsTargetNetworkCandidate=target};
static AppConfig C(DeviceRole role)=>new(){Role=role,GatewayLanIp="192.168.31.20",TargetDeviceIp="192.168.1.10",TargetDeviceAdapterIp="192.168.1.100",VirtualIp="192.168.1.101",VirtualSubnetMask="255.255.255.0",TargetDevicePort=30003};
static VirtualSubnetPlan Plan(AppConfig c, params NetworkAdapterInfo[] ads)=>new WindowsVirtualSubnetService(new Adapters(ads),new Priv(),new WindowsRouteApplyService(),new WindowsNatApplyService(),new WindowsAdapterIpConfigurationService(),new Log()).BuildPlan(c);
static void Check(bool ok,string m){Console.WriteLine((ok?"CONFIRMED ":"FAIL ")+m);if(!ok)throw new Exception(m);}
static void TransactionTests(){
var steps=new[]{new VirtualSubnetOperationStep{Command="preflight",RollbackCommand="无"},new VirtualSubnetOperationStep{Command="forward1",RollbackCommand="undo1"},new VirtualSubnetOperationStep{Command="forward2",RollbackCommand="undo2"},new VirtualSubnetOperationStep{Command="nat",RollbackCommand="undoNat"},new VirtualSubnetOperationStep{Command="unattempted",RollbackCommand="undoNever"}};
var calls=new List<string>();bool? outcome=null;
try{NetworkApplyTransaction.ExecuteAsync(steps,c=>{calls.Add(c);if(c=="nat")throw new Exception("NAT race");return Task.CompletedTask;},ok=>outcome=ok).GetAwaiter().GetResult();}catch(Exception){}
Check(outcome==true && string.Join(",",calls)=="preflight,forward1,forward2,nat,undoNat,undo2,undo1","NAT failure rolls back attempted steps in reverse, never unattempted");
calls.Clear();outcome=null;
try{NetworkApplyTransaction.ExecuteAsync(steps,c=>{calls.Add(c);if(c=="nat"||c=="undoNat")throw new Exception("uncertain receipt");return Task.CompletedTask;},ok=>outcome=ok).GetAwaiter().GetResult();}catch(Exception){}
Check(outcome==false && calls.Contains("undo2") && calls.Contains("undo1"),"uncertain NAT undo does not prevent forwarding restoration and reports incomplete");
calls.Clear();outcome=null;
try{NetworkApplyTransaction.ExecuteAsync(steps,c=>{calls.Add(c);throw new Exception("preflight reject");},ok=>outcome=ok).GetAwaiter().GetResult();}catch(Exception){}
Check(calls.SequenceEqual(new[]{"preflight"}) && outcome==true,"preflight failure never executes mutations or unrelated undos");
}
static void PersistenceTests(){
var dir=Path.Combine(Path.GetTempPath(),"robot-record-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
try{
var store=new FileOperationRollbackService(new Log(),dir);var path=Path.Combine(dir,"last-network-operation.json");
Check(store.LoadLastOperation()==null,"missing record is genuinely absent");
foreach(var json in new[]{"{broken","null","{}"}){File.WriteAllText(path,json);bool rejected=false;try{store.LoadLastOperation();}catch(InvalidOperationException){rejected=true;}Check(rejected,"corrupt/incomplete record fails closed: "+json);}
var executor=new WindowsNetworkConfigurationExecutor(null!,new Admin(),store,null!,new Log());
var method=typeof(WindowsNetworkConfigurationExecutor).GetMethod("ApplyPlanAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
var plan=new VirtualSubnetPlan{Status=DiagnosticStatus.Success,CanGenerateScripts=true};
var result=((Task<NetworkConfigurationExecutionResult>)method.Invoke(executor,new object[]{plan,C(DeviceRole.DebugClient),"mock"})!).GetAwaiter().GetResult();
Check(!result.Success && File.ReadAllText(path)=="{}","corrupt prior record blocks new application without overwriting evidence");
var record=new NetworkOperationRecord{RollbackSchemaVersion=1,Status=NetworkConfigurationApplyStatus.Applying};store.SaveOperation(record);
Check(store.LoadLastOperation()!.OperationId==record.OperationId,"atomic first save readable");
record.Status=NetworkConfigurationApplyStatus.Failed;store.SaveOperation(record);
Check(store.LoadLastOperation()!.Status==NetworkConfigurationApplyStatus.Failed && !Directory.GetFiles(dir,"*.tmp").Any(),"atomic replacement readable and temp cleaned");
var calls=new List<string>();bool incomplete=false;
try{NetworkApplyTransaction.RollbackAsync(new[]{"uncertainNat","restoreTarget","restoreLan"},c=>{calls.Add(c);if(c=="uncertainNat")throw new Exception("pending");return Task.CompletedTask;}).GetAwaiter().GetResult();}catch(AggregateException){incomplete=true;}
Check(incomplete && calls.SequenceEqual(new[]{"uncertainNat","restoreTarget","restoreLan"}),"manual rollback continues remaining steps and reports incomplete");
}finally{Directory.Delete(dir,true);}
}
static void LockTests(){
var dir=Path.Combine(Path.GetTempPath(),"robot-lock-"+Guid.NewGuid().ToString("N"));
try {
using(var first=NetworkOperationLock.Acquire(dir)) {
bool blocked=false;try {using var second=NetworkOperationLock.Acquire(dir);}catch(IOException){blocked=true;}
Check(blocked,"concurrent operation lock blocked before any network action");
var pi=new System.Diagnostics.ProcessStartInfo{FileName=Environment.ProcessPath!,UseShellExecute=false};
if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")pi.ArgumentList.Add(typeof(Program).Assembly.Location);
pi.ArgumentList.Add("--lock-probe");pi.ArgumentList.Add(dir);
using var child=System.Diagnostics.Process.Start(pi)!;child.WaitForExit();
Check(child.ExitCode==23,"separate real process cannot acquire held operation lock");
}
using(var next=NetworkOperationLock.Acquire(dir)) {Check(next.CanWrite,"operation lock released for subsequent action");}
Check(File.Exists(Path.Combine(dir,"network-operation.lock")),"lock file retained to prevent rename/delete race");
}finally {Directory.Delete(dir,true);}
}
static void StudentUxTests(){
var cfg=C(DeviceRole.DebugClient);cfg.GatewayLanIp="";cfg.EnableTcpProxyMode=false;cfg.SharedKey="a-trusted-shared-key";
var service=new MockConfig(cfg);var executor=new UxExecutor();
var vm=new RobotNet.Windows.Wpf.ViewModels.EasyModeViewModel(service,new Adapters(new[]{Ad("B LAN","10.20.0.2",true)}),new UxDiscovery(),executor,new UxPreflight(),new UxConfirm(),new Log());
Check(vm.GatewayLanIp=="","B never mistakes its own LAN IP for A's IP");
vm.SharedKey="pasted-from-A";
Check(vm.SharedKey=="pasted-from-A","B view model accepts pasted pairing key");
vm.GatewayLanIp="10.20.0.1";vm.PreviewCommand.Execute(null);
Check(!executor.Last!.EnableTcpProxyMode,"connection wizard preserves original-IP mode without enabling single-port proxy");
Check(service.Saves==0 && executor.Applies==0,"preview never saves configuration or applies network operations");
vm.PreviewCommand.Execute(null);
Check(executor.Applies==0,"repeated preview does not apply network operations");
cfg.TargetDeviceIp="192.168.57.2";cfg.TargetDeviceAdapterIp="";
vm.ReloadSavedConfiguration();
Check(vm.TargetDeviceAdapterIp=="","clearing saved A adapter IP clears wizard instead of retaining stale IP");
Check(vm.TargetDeviceIp=="192.168.57.2" && vm.PreviewSteps.Count==0,"saved settings refresh wizard parameters and clear stale preview");
vm.ApplyClientCommand.Execute(null);
Check(executor.Applies==0 && vm.Status.Contains("已取消"),"cancel confirmation does not apply network changes");
}
static void FairinoTests(){
var cfg=C(DeviceRole.DebugClient);cfg.GatewayLanIp="10.20.0.1";cfg.TargetDeviceAdapterIp="192.168.57.100";cfg.SharedKey="unchanged-test-key";
var service=new MockConfig(cfg);var vm=new RobotNet.Windows.Wpf.ViewModels.SettingsViewModel(service,new Log());
vm.PreviewFairinoProfileCommand.Execute(null);
Check(service.Saves==0 && cfg.TargetDeviceIp=="192.168.1.10","Fairino preset changes preview only, never saves or touches network");
Check(vm.TargetDeviceIp=="192.168.57.2" && vm.TargetDevicePort==20003 && !vm.EnableTcpProxyMode,"Fairino preview preserves documented target and selects route/NAT not single-port proxy");
Check(vm.GatewayLanIp=="10.20.0.1" && vm.TargetDeviceAdapterIp=="192.168.57.100" && vm.SharedKey=="unchanged-test-key","Fairino preset never invents or overwrites LAN credentials/address");
Check(vm.SaveMessage.Contains("20003/20004/8080") && vm.SaveMessage.Contains("不可也设"),"Fairino preview warns of multiport and duplicate-IP hazard");
var b=C(DeviceRole.DebugClient);b.TargetDeviceIp="192.168.57.2";b.EnableTcpProxyMode=false;
var bp=Plan(b,Ad("B-LAN","192.168.31.30",true));
Check(bp.CanGenerateScripts && bp.TargetSubnetCidr=="192.168.57.2/32","documented Fairino target produces unchanged /32 route");
var a=C(DeviceRole.GatewayAgent);a.TargetDeviceIp="192.168.57.2";a.TargetDeviceAdapterIp="192.168.57.100";a.EnableTcpProxyMode=false;
Check(Plan(a,Ad("A-LAN","192.168.31.20",true),Ad("Robot","192.168.57.100")).CanGenerateScripts,"Fairino dual-NIC route/NAT plan accepted with distinct local IP");
a.TargetDeviceAdapterIp="192.168.57.2";
Check(!Plan(a,Ad("A-LAN","192.168.31.20",true),Ad("Robot","192.168.57.2")).CanGenerateScripts,"unsafe tutorial duplicate robot/local IP rejected");
}
static void CanonicalAddressTests(){
foreach(var alias in new[]{"3232241922", "0xC0A83902", "192.168.057.2", "192.168.57.002", " 192.168.57.2"}) {
var c=C(DeviceRole.GatewayAgent);c.TargetDeviceIp=alias;c.TargetDeviceAdapterIp="192.168.57.2";
Check(!Plan(c,Ad("LAN","192.168.31.20",true),Ad("Robot","192.168.57.2")).CanGenerateScripts,"noncanonical robot address rejected: "+alias);
}
var shortAddress=C(DeviceRole.DebugClient);shortAddress.TargetDeviceIp="10.2";
Check(!Plan(shortAddress,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"short IPv4 rejected instead of silently targeting 10.0.0.2");
var gateway=C(DeviceRole.DebugClient);gateway.GatewayLanIp="192.168.31.020";
Check(!Plan(gateway,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"noncanonical gateway address rejected");
var mask=C(DeviceRole.DebugClient);mask.VirtualSubnetMask="0xffffff00";
Check(!Plan(mask,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"hex subnet mask rejected");
}
static void OriginalIpPreflightTests(){
foreach(var role in new[]{DeviceRole.DebugClient,DeviceRole.GatewayAgent}) {
var cfg=C(role);cfg.EnableTcpProxyMode=false;var ports=new BusyProxyPorts();
var preflight=new RobotNet.Windows.Wpf.Services.Diagnostics.ConnectionPreflightService(ports,new Log());
var before=(cfg.LocalListenPort,cfg.ProxyControlPort);var result=preflight.EnsurePortsReady(cfg);
Check(result.CanContinue && !result.ConfigChanged && before==(cfg.LocalListenPort,cfg.ProxyControlPort) && ports.Calls.All(p=>p==PortProtocol.Udp),"original-IP preflight ignores unused occupied proxy listeners: "+role);
cfg.EnableTcpProxyMode=true;result=preflight.EnsurePortsReady(cfg);
Check(!result.CanContinue && ports.Calls.Contains(PortProtocol.Tcp),"enabled proxy still rejects exhausted TCP listeners: "+role);
}
}
static void Main(string[] args){
if(args.Length==2 && args[0]=="--lock-probe") {try {using var probe=NetworkOperationLock.Acquire(args[1]);Environment.Exit(0);}catch(IOException){Environment.Exit(23);}return;}

CanonicalAddressTests();
OriginalIpPreflightTests();
var b=C(DeviceRole.DebugClient);var pb=Plan(b,Ad("B-LAN","192.168.31.30",true));
Check(pb.TargetSubnetCidr=="192.168.1.10/32" && pb.Steps.Any(s=>s.Command.Contains("New-NetRoute")&&s.Command.Contains("192.168.31.20")),"B generates only original robot /32 host route via A; no destination rewrite");
var routeStep=pb.Steps.Single(s=>s.Command.Contains("New-NetRoute"));
File.WriteAllText("route-preflight.ps1",WindowsRouteApplyService.CreatePreflightCommand(pb.TargetSubnetCidr,"B-LAN",b.GatewayLanIp));
Check(WindowsVirtualSubnetService.CreateApplyScript(pb).Contains(WindowsVirtualSubnetService.RouteNatSafetyNotice),"exported apply script carries data-plane trust warning before confirmation");
Check(routeStep.Command.StartsWith("$robotHostRoutes = @(Get-NetRoute") && routeStep.Command.Contains("$robotHostRoutes.Count -eq 1") && routeStep.Command.Contains("Conflicting robot host route"),"host-route preflight inspects every interface and rejects ambiguous routes before mutation");
Check(routeStep.Command.Split("$robotHostRoutes = @(Get-NetRoute").Length==3,"host-route conflict check repeated immediately before creation");
Check(!pb.Steps.Any(s=>s.Command.Contains("New-NetIPAddress")),"B never assigns VirtualIp 192.168.1.101; no actual same-subnet local IP");
var a=C(DeviceRole.GatewayAgent);var pa=Plan(a,Ad("A-LAN","192.168.31.20",true),Ad("A-ROBOT","192.168.1.100",false,true));
Check(pa.Warnings.Contains(WindowsVirtualSubnetService.RouteNatSafetyNotice),"route/NAT plan explicitly exposes unauthenticated whole-LAN forwarding risk");
Check(pa.LanSubnetCidr=="192.168.31.0/24" && pa.Steps.Count(s=>s.Command.Contains("Set-NetIPInterface"))==2 && pa.Steps.Any(s=>s.Command.Contains("New-NetNat")),"A enables both forwarding interfaces and LAN-source NAT");
Check(pa.Steps.All(s=>!s.Command.Contains("NetFirewall")&&!s.Command.Contains("30003")&&!s.Command.Contains("SharedKey")),"Route/NAT plan has no robot-port firewall or pairing-key gate");
var wrong=Plan(b,Ad("Wrong-Internet","10.8.0.2",true),Ad("Correct-LAN","192.168.31.30",true));
Check(wrong.LanInterfaceName=="Correct-LAN" && wrong.CanGenerateScripts,"FIXED: gateway subnet selects correct NIC regardless of enumeration order");
Check(!Plan(b,Ad("Wrong","10.8.0.2",true)).CanGenerateScripts,"unreachable gateway blocked");
Check(!Plan(b,Ad("One","192.168.31.30",true),Ad("Two","192.168.31.31",true)).CanGenerateScripts,"ambiguous gateway NIC blocked");
Check(!Plan(a,Ad("Wrong","192.168.31.30",true),Ad("Robot","192.168.1.100",false,true)).CanGenerateScripts,"A must own configured gateway IP");
var overlap=C(DeviceRole.DebugClient); overlap.GatewayLanIp="192.168.1.20";
var po=Plan(overlap,Ad("Overlapping-LAN","192.168.1.30",true));
Check(po.Status==DiagnosticStatus.Error && !po.CanGenerateScripts,"FIXED: overlapping robot/LAN subnet blocks application");
var broad=Ad("Broad","192.168.50.2",true);broad.SubnetMask="255.255.0.0";
Check(!Plan(b,broad).CanGenerateScripts,"broader NIC overlap blocked");
var narrow=Ad("Narrow","192.168.1.200",true);narrow.SubnetMask="255.255.255.128";
Check(!Plan(b,Ad("LAN","192.168.31.30",true),narrow).CanGenerateScripts,"contained subnet overlap blocked even when robot outside NIC range");
Check(!Plan(overlap,Ad("Mislabelled","192.168.1.30",true,true)).CanGenerateScripts,"target candidate flag cannot bypass overlap guard");
var noKey=C(DeviceRole.GatewayAgent);noKey.SharedKey="";var pk=Plan(noKey,Ad("A-LAN","192.168.31.20",true),Ad("A-ROBOT","192.168.1.100",false,true));
Check(pk.CanGenerateScripts,"No shared key required for direct Route/NAT plan (not authenticated proxy)");
Check(pa.Steps[0].Order==0 && !pa.Steps[0].WillModifySystem && pa.Steps[0].Command.Contains("@(Get-NetNat -ErrorAction Stop)"),"all NAT preflight precedes all mutations");
var nat=pa.Steps.Single(s=>s.Command.Contains("New-NetNat"));
Check(nat.Command.Contains("Existing NAT is incompatible or unrelated") && nat.Command.IndexOf("Existing NAT is incompatible or unrelated")<nat.Command.IndexOf("New-NetNat"),"repeat unrelated NAT check immediately before creation");
Check(pa.Steps[0].Command.Contains("ExternalIPInterfaceAddressPrefix") && pa.Steps[0].Command.Contains("$allRobotNetNats.Count -eq 1"),"external-prefix and multiple NAT configurations rejected");
Check(WindowsVirtualSubnetService.GetApplySteps(pa)[0].Order==0,"in-app execution retains read-only preflight before mutations");
var mismatch=Ad("Robot","192.168.1.100",false,true);mismatch.SubnetMask="255.255.255.128";
Check(!Plan(a,Ad("LAN","192.168.31.20",true),mismatch).CanGenerateScripts,"target actual mask mismatch blocks plan");
var broadcast=C(DeviceRole.DebugClient);broadcast.GatewayLanIp="192.168.31.255";
Check(!Plan(broadcast,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"broadcast next hop blocked");
broadcast.GatewayLanIp="192.168.31.0";
Check(!Plan(broadcast,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"network next hop blocked");
broadcast.GatewayLanIp="224.0.0.1";
Check(!Plan(broadcast,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"multicast next hop blocked");
var vmB=Ad("vEthernet-B","192.168.31.30");vmB.IsVirtual=true;
Check(Plan(b,vmB).CanGenerateScripts,"isolated virtual B NIC needs no default gateway or LAN candidate flag");
var vmA=Ad("vEthernet-A","192.168.31.20");vmA.IsVirtual=true;
var vmRobot=Ad("vEthernet-Robot","192.168.1.100");vmRobot.IsVirtual=true;
Check(Plan(a,vmA,vmRobot).CanGenerateScripts,"isolated virtual A NICs selected only by configured exact IPs");
var badTarget=C(DeviceRole.DebugClient);badTarget.TargetDeviceIp="192.168.1.255";
Check(!Plan(badTarget,Ad("LAN","192.168.31.30",true)).CanGenerateScripts,"broadcast robot target blocked for B too");
TransactionTests();
PersistenceTests();
FairinoTests();
StudentUxTests();
LockTests();
var mockPlan=new VirtualSubnetPlan{TargetSubnetCidr="192.168.1.10/32"};
mockPlan.Steps.Add(new(){Order=1,Command="$global:trace += 'forward'",RollbackCommand="$global:trace += 'restore'"});
mockPlan.Steps.Add(new(){Order=2,Command="throw 'NAT race'",RollbackCommand="$global:trace += 'natUndo'"});
File.WriteAllText("export-mock.ps1",WindowsVirtualSubnetService.CreateApplyScript(mockPlan).Replace("#Requires -RunAsAdministrator", "# isolated mock").Replace("$ConfirmRobotNetStage5 = $false","$ConfirmRobotNetStage5 = $true"));
mockPlan.Steps[1].RollbackCommand="throw 'uncertain NAT'";
File.WriteAllText("manual-export-mock.ps1",WindowsVirtualSubnetService.CreateRollbackScript(mockPlan).Replace("#Requires -RunAsAdministrator", "# isolated mock").Replace("$ConfirmRobotNetStage5Rollback = $false","$ConfirmRobotNetStage5Rollback = $true"));
File.WriteAllText("nat-preflight.ps1",pa.Steps[0].Command);
File.WriteAllText("plans.json",JsonSerializer.Serialize(new {NormalB=pb,NormalA=pa,WrongAdapter=wrong,Overlap=po},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("All assertions passed. No OS network commands executed.");
}}
class Adapters(NetworkAdapterInfo[] ads):INetworkAdapterService{public IReadOnlyList<NetworkAdapterInfo> GetAdapters()=>ads;public IReadOnlyList<NetworkAdapterInfo> FindTargetNetworkCandidates(string s)=>ads;public IReadOnlyList<NetworkAdapterInfo> FindLanCandidates()=>ads;public bool IsSameSubnet(string a,string m,string b){var x=IPAddress.Parse(a).GetAddressBytes();var y=IPAddress.Parse(b).GetAddressBytes();var z=IPAddress.Parse(m).GetAddressBytes();return Enumerable.Range(0,4).All(i=>(x[i]&z[i])==(y[i]&z[i]));}}
class Priv:IPrivilegeService{public bool IsRunningAsAdministrator()=>true;public PlatformOperationResult CheckAdministrator()=>new(){Status=DiagnosticStatus.Success};}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string m){}public void Warning(string m){}public void Error(string m,Exception? e=null){}public void Audit(string m){}public IReadOnlyList<string> GetLogs()=>[];}

class Admin:IAdminElevationService{public bool IsReadOnlyMode=>false;public PlatformOperationResult CheckAdminPrivilege()=>new(){Status=DiagnosticStatus.Success};public AdminElevationResult EnsureRunningAsAdmin(string[] args)=>new();}

class MockConfig(AppConfig cfg):RobotNet.Windows.Wpf.Services.Configuration.IConfigurationService{
public int Saves;public AppConfig Load()=>cfg;public void Save(AppConfig c){Saves++;}public AppConfig CreateDefault()=>new();}

class UxExecutor:INetworkConfigurationExecutor {
public event EventHandler<string>? ExecutionLogAdded;
public AppConfig? Last; public int Applies;
public VirtualSubnetPlan PreviewGatewayConfiguration(AppConfig c){Last=c;return new(){Status=DiagnosticStatus.Success};}
public VirtualSubnetPlan PreviewClientConfiguration(AppConfig c)=>PreviewGatewayConfiguration(c);
public Task<NetworkConfigurationExecutionResult> ApplyGatewayConfigurationAsync(AppConfig c){Applies++;return Task.FromResult(new NetworkConfigurationExecutionResult());}
public Task<NetworkConfigurationExecutionResult> ApplyClientConfigurationAsync(AppConfig c)=>ApplyGatewayConfigurationAsync(c);
public Task<NetworkConfigurationExecutionResult> RollbackLastConfigurationAsync()=>Task.FromResult(new NetworkConfigurationExecutionResult());
public NetworkOperationRecord? GetLastOperation()=>null;
}
class UxDiscovery:RobotNet.Windows.Wpf.Services.Discovery.IDiscoveryService{public event EventHandler<DeviceInfo>? DeviceDiscovered;public Task StartAsync()=>Task.CompletedTask;public Task StopAsync()=>Task.CompletedTask;public IReadOnlyList<DeviceInfo> GetOnlineDevices()=>[];}
class UxPreflight:RobotNet.Windows.Wpf.Services.Diagnostics.IConnectionPreflightService{public ConnectionPreflightResult EnsurePortsReady(AppConfig c)=>new(){CanContinue=true};}
class UxConfirm:RobotNet.Windows.Wpf.Services.Ui.IUserConfirmationService{public bool Confirm(string title,string message)=>false;}

class BusyProxyPorts:IPortAvailabilityService {
public List<PortProtocol> Calls=new();
public PortCheckResult CheckPort(int port,PortProtocol protocol){Calls.Add(protocol);return new(){Port=port,Protocol=protocol,IsAvailable=protocol==PortProtocol.Udp};}
public PortCheckResult FindAvailablePort(int port,PortProtocol protocol,int maxAttempts=100)=>new(){Port=port,Protocol=protocol,IsAvailable=false};
}
