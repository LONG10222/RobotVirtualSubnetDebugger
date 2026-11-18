using System.Net;
using System.Net.Sockets;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
static int Free(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
static void Assert(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
static AppConfig C(DeviceRole r,int gp,int lp,int tp)=>new(){Role=r,ProxyControlPort=gp,LocalListenPort=lp,TargetDeviceIp="127.0.0.1",TargetDevicePort=tp,GatewayLanIp="127.0.0.1",SharedKey="test-only-security-key",ProxyHeartbeatIntervalSeconds=1,ProxyIdleTimeoutSeconds=5,ProxyReconnectAttempts=0};
static async Task Send(TcpClient client,byte[] data,int chunk,CancellationToken token){var stream=client.GetStream();for(int pos=0;pos<data.Length;pos+=chunk){await stream.WriteAsync(data.AsMemory(pos,Math.Min(chunk,data.Length-pos)),token);await Task.Yield();}client.Client.Shutdown(SocketShutdown.Send);}
static async Task<byte[]> Read(TcpClient client,CancellationToken token){var stream=client.GetStream();using var output=new MemoryStream();byte[] buffer=new byte[2131];while(true){int n=await stream.ReadAsync(buffer,token);if(n==0)break;await output.WriteAsync(buffer.AsMemory(0,n),token);await Task.Yield();}return output.ToArray();}
for(int round=0;round<12;round++){
 using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(30));var t=cts.Token;
 var target=new TcpListener(IPAddress.Loopback,0);target.Start();int tp=((IPEndPoint)target.LocalEndpoint).Port,gp=Free(),lp=Free();
 var gateway=new SafeTcpProxyService(new Ports(),new Log());var debug=new SafeTcpProxyService(new Ports(),new Log());
 byte[] request=new byte[1048576],response=new byte[1048576];new Random(round*2).NextBytes(request);new Random(round*2+1).NextBytes(response);
 try{
 await gateway.StartAsync(C(DeviceRole.GatewayAgent,gp,lp,tp));await debug.StartAsync(C(DeviceRole.DebugClient,gp,lp,tp));
 var server=Task.Run(async()=>{using var remote=await target.AcceptTcpClientAsync(t);var read=Read(remote,t);await Send(remote,response,round%2==0?997:8191,t);Assert((await read).SequenceEqual(request),$"round {round}: target received complete 1MiB request");});
 using var local=new TcpClient();await local.ConnectAsync(IPAddress.Loopback,lp,t);var received=Read(local,t);await Send(local,request,round%2==0?4093:1021,t);Assert((await received).SequenceEqual(response),$"round {round}: client received complete 1MiB response");await server;
 for(int i=0;i<100&&(gateway.Current.ActiveConnections!=0||debug.Current.ActiveConnections!=0);i++)await Task.Delay(20,t);
 Assert(gateway.Current.ActiveConnections==0&&debug.Current.ActiveConnections==0,$"round {round}: drained both connections");
 Assert(string.IsNullOrEmpty(gateway.Current.LastError)&&string.IsNullOrEmpty(debug.Current.LastError),$"round {round}: no proxy error");
 }finally{await debug.StopAsync();await gateway.StopAsync();target.Stop();}
}
Console.WriteLine("ALL 12 BIDIRECTIONAL 1MIB CHUNKED ROUNDS PASSED");
class Ports:IPortAvailabilityService{public PortCheckResult CheckPort(int p,PortProtocol t)=>new(){Port=p,IsAvailable=true};public PortCheckResult FindAvailablePort(int p,PortProtocol t,int maxAttempts=100)=>CheckPort(p,t);}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string s){}public void Warning(string s){}public void Error(string s,Exception? e=null){Console.WriteLine("ERROR "+s+e?.Message);}public void Audit(string s){}public IReadOnlyList<string> GetLogs()=>Array.Empty<string>();}
