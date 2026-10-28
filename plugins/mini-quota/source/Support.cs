using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Web.Script.Serialization;

[assembly: AssemblyVersion("1.4.6.0")]
[assembly: AssemblyFileVersion("1.4.6.0")]
namespace UsagePet {
public static class AppInfo { public const string Version="1.4.6"; }
public static class AppPaths {
 public static string CodexHome {get {var value=Environment.GetEnvironmentVariable("CODEX_HOME");return Path.GetFullPath(string.IsNullOrWhiteSpace(value)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"):value);}}
 public static string LocalData {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexMiniQuota");}}
}
// Only event codes and exception types/error numbers are logged. Never log RPC or stderr bodies.
public static class Diag {
 static readonly object gate=new object();static readonly Dictionary<string,DateTime> recent=new Dictionary<string,DateTime>();
 public static void Event(string area,string code,string detail=null){try{lock(gate){
  var key=area+":"+code+":"+detail;DateTime last;if(recent.TryGetValue(key,out last)&&(DateTime.UtcNow-last).TotalSeconds<30)return;
  if(recent.Count>200)recent.Clear();recent[key]=DateTime.UtcNow;Directory.CreateDirectory(AppPaths.LocalData);
  var file=Path.Combine(AppPaths.LocalData,"diagnostics-"+Process.GetCurrentProcess().Id+".jsonl");
  if(File.Exists(file)&&new FileInfo(file).Length>512000){File.Copy(file,file+".1",true);File.WriteAllText(file,"");}
  File.AppendAllText(file,new JavaScriptSerializer().Serialize(new{utc=DateTime.UtcNow.ToString("o"),area=area,code=code,detail=detail})+Environment.NewLine);
 }}catch{} }
 public static void Failure(string area,string code,Exception error){Event(area,code,error.GetType().Name+":"+error.HResult.ToString(CultureInfo.InvariantCulture));}
 public static void Prune(){try{var files=new DirectoryInfo(AppPaths.LocalData).GetFiles("diagnostics-*.jsonl*");Array.Sort(files,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));for(int i=10;i<files.Length;i++)files[i].Delete();}catch{}}
}
public sealed class TextFileCache {
 string path,text;long length=-1;DateTime stamp;public int ReadCount{get;private set;}
 public string Read(string file){try{var info=new FileInfo(file);if(!info.Exists){Invalidate();return null;}
  if(file==path&&text!=null&&info.Length==length&&info.LastWriteTimeUtc==stamp)return text;
  var before=info.LastWriteTimeUtc;var size=info.Length;string next;
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))next=reader.ReadToEnd();
  ReadCount++;info.Refresh();if(info.Length!=size||info.LastWriteTimeUtc!=before){Invalidate();return null;}
  path=file;stamp=before;length=size;text=next;return next;
 }catch(Exception error){Invalidate();Diag.Failure("file","read-failed",error);return null;}}
 public void Invalidate(){path=null;text=null;length=-1;}
}
public sealed class QuotaRow {public string Bucket,Name,Plan,Window;public double? Remaining;public double Minutes;public DateTimeOffset? Reset;}
public static class QuotaModel {
 public static bool Number(object value,out double number){number=0;if(value==null||value is bool)return false;return double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&!double.IsNaN(number)&&!double.IsInfinity(number);}
 public static List<QuotaRow> Parse(Dictionary<string,object> result){
  var buckets=new List<KeyValuePair<string,Dictionary<string,object>>>();var map=Data.Obj(Data.Get(result,"rateLimitsByLimitId"));
  if(map!=null)foreach(var item in map){var bucket=Data.Obj(item.Value);if(bucket!=null)buckets.Add(new KeyValuePair<string,Dictionary<string,object>>(item.Key,bucket));}
  if(buckets.Count==0){var legacy=Data.Obj(Data.Get(result,"rateLimits"));if(legacy!=null)buckets.Add(new KeyValuePair<string,Dictionary<string,object>>("codex",legacy));}
  buckets.Sort((a,b)=>a.Key==b.Key?0:a.Key=="codex"?-1:b.Key=="codex"?1:string.CompareOrdinal(a.Key,b.Key));
  var rows=new List<QuotaRow>();foreach(var bucket in buckets)foreach(var key in new[]{"primary","secondary"}){
   var window=Data.Obj(Data.Get(bucket.Value,key));if(window==null)continue;double number;
   var row=new QuotaRow{Bucket=bucket.Key,Name=Data.Str(bucket.Value,"limitName"),Plan=Data.Str(bucket.Value,"planType"),Window=key};
   if(Number(Data.Get(window,"usedPercent"),out number))row.Remaining=Math.Max(0,Math.Min(100,100-number));
   if(Number(Data.Get(window,"windowDurationMins"),out number)&&number>0)row.Minutes=number;
   if(Number(Data.Get(window,"resetsAt"),out number)&&number>=-62135596800d&&number<=253402300799d)row.Reset=DateTimeOffset.FromUnixTimeSeconds((long)number);
   rows.Add(row);
  }return rows;
 }
}
}
