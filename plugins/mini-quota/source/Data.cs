using System;
using System.Collections.Generic;
namespace UsagePet {
public static class Data {
 public static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object>;}
 public static object Get(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)?result:null;}
 public static string Str(Dictionary<string,object> value,string key){return Convert.ToString(Get(value,key));}
}
}
