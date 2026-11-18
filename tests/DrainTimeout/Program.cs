using System.Net;using System.Net.Sockets;using System.Reflection;using System.Text;using System.Text.Json;
using RobotNet.Windows.Wpf.Models;using RobotNet.Windows.Wpf.Services.Proxy;using RobotNet.Windows.Wpf.Services.Logging;using RobotNet.Windows.Wpf.Services.Platform;
const string key="test-only-security-key";var sign=typeof(SafeTcpProxyService).GetMethod("SignMessage",BindingFlags.NonPublic|BindingFlags.Static)!;
int Free(){var t=new TcpListener(IPAddress.Loopback,0);t.Start();var p=((IPEndPoint)t.LocalEndpoint).Port;t.Stop();return p;}
async Task Send(Stream s,TcpProxyMessageType type,int port,CancellationToken token){var m=new TcpProxyControlMessage{ProtocolVersion=1,SessionToken="drain-test-session",SessionId="drain",ConnectionId="conn",MessageType=type,TargetHost="127.0.0.1",TargetPort=port};sign.Invoke(null,new object[]{m,key});await s.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m,new JsonSerializerOptions(JsonSerializerDefaults.Web))+"\n"),token);}
var target=new TcpListener(IPAddress.Loopback,0);target.Start();int tp=((IPEndPoint)target.LocalEndpoint).Port,gp=Free();var g=new SafeTcpProxyService(new Ports(),new Log());using var limit=new CancellationTokenSource(15000);var token=limit.Token;
try{
 await g.StartAsync(new AppConfig{Role=DeviceRole.GatewayAgent,ProxyControlPort=gp,LocalListenPort=Free(),TargetDeviceIp="127.0.0.1",TargetDevicePort=tp,SharedKey=key,ProxyHeartbeatIntervalSeconds=1,ProxyIdleTimeoutSeconds=3,ProxyReconnectAttempts=0});
 using var peer=new TcpClient();await peer.ConnectAsync(IPAddress.Loopback,gp,token);var stream=peer.GetStream();await Send(stream,TcpProxyMessageType.OpenConnection,tp,token);
 using var accepted=await target.AcceptTcpClientAsync(token);accepted.Client.Shutdown(SocketShutdown.Send);using var reader=new StreamReader(stream,Encoding.UTF8,leaveOpen:true);
 bool eof=false;while(!eof){var line=await reader.ReadLineAsync(token);if(line==null)throw new Exception("premature transport close");var m=JsonSerializer.Deserialize<TcpProxyControlMessage>(line,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;eof=m.MessageType==TcpProxyMessageType.EndOfStream;}
 await Send(stream,TcpProxyMessageType.EndOfStream,tp,token);
 for(int i=0;i<12;i++){await Task.Delay(400,token);try{await Send(stream,TcpProxyMessageType.Heartbeat,tp,token);}catch(IOException){break;}}
 Console.WriteLine($"After application EOF and >4.8s heartbeats (idle timeout 3s), ActiveConnections={g.Current.ActiveConnections}");
 if(g.Current.ActiveConnections!=0)throw new Exception("FAIL: absolute drain timeout missing; heartbeat keeps completed connection alive");
 Console.WriteLine("PASS: completed connection drain deadline enforced despite heartbeats");
}finally{await g.StopAsync();target.Stop();}
class Ports:IPortAvailabilityService{public PortCheckResult CheckPort(int p,PortProtocol t)=>new(){Port=p,IsAvailable=true};public PortCheckResult FindAvailablePort(int p,PortProtocol t,int maxAttempts=100)=>CheckPort(p,t);}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string s){}public void Warning(string s){}public void Error(string s,Exception? e=null){}public void Audit(string s){}public IReadOnlyList<string> GetLogs()=>Array.Empty<string>();}
