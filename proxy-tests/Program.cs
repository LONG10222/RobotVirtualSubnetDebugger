using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
class Program {
static int Free(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
static AppConfig C(DeviceRole r,int gp,int lp,int tp)=>new(){Role=r,ProxyControlPort=gp,LocalListenPort=lp,TargetDeviceIp="127.0.0.1",TargetDevicePort=tp,GatewayLanIp="127.0.0.1",SharedKey="test-only-not-a-credential",ProxyHeartbeatIntervalSeconds=1,ProxyIdleTimeoutSeconds=3,ProxyReconnectAttempts=0};
static SafeTcpProxyService S()=>new(new Ports(),new Log());
static void Assert(bool v,string text){if(!v)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);}
static async Task Closed(TcpClient c){using var ct=new CancellationTokenSource(6000);byte[] b=new byte[1024];try{Assert(await c.GetStream().ReadAsync(b,ct.Token)==0,"peer closed within deadline");}catch(IOException){Console.WriteLine("PASS: peer reset within deadline");}}
static async Task TimeoutTests(){
var g=S();int gp=Free(),lp=Free();await g.StartAsync(C(DeviceRole.GatewayAgent,gp,lp,Free()));
try{foreach(bool partial in new[]{false,true}){using var c=new TcpClient();await c.ConnectAsync(IPAddress.Loopback,gp);var sw=Stopwatch.StartNew();if(partial)await c.GetStream().WriteAsync("{\"protocolVersion\":"u8.ToArray());await Closed(c);Assert(sw.ElapsedMilliseconds<5500,$"{(partial?"partial":"silent")} auth bounded");await Task.Delay(100);Assert(g.Current.ActiveConnections==0,"unauthenticated slot released");}}finally{await g.StopAsync();}
var fake=new TcpListener(IPAddress.Loopback,0);fake.Start();gp=((IPEndPoint)fake.LocalEndpoint).Port;var b=S();lp=Free();await b.StartAsync(C(DeviceRole.DebugClient,gp,lp,Free()));try{using var local=new TcpClient();await local.ConnectAsync(IPAddress.Loopback,lp);using var accepted=await fake.AcceptTcpClientAsync();await Closed(local);await Task.Delay(100);Assert(b.Current.ActiveConnections==0,"silent gateway reply deadline releases local slot");}finally{await b.StopAsync();fake.Stop();}
}
static async Task HalfClose(bool targetFirst,int size){
var target=new TcpListener(IPAddress.Loopback,0);target.Start();int tp=((IPEndPoint)target.LocalEndpoint).Port,gp=Free(),lp=Free();var g=S();var b=S();await g.StartAsync(C(DeviceRole.GatewayAgent,gp,lp,tp));await b.StartAsync(C(DeviceRole.DebugClient,gp,lp,tp));using var ct=new CancellationTokenSource(20000);byte[] sent=Enumerable.Range(0,size).Select(i=>(byte)(i%251)).ToArray();
var server=Task.Run(async()=>{using var c=await target.AcceptTcpClientAsync(ct.Token);using var memory=new MemoryStream();var stream=c.GetStream();if(targetFirst){await stream.WriteAsync(sent,ct.Token);c.Client.Shutdown(SocketShutdown.Send);await stream.CopyToAsync(memory,ct.Token);Assert(memory.ToArray().SequenceEqual(sent),"target-first reverse data intact");}else{await stream.CopyToAsync(memory,ct.Token);Assert(memory.ToArray().SequenceEqual(sent),"request reaches target before FIN");await Task.Delay(size==65536?4500:150);await stream.WriteAsync(memory.ToArray(),ct.Token);c.Client.Shutdown(SocketShutdown.Send);}});
try{using var local=new TcpClient();await local.ConnectAsync(IPAddress.Loopback,lp);using var memory=new MemoryStream();var stream=local.GetStream();if(targetFirst){await stream.CopyToAsync(memory,ct.Token);Assert(memory.ToArray().SequenceEqual(sent),"target-first payload intact");await stream.WriteAsync(sent,ct.Token);local.Client.Shutdown(SocketShutdown.Send);}else{await stream.WriteAsync(sent,ct.Token);local.Client.Shutdown(SocketShutdown.Send);await stream.CopyToAsync(memory,ct.Token);Assert(memory.ToArray().SequenceEqual(sent),$"client-first half-close {size} bytes intact");}await server;await Task.Delay(300);Assert(g.Current.ActiveConnections==0&&b.Current.ActiveConnections==0,"both EOF release connections");}finally{await b.StopAsync();await g.StopAsync();target.Stop();}
}
static async Task LimitTests(){
var read=typeof(SafeTcpProxyService).GetMethod("ReadLineAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
foreach(byte value in new[]{(byte)'x',(byte)'\r'}){
using var input=new MemoryStream(Enumerable.Repeat(value,512*1024+1).ToArray());bool rejected=false;
try{await (Task<string>)read.Invoke(null,new object[]{input,CancellationToken.None})!;}catch(InvalidDataException){rejected=true;}
Assert(rejected && input.Position<=512*1024,"wire length including CR bounded");}
using(var input=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello\r\n")))Assert(await (Task<string>)read.Invoke(null,new object[]{input,CancellationToken.None})! == "hello","CRLF compatible");
var g=S();int gp=Free();var cfg=C(DeviceRole.GatewayAgent,gp,Free(),Free());cfg.ProxyIdleTimeoutSeconds=30;await g.StartAsync(cfg);var clients=new List<TcpClient>();
try{
for(int i=0;i<SafeTcpProxyService.MaxConcurrentConnections;i++){var c=new TcpClient();clients.Add(c);await c.ConnectAsync(IPAddress.Loopback,gp);}
var deadline=DateTime.UtcNow.AddSeconds(5);while(g.Current.ActiveConnections<64 && DateTime.UtcNow<deadline)await Task.Delay(20);
Assert(g.Current.ActiveConnections==64,"64 unauthenticated connections admitted");
using(var extra=new TcpClient()){await extra.ConnectAsync(IPAddress.Loopback,gp);await Closed(extra);}
Assert(g.Current.ActiveConnections==64,"excess socket not admitted");
clients[0].Dispose();await Task.Delay(200);
using(var replacement=new TcpClient()){await replacement.ConnectAsync(IPAddress.Loopback,gp);await Task.Delay(100);Assert(g.Current.ActiveConnections==64,"slot reused after client closes");}
await g.StopAsync();await Task.Delay(300);
var sem=(SemaphoreSlim)typeof(SafeTcpProxyService).GetField("_connectionSlots",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
Assert(sem.CurrentCount==64,"stop returns every admission slot");
}finally{foreach(var c in clients)c.Dispose();await g.StopAsync();}
}
static async Task Main(string[] args){await LimitTests();await TimeoutTests();if(args.Contains("half")){foreach(int size in new[]{0,1,65536,1048576})await HalfClose(false,size);await HalfClose(true,65536);}Console.WriteLine("ALL REQUESTED TESTS PASSED");}
}
class Ports:IPortAvailabilityService{public PortCheckResult CheckPort(int p,PortProtocol t)=>new(){Port=p,IsAvailable=true};public PortCheckResult FindAvailablePort(int p,PortProtocol t,int maxAttempts=100)=>CheckPort(p,t);}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string s){}public void Warning(string s){}public void Error(string s,Exception? e=null){Console.WriteLine("ERROR "+s+e?.Message);}public void Audit(string s){}public IReadOnlyList<string> GetLogs()=>Array.Empty<string>();}
