using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using UsagePet;
class Regression {
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static Dictionary<string,object> Json(string text){return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);}
 static NativeMini.Rect Rect(int x,int y,int w,int h){return new NativeMini.Rect{Left=x,Top=y,Right=x+w,Bottom=y+h};}
 static NativeMini.Candidate Window(int id,int x){return new NativeMini.Candidate{Handle=new IntPtr(id),Bounds=Rect(x,0,300,400),Anchor=Rect(20,20,112,121),Title="renamed window"};}
 [STAThread]static int Main(string[] args){try{
  var row=QuotaModel.Parse(Json("{\"rateLimits\":{\"primary\":{\"usedPercent\":18,\"windowDurationMins\":300}}}"));Check(row.Count==1&&row[0].Remaining==82,"legacy quota");
  row=QuotaModel.Parse(Json("{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300}}}"));Check(row.Count==1&&!row[0].Remaining.HasValue,"missing percentage preserved as unavailable");
  row=QuotaModel.Parse(Json("{\"rateLimits\":{\"primary\":{\"usedPercent\":null},\"secondary\":{\"usedPercent\":0}}}"));Check(row.Count==2&&!row[0].Remaining.HasValue&&row[1].Remaining==100,"null differs from zero");
  row=QuotaModel.Parse(Json("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"bad\",\"resetsAt\":1e30}}}"));Check(!row[0].Remaining.HasValue&&!row[0].Reset.HasValue,"malformed values cannot crash rendering");
  row=QuotaModel.Parse(Json("{\"rateLimits\":{\"primary\":{\"usedPercent\":-2},\"secondary\":{\"usedPercent\":101}}}"));Check(row[0].Remaining==100&&row[1].Remaining==0,"percent bounds");
  Check(QuotaModel.Parse(Json("{}")).Count==0,"empty response");
  row=QuotaModel.Parse(Json("{\"rateLimitsByLimitId\":{\"other\":{\"primary\":{}},\"codex\":{\"secondary\":{}}}}"));Check(row.Count==2&&row[0].Bucket=="codex","multiple buckets and priority");
  row=QuotaModel.Parse(Json("{\"rateLimitsByLimitId\":{\"bad\":null},\"rateLimits\":{\"primary\":{}}}"));Check(row.Count==1,"invalid map falls back to legacy");
  double number;Check(!QuotaModel.Number(double.NaN,out number)&&!QuotaModel.Number(true,out number),"invalid numeric types");
  var temp=Path.Combine(args[1],"cache-test.txt");var cache=new TextFileCache();File.WriteAllText(temp,"first");Check(cache.Read(temp)=="first","initial cache read");for(int i=0;i<100;i++)cache.Read(temp);Check(cache.ReadCount==1,"100 unchanged scans avoid file reads");
  File.WriteAllText(temp,"second-longer");Check(cache.Read(temp)=="second-longer"&&cache.ReadCount==2,"cache invalidation");File.Delete(temp);Check(cache.Read(temp)==null,"deleted state file");File.WriteAllText(temp,"reopened");Check(cache.Read(temp)=="reopened","state file reappears");
  using(var locked=new FileStream(temp,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){cache.Invalidate();Check(cache.Read(temp)==null,"locked state file handled");}Check(cache.Read(temp)=="reopened","recover from locked file");File.Delete(temp);
  var a=Window(1,0);var b=Window(2,800);Check(NativeMini.Match(new List<NativeMini.Candidate>{a,b})==a,"multiple windows matched by location");
  Check(NativeMini.Match(new List<NativeMini.Candidate>{a,Window(3,0)})==null&&NativeMini.LastReason=="ambiguous-window","ambiguous windows fail closed");
  Check(NativeMini.Same(a,Window(1,0))&&!NativeMini.Same(a,b),"unchanged selection suppresses notifications");
  var anchor=NativeMini.MakeAnchor(-1800,100,-1920,0,1920,Rect(-2400,0,2400,1350),112);Check(anchor.Left==-2250&&anchor.Top==125&&anchor.Width==140,"negative monitor coordinates and scaling");
  var previousHome=Environment.GetEnvironmentVariable("CODEX_HOME");Environment.SetEnvironmentVariable("CODEX_HOME",args[1]);Check(AppPaths.CodexHome==Path.GetFullPath(args[1]),"CODEX_HOME honored");
  var state=Path.Combine(args[1],".codex-global-state.json");File.WriteAllText(state,"{\"electron-avatar-overlay-open\":false}");Check(NativeMini.Selected()==null&&NativeMini.LastReason=="pet-closed","closed pet");
  File.WriteAllText(state,"{");Check(NativeMini.Selected()==null&&NativeMini.LastReason=="state-invalid","partial state JSON");
  File.WriteAllText(state,"{\"electron-avatar-overlay-open\":true}");Check(NativeMini.Selected()==null&&NativeMini.LastReason=="bounds-missing","reopen pet missing bounds");File.Delete(state);Environment.SetEnvironmentVariable("CODEX_HOME",previousHome);
  foreach(var mode in new[]{"success","error","timeout","disconnect"}){
   Environment.SetEnvironmentVariable("MINI_QUOTA_TEST_MODE",mode);bool failed=false;using(var client=new QuotaClient(args[0],500))try{var result=client.Read().GetAwaiter().GetResult();Check(mode=="success"&&QuotaModel.Parse(result)[0].Remaining==82,"RPC success");}catch(Exception error){failed=true;Check(!error.Message.Contains("SECRET_DO_NOT_LOG"),"RPC "+mode+" redaction");if(mode=="timeout")Check(error is TimeoutException,"RPC timeout classified");}
   Check(failed==(mode!="success"),"RPC "+mode+" outcome");
  }
  Environment.SetEnvironmentVariable("MINI_QUOTA_TEST_MODE",null);Check(QuotaClient.ClassifyStderr("authentication SECRET")=="auth","stderr category excludes content");
  Console.WriteLine("C# regression checks: "+count);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
