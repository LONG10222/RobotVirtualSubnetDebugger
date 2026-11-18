using System.Net;
using System.Net.Sockets;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Logging;
int count=0;
void Assert(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
void Denied(string host,int port,string request,int rp,string name){try{GatewayTargetPolicy.Authorize(host,port,request,rp);throw new Exception("FAIL accepted "+name);}catch(UnauthorizedAccessException){Assert(true,name);}}
Assert(GatewayTargetPolicy.Authorize("127.0.0.1",1234,"",0)==("127.0.0.1",1234),"omitted target uses gateway config");
Assert(GatewayTargetPolicy.Authorize("::1",1234,"0:0:0:0:0:0:0:1",1234)==("::1",1234),"canonical IPv6 allowed");
Denied("127.0.0.1",1234,"127.0.0.2",1234,"different IP denied");
Denied("127.0.0.1",1234,"127.0.0.1",1235,"different port denied");
Denied("127.0.0.1",1234,"localhost",1234,"hostname alias cannot override literal");
Denied("",1234,"127.0.0.1",1234,"missing gateway target fails closed");
Denied("127.0.0.1",0,"",0,"invalid gateway port fails closed");
Denied("127.0.0.1",1234,"",-1,"negative request port denied");
int Free(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();var p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
AppConfig Config(DeviceRole r,int gp,int lp,int tp,string host="127.0.0.1",string key="test-only-security-key")=>new(){Role=r,ProxyControlPort=gp,LocalListenPort=lp,TargetDeviceIp=host,TargetDevicePort=tp,GatewayLanIp="127.0.0.1",SharedKey=key,ProxyHeartbeatIntervalSeconds=1,ProxyIdleTimeoutSeconds=3,ProxyReconnectAttempts=0};
async Task Test(bool allowed,string host,bool badKey=false){
 var target=new TcpListener(IPAddress.Loopback,0);target.Start();int tp=((IPEndPoint)target.LocalEndpoint).Port;int gp=Free(),lp=Free();
 var g=new SafeTcpProxyService(new Ports(),new Log());var b=new SafeTcpProxyService(new Ports(),new Log());
 using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(7));int accepted=0;
 var server=Task.Run(async()=>{try{using var peer=await target.AcceptTcpClientAsync(stop.Token);Interlocked.Increment(ref accepted);await peer.GetStream().CopyToAsync(peer.GetStream(),stop.Token);}catch(OperationCanceledException){}catch(IOException){}catch(SocketException){}});
 try{
 await g.StartAsync(Config(DeviceRole.GatewayAgent,gp,lp,allowed?tp:Free()));
 if(!allowed) g.Current.TargetDevicePort=tp; // UI state cannot expand the startup allowlist.
 await b.StartAsync(Config(DeviceRole.DebugClient,gp,lp,tp,host,badKey?"wrong-test-key":"test-only-security-key"));
 using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,lp,stop.Token);var stream=client.GetStream();
 await stream.WriteAsync(new byte[]{42},stop.Token);var buf=new byte[1];int n=0;try{n=await stream.ReadAsync(buf,stop.Token);}catch(IOException){}
 if(allowed&&!badKey)Assert(n==1&&buf[0]==42&&accepted==1,"real authorized echo succeeds");
 else{
 string rejection="";
 if(!badKey){using var remaining=new MemoryStream();if(n>0)remaining.Write(buf,0,n);await stream.CopyToAsync(remaining,stop.Token);rejection=System.Text.Encoding.UTF8.GetString(remaining.ToArray());}
 await Task.Delay(150);
 Assert(accepted==0 && (badKey ? n==0 : rejection.StartsWith("RobotNet TCP proxy failed:") && rejection.Contains("授权范围")),badKey?"wrong key rejected before target connect":"real unauthorized target rejected despite mutable UI state");
 }
 }finally{await b.StopAsync();await g.StopAsync();stop.Cancel();target.Stop();await server;}
}
await Test(true,"127.0.0.1");await Test(false,"127.0.0.1");await Test(true,"127.0.0.1",true);
Console.WriteLine($"All {count} target authorization tests passed.");
class Ports:IPortAvailabilityService{public PortCheckResult CheckPort(int p,PortProtocol t)=>new(){Port=p,IsAvailable=true};public PortCheckResult FindAvailablePort(int p,PortProtocol t,int maxAttempts=100)=>CheckPort(p,t);}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string s){}public void Warning(string s){}public void Error(string s,Exception? e=null){}public void Audit(string s){}public IReadOnlyList<string> GetLogs()=>Array.Empty<string>();}
