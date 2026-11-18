using System.Reflection;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Logging;
var type=typeof(SafeTcpProxyService);var flags=BindingFlags.NonPublic|BindingFlags.Instance;
var validate=type.GetMethod("ValidateSignedMessage",flags)!;
var sign=type.GetMethod("SignMessage",BindingFlags.NonPublic|BindingFlags.Static)!;
var cacheField=type.GetField("_recentNonces",flags)!;
const string key="test-key-only-not-production";int count=0;
void Assert(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
SafeTcpProxyService Service()=>new(new Ports(),new Log());
TcpProxyControlMessage Message(string? nonce=null){var m=new TcpProxyControlMessage{ProtocolVersion=1,SessionToken="test-session",Nonce=nonce??Guid.NewGuid().ToString("N"),MessageType=TcpProxyMessageType.OpenConnection};sign.Invoke(null,new object[]{m,key});return m;}
string Check(SafeTcpProxyService s,TcpProxyControlMessage m)=>(string)validate.Invoke(s,new object?[]{m,key,null})!;
Dictionary<string,DateTimeOffset> Cache(SafeTcpProxyService s)=>(Dictionary<string,DateTimeOffset>)cacheField.GetValue(s)!;
var service=Service();var real=Message("known-public-nonce");var forged=Message(real.Nonce);forged.Signature=new string('0',64);
Assert(Check(service,forged)!="","forged signature rejected");
Assert(Cache(service).Count==0,"invalid signature cannot poison replay cache");
Assert(Check(service,real)=="","legitimate nonce still usable after forged attempt");
Assert(Check(service,real)!="","authenticated replay rejected");
Assert(Cache(service).Count==1,"duplicate nonce does not grow cache");

var oversized=Message(new string('n',129));Assert(Check(service,oversized)!=""&&Cache(service).Count==1,"oversized signed nonce rejected without cache mutation");
var flood=Service();for(int i=0;i<1000;i++){var m=Message();m.Signature=new string('0',64);AssertRejected(Check(flood,m));}
Assert(Cache(flood).Count==0,"1000 unauthenticated attempts leave cache empty");
void AssertRejected(string error){if(error=="")throw new Exception("accepted forged signature");}
var bounded=Service();var capacity=(int)type.GetField("MaxRememberedNonces",BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()!;
var queue=(Queue<(string Nonce,DateTimeOffset RememberedAt)>)type.GetField("_nonceExpiry",flags)!.GetValue(bounded)!;
var timestamp=DateTimeOffset.UtcNow;
for(int i=0;i<capacity;i++){string nonce="capacity-"+i;Cache(bounded).Add(nonce,timestamp);queue.Enqueue((nonce,timestamp));}
Assert(Check(bounded,Message()).Contains("容量上限")&&Cache(bounded).Count==capacity,"cache saturation fails closed and stays bounded");
Assert(Check(bounded,Message("capacity-0")).Contains("重复 nonce"),"saturation never evicts active replay evidence");
// Set an expired FIFO head to simulate time passing without a ten-minute sleep.
var expired=queue.Dequeue();queue.Clear();queue.Enqueue((expired.Nonce,DateTimeOffset.UtcNow-TimeSpan.FromMinutes(11)));
for(int i=1;i<capacity;i++)queue.Enqueue(("capacity-"+i,timestamp));
Assert(Check(bounded,Message())==""&&Cache(bounded).Count==capacity,"expired slot reclaimed before capacity check");
Assert(!Cache(bounded).ContainsKey(expired.Nonce)&&queue.Count==capacity,"expiry removes dictionary and queue entries together");
var parallel=Service();var same=Message();var results=new string[32];Parallel.For(0,results.Length,i=>results[i]=Check(parallel,same));
Assert(results.Count(r=>r=="")==1&&Cache(parallel).Count==1,"concurrent identical authenticated messages accepted once");
Console.WriteLine($"All {count} nonce tests passed.");
class Ports:IPortAvailabilityService{public PortCheckResult CheckPort(int p,PortProtocol t)=>new(){Port=p,IsAvailable=true};public PortCheckResult FindAvailablePort(int p,PortProtocol t,int maxAttempts=100)=>CheckPort(p,t);}
class Log:ILogService{public event EventHandler<string>? EntryAdded;public string LogFilePath=>"";public void Info(string s){}public void Warning(string s){}public void Error(string s,Exception? e=null){}public void Audit(string s){}public IReadOnlyList<string> GetLogs()=>Array.Empty<string>();}
