using System;
using System.Windows;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using UsagePet;
class RenderFixture {
 [STAThread]static int Main(string[] args){try{
  I18n.Mode=args[0];I18n.Poll();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(args[2]=="missing"?"{\"rateLimits\":{\"primary\":{\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":null,\"windowDurationMins\":10080}}}":args[2]=="empty"?"{}":"{\"rateLimits\":{\"planType\":\"example\",\"primary\":{\"usedPercent\":18,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":3,\"windowDurationMins\":10080}}}");
  var pet=new Pet();pet.ExportPreview(args[1],data);pet.Close();return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
