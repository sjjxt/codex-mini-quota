using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UsagePet;

namespace MiniQuotaBootstrap {
 static class Program {
  static JavaScriptSerializer json=new JavaScriptSerializer();
  static string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexMiniQuota");
  static void Record(string evt,string client,bool launched){try{Directory.CreateDirectory(folder);var file=Path.Combine(folder,"plugin-bootstrap.jsonl");if(File.Exists(file)&&new FileInfo(file).Length>1000000)File.WriteAllText(file,"");File.AppendAllText(file,json.Serialize(new{evt=evt,pid=Process.GetCurrentProcess().Id,client=client,launched=launched,utc=DateTime.UtcNow.ToString("o")})+"\n");}catch{}}
  static object Get(Dictionary<string,object> d,string k){object v;return d!=null&&d.TryGetValue(k,out v)?v:null;}
  static bool? HelperRunning(){try{using(var m=Mutex.OpenExisting("Local\\MiniQuotaCompanion_v2"))return true;}catch(WaitHandleCannotBeOpenedException){return false;}catch(UnauthorizedAccessException e){Diag.Failure("bootstrap","helper-status-denied",e);return null;}}
  static bool Start(){
   using(var launch=new Mutex(false,"Local\\MiniQuotaLaunch_v2")){
    bool owned=false;try{try{owned=launch.WaitOne(3000);}catch(AbandonedMutexException){owned=true;}
     if(!owned){Diag.Event("bootstrap","launch-busy");return false;}
     if(HelperRunning()!=false)return false;
     var exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MiniQuota.exe");if(!File.Exists(exe)){Diag.Event("bootstrap","helper-missing");return false;}
     // App-scoped MCP inventory destroys its temporary service process tree.
     // Ask the ordinary desktop Shell to activate this UI instead of making it
     // a child of that temporary service. No elevation, task or startup entry.
     using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\MiniQuotaReady_v2")){
      ready.Reset();object factory=null,windows=null,desktop=null,document=null,shell=null;
      try{factory=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application",true));
       // Shell.Application itself may execute in the caller. Obtain the desktop
       // folder's Application object, whose implementation lives in Explorer.
       windows=((dynamic)factory).Windows();int desktopHandle;
       desktop=((dynamic)windows).FindWindowSW(0,0,8,out desktopHandle,1);
       if(desktop==null)throw new InvalidOperationException("Desktop Shell unavailable");
       document=((dynamic)desktop).Document;shell=((dynamic)document).Application;
       var args="--codex-home-base64 "+Convert.ToBase64String(Encoding.UTF8.GetBytes(AppPaths.CodexHome));
       ((dynamic)shell).ShellExecute(exe,args,Path.GetDirectoryName(exe),"open",0);
      }finally{foreach(var item in new[]{shell,document,desktop,windows,factory})if(item!=null&&Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item);}
      Diag.Event("bootstrap","shell-start-requested");
      if(!ready.WaitOne(4000)){Diag.Event("bootstrap","helper-ready-timeout");return false;}
      Diag.Event("bootstrap","helper-ready");return true;
     }
    }finally{if(owned)launch.ReleaseMutex();}
   }
  }
  static void Reply(object id,object result){Console.WriteLine(json.Serialize(new{jsonrpc="2.0",id=id,result=result}));Console.Out.Flush();}
  [STAThread]static int Main(){Diag.Prune();Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);Record("process-start",null,false);string line;
   while((line=Console.ReadLine())!=null){object id=null;try{var msg=json.Deserialize<Dictionary<string,object>>(line);id=Get(msg,"id");var method=Convert.ToString(Get(msg,"method"));var param=Get(msg,"params")as Dictionary<string,object>;
    if(method=="initialize"){
     var client=Get(param,"clientInfo")as Dictionary<string,object>;bool launched=false;try{launched=Start();}catch(Exception e){Diag.Failure("bootstrap","helper-start-failed",e);Console.Error.WriteLine("mini-quota: helper-start-failed");}
     Record("initialize",Convert.ToString(Get(client,"name")),launched);
     Reply(id,new{protocolVersion=Convert.ToString(Get(param,"protocolVersion")??"2024-11-05"),capabilities=new{tools=new{}},serverInfo=new{name="mini-quota-bootstrap",version=AppInfo.Version}});
    }else if(id==null){}else if(method=="ping")Reply(id,new{});
    else if(method=="tools/list")Reply(id,new{tools=new[]{new{name="quota_component_status",description="Read whether the local Mini usage bubble component is running.",inputSchema=new{type="object",properties=new{},additionalProperties=false},annotations=new{readOnlyHint=true,openWorldHint=false}}}});
    else if(method=="tools/call"){bool valid=Convert.ToString(Get(param,"name"))=="quota_component_status";Reply(id,new{content=new[]{new{type="text",text=valid?json.Serialize(new{running=HelperRunning(),version=AppInfo.Version}):"Unknown tool"}},isError=!valid});}
    else if(method=="resources/list")Reply(id,new{resources=new object[0]});
    else if(method=="resources/templates/list")Reply(id,new{resourceTemplates=new object[0]});
    else if(method=="prompts/list")Reply(id,new{prompts=new object[0]});
    else{Console.WriteLine(json.Serialize(new{jsonrpc="2.0",id=id,error=new{code=-32601,message="Method not found"}}));Console.Out.Flush();}
   }catch(Exception e){Diag.Failure("bootstrap","invalid-request",e);Console.Error.WriteLine("mini-quota: invalid-request");if(id!=null){Console.WriteLine(json.Serialize(new{jsonrpc="2.0",id=id,error=new{code=-32603,message="Internal service error"}}));Console.Out.Flush();}}}
   Record("stdin-closed",null,false);return 0;
  }
 }
}
