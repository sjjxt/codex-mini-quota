using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace UsagePet {
public static class I18n {
 static readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexMiniQuota","preferences.json");
 public static string Mode="auto";
 static string language;
 static readonly TextFileCache configFile=new TextFileCache();
 public static Action Changed;
 static I18n(){try{var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));object x;if(d.TryGetValue("language",out x)&&new[]{"auto","zh-CN","en"}.ContainsValue(Convert.ToString(x)))Mode=Convert.ToString(x);}catch{}language=Resolve();}
 static bool ContainsValue(this string[] values,string value){return Array.IndexOf(values,value)>=0;}
 public static string Resolve(){
  if(Mode!="auto")return Mode;
  string locale=CultureInfo.CurrentUICulture.Name;
   try{var config=configFile.Read(Path.Combine(AppPaths.CodexHome,"config.toml"));
   var match=Regex.Match(config??"",@"(?m)^\s*localeOverride\s*=\s*[""']([^""']+)[""']");if(match.Success)locale=match.Groups[1].Value;
  }catch{}
  return locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en";
 }
 public static void Poll(){var next=Resolve();if(next!=language){language=next;if(Changed!=null)Changed();}}
 public static void Set(string mode){if(mode!="auto"&&mode!="zh-CN"&&mode!="en")return;Mode=mode;language=Resolve();try{Directory.CreateDirectory(Path.GetDirectoryName(settings));File.WriteAllText(settings,new JavaScriptSerializer().Serialize(new{language=Mode}));}catch{}if(Changed!=null)Changed();}
 public static string T(string zh,string en){return language=="zh-CN"?zh:en;}
 public static string TestLanguage(string locale){return locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en";}
}
}
