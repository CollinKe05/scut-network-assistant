using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using CampusNetwork;

static class OnboardingTests {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static int checks;
    static void Check(bool ok,string reason) {if(!ok)throw new Exception(reason);checks++;}
    static object Field(object value,string name) {return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
    static object Call(object value,string name,params object[] args) {return value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}
    [STAThread] static int Main(string[] args) {
        try {
            Application.EnableVisualStyles();
            string root=Path.GetFullPath(args[0]);
            var runtime=typeof(MainForm).Assembly.GetType("CampusNetwork.RuntimePack");
            var prepare=runtime.GetMethod("Prepare",BindingFlags.Public|BindingFlags.Static);
            prepare.Invoke(null,new object[]{new[]{"--data-dir",root}});
            Check(!File.Exists(Path.Combine(root,"settings.json")),"private settings were embedded");
            Check(!File.Exists(Path.Combine(root,".env")),"private environment was embedded");
            var defaults=Json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
            Check(defaults.rules.Count==1 && defaults.rules[0].kind=="suffix" && defaults.rules[0].value=="scut.edu.cn","default rules must be domain only");
            using(var form=new MainForm(root,true,true)) {
                var settings=(Settings)Field(form,"settings");
                Check(settings.ServerAddress=="" && settings.ServerUser=="" && settings.PythonPath=="" && !settings.SetupCompleted,"first run depends on author configuration");
                Check(((TabControl)Field(form,"tabs")).TabPages[0].Text=="首次配置向导","wizard is not first tab");
                typeof(MainForm).GetField("wizardStep",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,2);
                Call(form,"RenderWizard");
                var boxes=(Dictionary<string,TextBox>)Field(form,"inputs");
                boxes["CampusDomain"].Text="campus.example";boxes["VpnServerUrl"].Text="https://vpn.campus.example";boxes["ProbeUrl"].Text="https://video.campus.example/";
                Call(form,"SaveConnectionFields");Call(form,"SaveSettings");Call(form,"GenerateClashFiles");
                var rules=Json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
                Check(rules.rules.Count==1 && rules.rules[0].value=="campus.example","browser-only setup creates a server route");
                Check(File.ReadAllText(Path.Combine(root,"campus-profile.yaml")).Contains("- MATCH,DIRECT"),"new users need an account-free direct fallback");
                Check(File.ReadAllText(Path.Combine(root,"clash-scut.js")).Contains("vpn.campus.example"),"VPN gateway protection not generated");
                rules.rules.Add(new Rule {enabled=true,kind="domain",value="keep.example",route="direct"});
                File.WriteAllText(Path.Combine(root,"network-rules.json"),Json.Serialize(rules));
                boxes["ServerAddress"].Text="192.0.2.10";boxes["ServerUser"].Text="demo";
                Call(form,"SaveConnectionFields");
                rules=Json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
                Check(rules.rules.Any(r=>r.value=="192.0.2.10/32"),"explicit user server not routed");
                Check(rules.rules.Any(r=>r.value=="keep.example"),"existing custom rule deleted by wizard");
                string before=File.ReadAllText(Path.Combine(root,"network-rules.json"));
                boxes["VpnServerUrl"].Text="https://vpn.campus.example/?token=not-a-real-secret";
                bool rejected=false;try {Call(form,"SaveConnectionFields");}catch(TargetInvocationException){rejected=true;}
                Check(rejected && before==File.ReadAllText(Path.Combine(root,"network-rules.json")),"invalid URL wrote configuration");
                // Re-extraction must keep local rule edits and private settings.
                File.Delete(Path.Combine(root,".payload-version"));
                prepare.Invoke(null,new object[]{new[]{"--data-dir",root}});
                Check(before==File.ReadAllText(Path.Combine(root,"network-rules.json")),"bootstrap overwrote local rules");
                Check(File.Exists(Path.Combine(root,"settings.json")),"bootstrap removed local settings");
                if(args.Length>1) {
                    Directory.CreateDirectory(args[1]);
                    form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();
                    for(int step=0;step<6;step++) {
                        typeof(MainForm).GetField("wizardStep",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,step);
                        Call(form,"RenderWizard");form.Refresh();Application.DoEvents();
                        using(var image=new Bitmap(form.Width,form.Height)) {
                            form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                            image.Save(Path.Combine(args[1],"wizard-"+step+".png"));
                        }
                    }
                    form.Hide();
                }
            }
            Console.WriteLine("ONBOARDING_TESTS_OK: "+checks+" checks; no network or installers executed");return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
