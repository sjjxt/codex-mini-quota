using System;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class FakeCodex {
 static int Main(){var json=new JavaScriptSerializer();string line;var mode=Environment.GetEnvironmentVariable("MINI_QUOTA_TEST_MODE");
  while((line=Console.ReadLine())!=null){var msg=json.Deserialize<Dictionary<string,object>>(line);if(!msg.ContainsKey("id"))continue;var method=Convert.ToString(msg["method"]);
   if(method=="account/rateLimits/read"){
    if(mode=="timeout"){Thread.Sleep(5000);continue;}
    if(mode=="disconnect")return 0;
    if(mode=="error"){Console.Error.WriteLine("authentication error SECRET_DO_NOT_LOG");Console.WriteLine(json.Serialize(new{id=msg["id"],error=new{code=-32001,message="SECRET_DO_NOT_LOG"}}));continue;}
   }
   Console.WriteLine(json.Serialize(new{id=msg["id"],result=method=="initialize"?(object)new{}:new{rateLimits=new{primary=new{usedPercent=18,windowDurationMins=300}}}}));Console.Out.Flush();
  }return 0;
 }
}
