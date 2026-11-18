// Headless view-model tests only. Actual WPF is compiled separately.
namespace System.Windows {
public class Application {public static Application? Current=>null;public TestDispatcher? Dispatcher=>null;}
public class TestDispatcher {public bool CheckAccess()=>true;public void BeginInvoke(System.Action a)=>a();}
}
