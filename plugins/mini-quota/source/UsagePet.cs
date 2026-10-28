using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Globalization;
using Forms = System.Windows.Forms;

namespace UsagePet {
public sealed class QuotaClient : IDisposable {
 Process proc; int serial; readonly object gate=new object();readonly string executable;readonly int timeoutMs;
 ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
 public QuotaClient(string executable=null,int timeoutMs=20000){this.executable=executable;this.timeoutMs=timeoutMs;}
 public static string FindCodex(){var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
  if(Directory.Exists(parent)){var files=Directory.GetFiles(parent,"codex.exe",SearchOption.AllDirectories);if(files.Length>0)return files.OrderByDescending(File.GetLastWriteTimeUtc).First();}
  foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))try{var file=Path.Combine(dir,"codex.exe");if(File.Exists(file))return file;}catch{}
  throw new Exception(I18n.T("未找到 Codex，请先安装并登录 Codex 桌面版或 CLI。","Codex was not found. Install and sign in to Codex Desktop or CLI."));
 }
 public static string ClassifyStderr(string line){var text=(line??"").ToLowerInvariant();return text.Contains("unauthorized")||text.Contains("authentication")?"auth":text.Contains("timeout")||text.Contains("timed out")?"timeout":text.Contains("sqlite")?"local-storage":text.Contains("error")?"error":"diagnostic";}
 async Task Start(){if(proc!=null&&!proc.HasExited)return;Dispose();
  var si=new ProcessStartInfo(executable??FindCodex(),"app-server --listen stdio://"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
  foreach(var key in si.EnvironmentVariables.Keys.Cast<string>().Where(k=>k.StartsWith("CODEX_")&&k!="CODEX_HOME").ToArray())si.EnvironmentVariables.Remove(key);
  var active=new Process{StartInfo=si};proc=active;
  active.OutputDataReceived+=(sender,e)=>{if(!object.ReferenceEquals(proc,active))return;if(e.Data==null){Diag.Event("rpc","disconnected");FailPending();return;}
   try{var msg=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.Data);var id=Data.Get(msg,"id");TaskCompletionSource<Dictionary<string,object>> completion;
    if(id!=null&&pending.TryRemove(Convert.ToInt32(id),out completion)){
     var error=Data.Obj(Data.Get(msg,"error"));if(error!=null){double number;Diag.Event("rpc","server-error",QuotaModel.Number(Data.Get(error,"code"),out number)?number.ToString(CultureInfo.InvariantCulture):"unknown");completion.TrySetException(new Exception(I18n.T("额度接口暂不可用，请确认 Codex 已登录后重试。","Quota is unavailable. Check that Codex is signed in and retry.")));}
     else{var result=Data.Obj(Data.Get(msg,"result"));if(result==null)completion.TrySetException(new Exception(I18n.T("额度响应格式无效，请重试。","Invalid quota response. Please retry.")));else completion.TrySetResult(result);}
    }
   }catch(Exception error){Diag.Failure("rpc","invalid-response",error);}
  };
  active.ErrorDataReceived+=(sender,e)=>{if(e.Data!=null&&object.ReferenceEquals(proc,active))Diag.Event("app-server","stderr-"+ClassifyStderr(e.Data));};
  try{active.Start();active.BeginOutputReadLine();active.BeginErrorReadLine();Diag.Event("rpc","started");
   await Call("initialize",new{clientInfo=new{name="mini_quota",title="Mini Quota",version=AppInfo.Version}});Send(new{method="initialized"});
  }catch(Exception error){Diag.Failure("rpc","start-failed",error);throw;}
 }
 void Send(object msg){lock(gate){proc.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(msg));proc.StandardInput.Flush();}}
 async Task<Dictionary<string,object>> Call(string method,object param=null){int id=Interlocked.Increment(ref serial);var completion=new TaskCompletionSource<Dictionary<string,object>>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=completion;
  try{Send(new{id=id,method=method,@params=param});if(await Task.WhenAny(completion.Task,Task.Delay(timeoutMs))!=completion.Task){Diag.Event("rpc","request-timeout");throw new TimeoutException(I18n.T("连接超时，请确认网络和 Codex 登录状态，然后重试。","Connection timed out. Check your connection and Codex sign-in, then retry."));}return await completion.Task;
  }finally{TaskCompletionSource<Dictionary<string,object>> unused;pending.TryRemove(id,out unused);}
 }
 public async Task<Dictionary<string,object>> Read(){try{await Start();return await Call("account/rateLimits/read");}catch(Exception error){Diag.Failure("quota","read-failed",error);Dispose();throw;}}
 void FailPending(){foreach(var item in pending)item.Value.TrySetException(new Exception(I18n.T("Codex 连接已断开，请重试。","Codex disconnected. Please retry.")));}
 public void Dispose(){var previous=proc;proc=null;if(previous!=null){try{if(!previous.HasExited)previous.Kill();}catch(Exception error){Diag.Failure("rpc","stop-failed",error);}previous.Dispose();}FailPending();pending.Clear();}
}
public sealed class Pet : Window {
 readonly QuotaClient client=new QuotaClient();
 Window bubble;StackPanel rows;TextBlock status,plan;Button refresh;ScrollViewer quotaScroll;
 DispatcherTimer poll,countdown,lifetime; Forms.NotifyIcon tray;
 DateTime lastCodexSeen=DateTime.UtcNow;
 bool busy;string lastError;DateTime? updated;Dictionary<string,object> latest;public Action Ready;
 Brush ink=BrushOf("#4D3D68"),muted=BrushOf("#9486A8");
 public static Brush BrushOf(string s){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));}
 NativeMini native;bool started;
 public Pet() {
  Title=I18n.T("Mini 用量助手","Mini Quota Helper");Width=1;Height=1;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;
  CreateBubble();
  Loaded+=async(a,b)=>{if(started)return;started=true;native=new NativeMini(Dispatcher);native.Changed=FollowMini;native.Clicked=()=>Toggle();Hide();FollowMini();Diag.Event("helper","ready",AppInfo.Version);if(Ready!=null)Ready();await Refresh();if(Environment.GetCommandLineArgs().Contains("--preview")&&native.Current!=null)Toggle();};
  poll=new DispatcherTimer{Interval=TimeSpan.FromSeconds(60)};poll.Tick+=async(a,b)=>await Refresh();poll.Start();
  countdown=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};countdown.Tick+=(a,b)=>{I18n.Poll();if(bubble.IsVisible&&latest!=null)Render();};countdown.Start();
  lifetime=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};lifetime.Tick+=(a,b)=>{if(NativeMini.OfficialRunning())lastCodexSeen=DateTime.UtcNow;else if((DateTime.UtcNow-lastCodexSeen).TotalSeconds>=12){Diag.Event("helper","official-client-exited");Close();}};lifetime.Start();
  tray=new Forms.NotifyIcon {Icon=System.Drawing.SystemIcons.Information,Text=I18n.T("Mini 用量助手","Mini Quota Helper"),Visible=true};
  tray.ContextMenuStrip=new Forms.ContextMenuStrip();tray.ContextMenuStrip.Opening+=(a,b)=>BuildTray();
  I18n.Changed=()=>{bool visible=bubble.IsVisible;bubble.Close();CreateBubble();Title=I18n.T("Mini 用量助手","Mini Quota Helper");tray.Text=Title;if(latest!=null)Render();else status.Text=I18n.T("请点击刷新读取额度","Refresh to fetch your usage");if(visible){PositionBubble();bubble.Show();bubble.Activate();}};
  tray.DoubleClick+=(a,b)=>Dispatcher.Invoke(()=>Toggle());
  KeyDown+=(a,b)=>{if(b.Key==Key.Escape)bubble.Hide();};
  Closed+=(a,b)=>{Diag.Event("helper","closed");I18n.Changed=null;if(native!=null)native.Dispose();poll.Stop();countdown.Stop();lifetime.Stop();client.Dispose();tray.Dispose();bubble.Close();Application.Current.Shutdown();};
 }
 void FollowMini(){
  if(native==null||native.Current==null){if(bubble.IsVisible)bubble.Hide();if(IsVisible)Hide();return;}
  var r=native.Current.Anchor;var source=PresentationSource.FromVisual(bubble)??PresentationSource.FromVisual(this);var m=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
  var center=m.Transform(new Point((r.Left+r.Right)/2.0,r.Bottom));
  var screen=Forms.Screen.FromPoint(new System.Drawing.Point((r.Left+r.Right)/2,r.Bottom-1)).WorkingArea;
  var lower=m.Transform(new Point(screen.Right,screen.Bottom));
  Left=Math.Min(center.X-Width/2,lower.X-Width);Top=Math.Min(center.Y+38,lower.Y-Height-2);
  PositionBubble();
 }
 void BuildTray(){var menu=tray.ContextMenuStrip;menu.Items.Clear();menu.Items.Add(I18n.T("查看额度","Show usage"),null,(a,b)=>Dispatcher.Invoke(()=>Toggle()));
  var lang=new Forms.ToolStripMenuItem("中文 / English");foreach(var mode in new[]{"auto","zh-CN","en"}){var item=new Forms.ToolStripMenuItem(mode=="auto"?"自动 / Auto":mode=="zh-CN"?"简体中文":"English"){Checked=I18n.Mode==mode};var choice=mode;item.Click+=(a,b)=>Dispatcher.Invoke(()=>I18n.Set(choice));lang.DropDownItems.Add(item);}menu.Items.Add(lang);
  menu.Items.Add(I18n.T("退出用量助手","Exit quota helper"),null,(a,b)=>Dispatcher.Invoke(()=>Close()));}
 void AddMenu(ContextMenu m,string text,Action f){var item=new MenuItem{Header=text};item.Click+=(s,e)=>f();m.Items.Add(item);}
 void CreateBubble() {
  bubble=new Window {Title=I18n.T("Codex 剩余额度","Codex Remaining Usage"),Width=350,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowInTaskbar=Environment.GetCommandLineArgs().Contains("--preview"),ShowActivated=true};
  var outer=new StackPanel {Margin=new Thickness(12,12,12,14)};
  var border=new Border {Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FFFDFE"),(Color)ColorConverter.ConvertFromString("#F7F1FF"),90),BorderBrush=BrushOf("#E5D7F3"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(22,19,22,18),Effect=new DropShadowEffect{BlurRadius=16,ShadowDepth=3,Opacity=.17,Color=Colors.Purple}};
  outer.Children.Add(border);var stack=new StackPanel();border.Child=stack;
  var head=new DockPanel();var close=new Button {Content="×",FontSize=19,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=muted,Cursor=Cursors.Hand,Padding=new Thickness(6,0,0,0),ToolTip=I18n.T("收起","Close")};DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);close.Click+=(s,e)=>bubble.Hide();
  head.Children.Add(new TextBlock {Text=I18n.T("你的 Codex 余量","Your Codex Usage"),FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=ink});stack.Children.Add(head);
  plan=new TextBlock {Text=I18n.T("跟随 Codex 当前 Mini / 宠物","Following your Codex Mini / pet"),FontSize=11,Foreground=muted,Margin=new Thickness(0,5,0,16)};stack.Children.Add(plan);
  rows=new StackPanel();quotaScroll=new ScrollViewer{Content=rows,MaxHeight=420,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};stack.Children.Add(quotaScroll);
  var foot=new DockPanel {Margin=new Thickness(0,14,0,0)};refresh=new Button {Content=I18n.T("↻ 刷新","↻ Refresh"),Padding=new Thickness(10,5,10,5),Background=BrushOf("#EEE3FA"),Foreground=ink,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};DockPanel.SetDock(refresh,Dock.Right);foot.Children.Add(refresh);refresh.Click+=async(s,e)=>await Refresh();
  status=new TextBlock {Text=I18n.T("正在连接…","Connecting…"),FontSize=10,Foreground=muted,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=190};foot.Children.Add(status);stack.Children.Add(foot);
  stack.Children.Add(new TextBlock{Text=I18n.T("来自当前登录账号 · 每 60 秒更新","Signed-in account · Updates every 60 seconds"),FontSize=10,Foreground=muted,Margin=new Thickness(0,10,0,0)});
  var languageButtons=new StackPanel{Orientation=Orientation.Horizontal};
  var languageGroup=new Border{Child=languageButtons,CornerRadius=new CornerRadius(10),Background=BrushOf("#EEE5F7"),Padding=new Thickness(3),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left};
  foreach(var mode in new[]{"zh-CN","en"}){
   var choice=mode;bool selected=I18n.Resolve()==choice;
   var label=new TextBlock{Text=choice=="zh-CN"?"中文":"English",FontSize=11,FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal,Foreground=selected?ink:muted};
   var face=new Border{Child=label,Background=selected?Brushes.White:Brushes.Transparent,CornerRadius=new CornerRadius(7),Padding=new Thickness(12,5,12,5)};
   var button=new Button{Content=face,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Cursor=Cursors.Hand,ToolTip=choice=="zh-CN"?"切换为简体中文":"Switch to English"};
   System.Windows.Automation.AutomationProperties.SetName(button,choice=="zh-CN"?"简体中文":"English");
   button.Click+=(a,b)=>{if(I18n.Mode!=choice)I18n.Set(choice);};languageButtons.Children.Add(button);
  }
  stack.Children.Add(languageGroup);

  var tail=new System.Windows.Shapes.Polygon {Points=new PointCollection{new Point(0,0),new Point(22,0),new Point(16,13)},Fill=BrushOf("#F7F1FF"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,-1,50,0)};outer.Children.Add(tail);bubble.Content=outer;
  bubble.SizeChanged+=(s,e)=>PositionBubble();bubble.Deactivated+=(s,e)=>{if(!IsMouseOver)bubble.Hide();};bubble.KeyDown+=(s,e)=>{if(e.Key==Key.Escape)bubble.Hide();};
 }
 void Toggle(){if(bubble.IsVisible){bubble.Hide();return;}if(latest!=null)Render();PositionBubble();bubble.Show();bubble.Activate();if(updated==null || DateTime.Now-updated.Value>TimeSpan.FromSeconds(15)){var ignore=Refresh();}}
 Rect WorkArea(){var source=PresentationSource.FromVisual(bubble)??PresentationSource.FromVisual(this);var transform=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
  var c=native==null?null:native.Current;var point=c==null?new Point(Left,Top):new Point((c.Anchor.Left+c.Anchor.Right)/2,c.Anchor.Top);var r=Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y)).WorkingArea;return new Rect(transform.Transform(new Point(r.Left,r.Top)),transform.Transform(new Point(r.Right,r.Bottom)));}
 void PositionBubble(){if(bubble==null)return;var w=WorkArea();bubble.MaxHeight=Math.Max(160,w.Height);if(quotaScroll!=null)quotaScroll.MaxHeight=Math.Max(80,Math.Min(420,w.Height-220));var h=bubble.ActualHeight>1?bubble.ActualHeight:360;double petTop=Top-160;
  if(native!=null&&native.Current!=null){var src=PresentationSource.FromVisual(bubble)??PresentationSource.FromVisual(this);var m=src==null?Matrix.Identity:src.CompositionTarget.TransformFromDevice;petTop=m.Transform(new Point(native.Current.Anchor.Left,native.Current.Anchor.Top)).Y;}
  double y=petTop-h-6;if(y<w.Top)y=Top+Height+8;bubble.Left=Math.Max(w.Left,Math.Min(Left+Width/2-280,w.Right-350));bubble.Top=Math.Max(w.Top,Math.Min(y,w.Bottom-h));}
 async Task Refresh(){if(busy)return;busy=true;refresh.IsEnabled=false;status.Text=I18n.T("正在读取最新额度…","Fetching latest usage…");
  try{latest=await client.Read();updated=DateTime.Now;lastError=null;Render();}catch(Exception e){lastError=e.Message;status.Text=e.Message;if(latest==null){rows.Children.Clear();rows.Children.Add(new TextBlock{Text=I18n.T("暂时无法读取额度\n请先登录 Codex，再点刷新。","Unable to read usage.\nSign in to Codex, then refresh."),Foreground=ink,FontSize=14,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});}else{status.Text=I18n.T("刷新失败 · 以下为上次数据\n","Refresh failed · Showing previous data\n")+e.Message;}}finally{busy=false;refresh.IsEnabled=true;PositionBubble();}}
 void Render(){
  rows.Children.Clear();var data=QuotaModel.Parse(latest);var bucketCount=data.Select(item=>item.Bucket).Distinct().Count();string previous=null;int displayed=0;
  foreach(var row in data){if(bucketCount>1&&row.Bucket!=previous)rows.Children.Add(new TextBlock{Text=string.IsNullOrEmpty(row.Name)?row.Bucket:row.Name,Foreground=muted,FontSize=11,Margin=new Thickness(0,4,0,6)});previous=row.Bucket;if(AddQuota(row))displayed++;}
  if(displayed==0)rows.Children.Add(new TextBlock{Text=I18n.T("当前账号未返回额度数据","No quota data was returned for this account"),Foreground=ink,TextWrapping=TextWrapping.Wrap});
  string p=data.Count>0?data[0].Plan:"";plan.Text=(p.Length>0?p.ToUpperInvariant()+" · ":"")+I18n.T("跟随 Codex 当前 Mini / 宠物","Following your Codex Mini / pet");
  status.Text=lastError==null?I18n.T("更新于 ","Updated ")+(updated.HasValue?updated.Value.ToString("HH:mm:ss"):"—"):I18n.T("刷新失败 · 上次数据 ","Refresh failed · Last update ")+(updated.HasValue?updated.Value.ToString("HH:mm:ss"):"—");
 }
 bool AddQuota(QuotaRow row){
  double mins=row.Minutes;string label=mins==300?I18n.T("5 小时额度","5-hour remaining"):mins==10080?I18n.T("每周额度","Weekly remaining"):mins>0?(mins>=60?(mins/60).ToString("0.#")+I18n.T(" 小时额度","-hour remaining"):mins+I18n.T(" 分钟额度","-minute remaining")):row.Window=="primary"?I18n.T("主要额度","Primary remaining"):I18n.T("次要额度","Secondary remaining");
  var block=new StackPanel{Margin=new Thickness(0,0,0,16)};var line=new DockPanel();var pct=new TextBlock{Text=row.Remaining.HasValue?row.Remaining.Value.ToString("0.#")+"%":"—",FontSize=25,FontWeight=FontWeights.SemiBold,Foreground=row.Remaining.HasValue&&row.Remaining.Value<=20?BrushOf("#CB6381"):BrushOf("#8A63BC")};DockPanel.SetDock(pct,Dock.Right);line.Children.Add(pct);line.Children.Add(new TextBlock{Text=label,FontSize=13,Foreground=ink,VerticalAlignment=VerticalAlignment.Center});block.Children.Add(line);
  if(row.Remaining.HasValue){double remain=row.Remaining.Value;var bar=new Grid{Height=7,Margin=new Thickness(0,7,0,7)};bar.Children.Add(new Border{Background=BrushOf("#EDE5F5"),CornerRadius=new CornerRadius(4)});var fill=new Border{Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#C2A1E8"),(Color)ColorConverter.ConvertFromString("#E6A9CF"),0),CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Left};bar.SizeChanged+=(s,e)=>fill.Width=bar.ActualWidth*remain/100;bar.Children.Add(fill);block.Children.Add(bar);}
  else block.Children.Add(new TextBlock{Text=I18n.T("额度暂未提供","Usage unavailable"),Foreground=muted,FontSize=10,Margin=new Thickness(0,7,0,7)});
  string reset=I18n.T("重置时间暂未提供","Reset time unavailable");if(row.Reset.HasValue){var time=row.Reset.Value.LocalDateTime;var left=time-DateTime.Now;string d=left.TotalSeconds<=0?I18n.T("等待服务端更新","Waiting for server update"):left.TotalDays>=1?(int)left.TotalDays+I18n.T("天 ","d ")+left.Hours+I18n.T("小时","h"):left.TotalHours>=1?(int)left.TotalHours+I18n.T("小时 ","h ")+left.Minutes+I18n.T("分","m"):Math.Max(1,(int)left.TotalMinutes)+I18n.T("分钟","m");reset=I18n.T("重置 ","Resets ")+time.ToString("MM-dd HH:mm")+" · "+d;}
  block.Children.Add(new TextBlock{Text=reset,FontSize=10,Foreground=muted,TextWrapping=TextWrapping.Wrap});rows.Children.Add(block);return true;
 }
 public void ExportPreview(string path,Dictionary<string,object> data){latest=data;updated=DateTime.Now;Render();var content=(FrameworkElement)bubble.Content;content.Measure(new Size(350,double.PositiveInfinity));content.Arrange(new Rect(0,0,350,content.DesiredSize.Height));content.UpdateLayout();
  var bitmap=new RenderTargetBitmap(350,(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))encoder.Save(f);}
}
public static class Program {
 [STAThread]public static int Main(string[] args){
  int homeArg=Array.IndexOf(args,"--codex-home-base64");if(homeArg>=0){try{Environment.SetEnvironmentVariable("CODEX_HOME",EncodingHome(args,homeArg));}catch{ return 2; }}
  Diag.Prune();
  AppDomain.CurrentDomain.ProcessExit+=(s,e)=>Diag.Event("helper","process-exit");
  int langArg=Array.IndexOf(args,"--language");if(langArg>=0&&langArg+1<args.Length){I18n.Mode=args[langArg+1];I18n.Poll();}
  if(args.Contains("--render-preview")){try{Dictionary<string,object> data;using(var c=new QuotaClient())data=c.Read().GetAwaiter().GetResult();var previewApp=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var previewPet=new Pet();previewPet.ExportPreview(args[Array.IndexOf(args,"--render-preview")+1],data);previewPet.Close();return 0;}catch{return 1;}}
  if(args.Contains("--quit")){try{using(var stop=EventWaitHandle.OpenExisting("Local\\MiniQuotaCompanionStop_v2"))stop.Set();}catch{}return 0;}
  if(args.Contains("--diagnose")){var json=new JavaScriptSerializer().Serialize(NativeMini.Diagnostic());if(args.Length>1)File.WriteAllText(args[1],json);return 0;}
  if(args.Contains("--check")){try{using(var c=new QuotaClient()){var d=c.Read().GetAwaiter().GetResult();var b=Data.Obj(Data.Get(d,"rateLimits"));var report=new Dictionary<string,object>();foreach(var k in new[]{"primary","secondary","planType"})report[k]=Data.Get(b,k);Console.WriteLine(new JavaScriptSerializer().Serialize(report));if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(report));return 0;}}catch(Exception e){if(args.Length>1)File.WriteAllText(args[1],e.Message);return 1;}}
  bool created;using(var mutex=new Mutex(true,"Local\\MiniQuotaCompanion_v2",out created)){if(!created){Diag.Event("helper","duplicate-exit");return 0;}
   var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
   app.DispatcherUnhandledException+=(s,e)=>{Diag.Failure("ui","unhandled",e.Exception);MessageBox.Show(I18n.T("桌宠遇到问题：","Quota helper error: ")+e.Exception.Message,I18n.T("蕾米用量桌宠","Mini Quota Helper"));e.Handled=true;};
   try{var pet=new Pet();
    using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\MiniQuotaReady_v2"))using(var stop=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\MiniQuotaCompanionStop_v2")){
     pet.Ready=()=>ready.Set();
     var waiter=new Thread(()=>{stop.WaitOne();Diag.Event("helper","stop-requested");app.Dispatcher.BeginInvoke(new Action(()=>pet.Close()));});waiter.IsBackground=true;waiter.Start();
     app.Run(pet);return 0;}
   }catch(Exception e){Diag.Failure("helper","fatal",e);MessageBox.Show(e.Message,I18n.T("蕾米用量桌宠","Mini Quota Helper"));return 1;}
  }
 }
 static string EncodingHome(string[] args,int index){if(index+1>=args.Length)throw new ArgumentException();return Path.GetFullPath(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[index+1])));}
}
}
