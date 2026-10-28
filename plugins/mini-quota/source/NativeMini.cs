using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

namespace UsagePet {
// Observe official windows and clicks; do not consume or synthesize input.
public sealed class NativeMini : IDisposable {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;public int Width{get{return Right-Left;}}public int Height{get{return Bottom-Top;}}}
 [StructLayout(LayoutKind.Sequential)] struct Pt {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct MouseInfo {public Pt pt;public uint mouseData,flags,time;public IntPtr extra;}
 delegate bool EnumProc(IntPtr h,IntPtr p);delegate IntPtr HookProc(int n,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f,IntPtr l);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out int pid);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int i);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr h,uint flag);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Pt p);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,HookProc f,IntPtr module,uint tid);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string s);
 const int Topmost=8;const int AnchorPadding=20;
 static readonly TextFileCache stateFile=new TextFileCache(),configFile=new TextFileCache();
 static string parsedText;static Dictionary<string,object> parsedState;
 public static string LastReason="not-scanned";
 public sealed class Candidate {public IntPtr Handle;public Rect Bounds,Anchor;public string Title;public int Pid;public int Style;}
 static bool IsOfficial(int pid){try{using(var process=Process.GetProcessById(pid)){
  var path=process.MainModule.FileName;var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps")+"\\OpenAI.Codex_";
  return path.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&(path.EndsWith("\\app\\ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||path.EndsWith("\\app\\Codex.exe",StringComparison.OrdinalIgnoreCase));
 }}catch(Exception error){Diag.Failure("window","process-query-failed",error);return false;}}
 public static bool OfficialRunning(){foreach(var name in new[]{"Codex","ChatGPT"}){var processes=Process.GetProcessesByName(name);bool found=false;foreach(var p in processes)using(p){if(IsOfficial(p.Id))found=true;}if(found)return true;}return false;}
 public static List<Candidate> Candidates(){var list=new List<Candidate>();var cache=new Dictionary<int,bool>();
  EnumWindows((h,l)=>{try{if(!IsWindowVisible(h)||IsIconic(h)||(GetWindowLong(h,-20)&Topmost)==0)return true;
   Rect r;if(!GetWindowRect(h,out r)||r.Width<60||r.Height<50||r.Width>1600||r.Height>1800)return true;
   int pid;GetWindowThreadProcessId(h,out pid);bool valid;if(!cache.TryGetValue(pid,out valid)){valid=IsOfficial(pid);cache[pid]=valid;}if(!valid)return true;
   var title=new StringBuilder(512);GetWindowText(h,title,512);list.Add(new Candidate{Handle=h,Bounds=r,Title=title.ToString(),Pid=pid,Style=GetWindowLong(h,-20)});
  }catch(Exception error){Diag.Failure("window","enumeration-failed",error);}return true;},IntPtr.Zero);return list;
 }
 // Fail closed when location matching is ambiguous.
 public static Candidate Match(List<Candidate> candidates){Candidate selected=null;foreach(var c in candidates){
  if(c.Anchor.Left<c.Bounds.Left-AnchorPadding||c.Anchor.Top<c.Bounds.Top-AnchorPadding||c.Anchor.Right>c.Bounds.Right+AnchorPadding||c.Anchor.Bottom>c.Bounds.Bottom+AnchorPadding)continue;
  if(selected!=null){LastReason="ambiguous-window";return null;}selected=c;
 }LastReason=selected==null?"anchor-outside-window":"ready";return selected;}
 public static Rect MakeAnchor(double x,double y,double displayX,double displayY,double displayWidth,Rect screen,double size){
  double scale=displayWidth>0?screen.Width/displayWidth:1;int left=(int)Math.Round(screen.Left+(x-displayX)*scale),top=(int)Math.Round(screen.Top+(y-displayY)*scale);
  return new Rect{Left=left,Top=top,Right=left+(int)(size*scale),Bottom=top+(int)(size*208/192*scale)};
 }
 static Candidate Reason(string reason){LastReason=reason;Diag.Event("window",reason);return null;}
 public static Candidate Selected(){try{
  var text=stateFile.Read(Path.Combine(AppPaths.CodexHome,".codex-global-state.json"));if(text==null)return Reason("state-unavailable");
  if(!object.ReferenceEquals(text,parsedText)){parsedState=new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<Dictionary<string,object>>(text);parsedText=text;}
  var state=parsedState;if(!object.Equals(Data.Get(state,"electron-avatar-overlay-open"),true))return Reason("pet-closed");
  var saved=Data.Obj(Data.Get(state,"electron-avatar-overlay-bounds"));if(saved==null)return Reason("bounds-missing");
  var display=Data.Obj(Data.Get(saved,"displayBounds"));if(display==null)return Reason("display-missing");
  double x,y,dw,dx,dy;if(!QuotaModel.Number(Data.Get(saved,"x"),out x)||!QuotaModel.Number(Data.Get(saved,"y"),out y)||!QuotaModel.Number(Data.Get(display,"width"),out dw)||dw<=0||!QuotaModel.Number(Data.Get(display,"x"),out dx)||!QuotaModel.Number(Data.Get(display,"y"),out dy))return Reason("invalid-bounds");
  double size=112;var config=configFile.Read(Path.Combine(AppPaths.CodexHome,"config.toml"));if(config!=null){var match=System.Text.RegularExpressions.Regex.Match(config,@"(?m)^\s*avatar-overlay-mascot-width-px\s*=\s*(\d+)");double value;if(match.Success&&double.TryParse(match.Groups[1].Value,out value))size=Math.Max(80,Math.Min(224,value));}
  var all=Candidates();if(all.Count==0)return Reason("official-window-not-found");foreach(var c in all){var area=Forms.Screen.FromRectangle(new System.Drawing.Rectangle(c.Bounds.Left,c.Bounds.Top,c.Bounds.Width,c.Bounds.Height)).Bounds;
   c.Anchor=MakeAnchor(x,y,dx,dy,dw,new Rect{Left=area.Left,Top=area.Top,Right=area.Right,Bottom=area.Bottom},size);}
  var selected=Match(all);if(selected==null)Diag.Event("window",LastReason);return selected;
 }catch(Exception error){Diag.Failure("window","state-invalid",error);return Reason("state-invalid");}}
 public Candidate Current;public Action Changed,Clicked;DispatcherTimer timer;HookProc hookProc;IntPtr hook;Dispatcher ui;
 IntPtr pressedWindow;Pt pressed;uint pressTime;
 public NativeMini(Dispatcher dispatcher){ui=dispatcher;hookProc=OnMouse;InstallHook();timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(s,e)=>Scan();Scan();timer.Start();}
 void InstallHook(){hook=SetWindowsHookEx(14,hookProc,GetModuleHandle(null),0);if(hook==IntPtr.Zero)Diag.Event("mouse","hook-install-failed",Marshal.GetLastWin32Error().ToString());else Diag.Event("mouse","hook-ready");}
 public static bool Same(Candidate a,Candidate b){if(a==null||b==null)return a==b;return a.Handle==b.Handle&&a.Anchor.Left==b.Anchor.Left&&a.Anchor.Top==b.Anchor.Top&&a.Anchor.Right==b.Anchor.Right&&a.Anchor.Bottom==b.Anchor.Bottom;}
 void Scan(){var next=Selected();bool changed=!Same(Current,next);Current=next;timer.Interval=TimeSpan.FromMilliseconds(next==null?3000:500);if(changed&&Changed!=null)Changed();if(hook==IntPtr.Zero)InstallHook();}
 IntPtr OnMouse(int code,IntPtr message,IntPtr data){try{if(code>=0){int m=message.ToInt32();if(m==0x201||m==0x202){var info=(MouseInfo)Marshal.PtrToStructure(data,typeof(MouseInfo));if(m==0x201){pressedWindow=IntPtr.Zero;var c=Current;if(c!=null){var hit=GetAncestor(WindowFromPoint(info.pt),2);if(hit==c.Handle&&info.pt.X>=c.Anchor.Left-8&&info.pt.X<=c.Anchor.Right+8&&info.pt.Y>=c.Anchor.Top-8&&info.pt.Y<=c.Anchor.Bottom){pressedWindow=c.Handle;pressed=info.pt;pressTime=info.time;}}}else{var c=Current;if(c!=null&&pressedWindow==c.Handle&&Math.Abs(info.pt.X-pressed.X)<=6&&Math.Abs(info.pt.Y-pressed.Y)<=6&&info.time-pressTime<600)ui.BeginInvoke(new Action(()=>{if(Clicked!=null)Clicked();}));pressedWindow=IntPtr.Zero;}}}}catch(Exception error){Diag.Failure("mouse","click-failed",error);}return CallNextHookEx(hook,code,message,data);}
 public void Dispose(){timer.Stop();if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
 public static object Diagnostic(){var windows=new List<object>();foreach(var c in Candidates())windows.Add(new{pid=c.Pid,x=c.Bounds.Left,y=c.Bounds.Top,width=c.Bounds.Width,height=c.Bounds.Height});var selected=Selected();return new{version=AppInfo.Version,reason=LastReason,windows=windows,miniDetected=selected!=null,anchor=selected==null?null:(object)new{x=selected.Anchor.Left,y=selected.Anchor.Top,width=selected.Anchor.Width,height=selected.Anchor.Height}};}
}
}
