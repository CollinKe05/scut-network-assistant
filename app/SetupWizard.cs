using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CampusNetwork {
    public partial class MainForm {
        int wizardStep;
        bool targetVerified;
        readonly ListBox steps=new ListBox();
        readonly FlowLayoutPanel wizardBody=new FlowLayoutPanel();
        readonly Dictionary<string,TextBox> inputs=new Dictionary<string,TextBox>();
        readonly Dictionary<string,TextBox> toolInputs=new Dictionary<string,TextBox>();
        Label stepStatus;
        Button wizardNext;
        class Tool {
            public string Key, Name, Package, Url;
            public bool Required;
            public Tool(string key,string name,string package,string url,bool required) {Key=key;Name=name;Package=package;Url=url;Required=required;}
        }
        readonly Tool[] tools={
            new Tool("DockerPath","Docker Desktop","Docker.DockerDesktop","https://docs.docker.com/desktop/setup/install/windows-install/",true),
            new Tool("PythonPath","Python 3","Python.Python.3.13","https://www.python.org/downloads/windows/",true),
            new Tool("ClashPath","Clash Verge Rev","ClashVergeRev.ClashVergeRev","https://www.clashverge.dev/install.html",true),
            new Tool("VncViewerPath","RealVNC Viewer（可选）","","https://www.realvnc.com/en/connect/download/viewer/",false),
            new Tool("TermiusPath","Termius（可选）","Termius.Termius","https://termius.com/download/windows",false)
        };
        void BuildWizard(TabPage page) {
            var navigation=new Panel {Dock=DockStyle.Left,Width=145,Padding=new Padding(0,0,12,0)};
            steps.Dock=DockStyle.Fill;steps.BorderStyle=BorderStyle.None;steps.BackColor=BackColor;steps.ItemHeight=36;steps.Font=Font;
            steps.Items.AddRange(new object[]{"1  准备电脑","2  安装依赖","3  填写信息","4  配置 Clash","5  登录与验证","6  SSH 与完成"});
            steps.Enabled=false;navigation.Controls.Add(steps);
            var bottom=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft};
            wizardNext=ActionButton("下一步",true,async delegate {await NextWizard();},false);bottom.Controls.Add(wizardNext);
            bottom.Controls.Add(ActionButton("上一步",false,delegate {if(!busy && wizardStep>0){wizardStep--;RenderWizard();}},false));
            wizardBody.Dock=DockStyle.Fill;wizardBody.FlowDirection=FlowDirection.TopDown;wizardBody.WrapContents=false;wizardBody.AutoScroll=true;
            wizardBody.SizeChanged+=delegate {foreach(Control c in wizardBody.Controls)c.Width=Math.Max(280,wizardBody.ClientSize.Width-24);};
            page.Controls.Add(wizardBody);page.Controls.Add(bottom);page.Controls.Add(navigation);
            wizardStep=settings.SetupCompleted?5:Math.Min(settings.SetupStep,3);RenderWizard();
        }
        void WizardText(string text,int height,bool heading) {
            var l=TextLabel(text,heading?13F:9F,heading);l.Height=height;l.Width=Math.Max(280,wizardBody.ClientSize.Width-24);l.Margin=new Padding(0,0,0,12);wizardBody.Controls.Add(l);
        }
        FlowLayoutPanel WizardButtons() {
            var row=new FlowLayoutPanel {Height=52,Width=Math.Max(280,wizardBody.ClientSize.Width-24),Margin=new Padding(0)};wizardBody.Controls.Add(row);return row;
        }
        void Field(string key,string label,string value) {
            WizardText(label,24,false);
            var box=new TextBox {Text=value??"",Width=Math.Max(280,wizardBody.ClientSize.Width-24),Height=30,Margin=new Padding(0,0,0,14)};
            wizardBody.Controls.Add(box);inputs[key]=box;
        }
        void RenderWizard() {
            wizardBody.SuspendLayout();wizardBody.Controls.Clear();inputs.Clear();toolInputs.Clear();steps.SelectedIndex=wizardStep;
            wizardNext.Text=wizardStep==5?"完成配置":"下一步";
            switch(wizardStep) {
                case 0:
                    WizardText("从零开始配置校园连接",50,true);
                    WizardText("不需要安装 Git，也不需要手写配置。按左侧顺序完成准备、安装、填写信息、导入配置、登录与验证。学校账号和服务器密码只在对应登录窗口输入。",95,false);
                    WizardText("电脑要求：Windows 10/11 64 位，启用 BIOS 硬件虚拟化，并按 Docker 官方文档启用 WSL 2。安装过程可能需要管理员权限和重启电脑，请先保存其他工作。",95,false);
                    var start=WizardButtons();
                    start.Controls.Add(ActionButton("Docker 准备说明",false,delegate {OpenUrl("https://docs.docker.com/desktop/setup/install/windows-install/");},false));
                    start.Controls.Add(ActionButton("WSL 安装说明",false,delegate {OpenUrl("https://learn.microsoft.com/windows/wsl/install");},false));
                    WizardText("只需校园网页：Docker + Python + Clash。RealVNC 和 Termius 可选；没有 RealVNC 时使用浏览器登录容器。",90,false);
                    break;
                case 1:
                    WizardText("安装后重新检测，不会自动安装任何软件",50,true);
                    WizardText("先点官方下载，按安装器提示完成安装。也可点击 WinGet 安装，在打开的命令窗口中确认安装。Docker 安装完成后请启动一次并等待引擎就绪。",90,false);
                    foreach(var tool in tools) AddTool(tool);
                    var rescan=WizardButtons();rescan.Controls.Add(ActionButton("重新检测路径",false,delegate {SaveToolPaths(false);SaveSettings();RenderWizard();},false));
                    break;
                case 2:
                    WizardText("填写自己的连接信息",45,true);
                    WizardText("华工的公开 VPN 地址和学校域名可直接使用，也可以改成你所在学校的 EasyConnect 服务。服务器是可选项，不填就只检查校园网址。",85,false);
                    Field("VpnServerUrl","校园 VPN 地址（https）",settings.VpnServerUrl);
                    Field("CampusDomain","学校域名（只填写域名，不是网址）",settings.CampusDomain);
                    Field("ProbeUrl","用于验证的校内网址（https）",settings.ProbeUrl);
                    Field("ServerAddress","SSH 服务器地址（可选，留空不连接服务器）",settings.ServerAddress);
                    Field("ServerUser","SSH 用户名（填写服务器时需要）",settings.ServerUser);
                    Field("SshPort","SSH 端口",settings.SshPort.ToString());
                    Field("LocalProxyPort","电脑 Clash 混合代理端口",settings.LocalProxyPort.ToString());
                    Field("RemoteProxyPort","服务器反向代理端口（仅在需要时使用）",settings.RemoteProxyPort.ToString());
                    break;
                case 3:
                    WizardText("配置校园分流",45,true);
                    WizardText("没有外网订阅的新用户：打开 Clash 的“订阅”页，把生成的 campus-profile.yaml 拖进去，点击新卡片启用。它只提供校园 VPN 分流，其他流量直连，不需要购买外网订阅。",100,false);
                    var profile=WizardButtons();
                    profile.Controls.Add(ActionButton("生成配置文件",true,delegate {TryOpen(GenerateClashFiles);},false));
                    profile.Controls.Add(ActionButton("打开 Clash",false,delegate {TryOpen(delegate {Process.Start(settings.ClashPath);});},false));
                    profile.Controls.Add(ActionButton("打开文件夹",false,delegate {OpenUrl(root);},false));
                    WizardText("已有可用外网订阅：继续使用原订阅；在订阅页底部“全局扩展脚本 / Script”中粘贴本助手生成的脚本并保存。已有自定义脚本时应手动合并，不要覆盖自己的逻辑。",110,false);
                    var script=WizardButtons();script.Controls.Add(ActionButton("复制扩展脚本",false,delegate {TryOpen(delegate {GenerateClashFiles();Clipboard.SetText(File.ReadAllText(Path.Combine(root,"clash-scut.js")));MessageBox.Show(this,"已复制。请在 Clash 的全局扩展脚本中粘贴并保存；已有自定义脚本需自行合并。");});},false));
                    script.Controls.Add(ActionButton("官方导入教程",false,delegate {OpenUrl("https://www.clashverge.dev/guide/profile.html");},false));
                    WizardText("无论哪种方式，都需要启用 TUN（虚拟网卡），选择“规则”模式。若提示安装服务，按 Clash 提示确认。导入完成后点击下面的检查。",85,false);
                    var check=WizardButtons();check.Controls.Add(ActionButton("检查分流配置",true,async delegate {await CheckWizardClash();},false));
                    stepStatus=TextLabel("等待检查：分流组、规则模式、TUN、代理端口。",9F,false);stepStatus.Width=550;stepStatus.Height=70;wizardBody.Controls.Add(stepStatus);
                    break;
                case 4:
                    WizardText("启动、登录，再验证访问",45,true);
                    WizardText("首次启动会下载 Docker 镜像，耗时受网络影响。连接前会询问是否允许切换网络；不会因发现新镜像而自动替换运行中的 VPN。",90,false);
                    var connect=WizardButtons();
                    connect.Controls.Add(ActionButton("启动校园 VPN",true,async delegate {await Connect("-VpnOnly");},true));
                    connect.Controls.Add(ActionButton("校内直接访问",false,async delegate {await Connect("-CampusOnly");},true));
                    connect.Controls.Add(ActionButton("校园登录",false,delegate {OpenVnc();},false));
                    WizardText("点击“校园登录”：有 RealVNC 时连接 127.0.0.1:5901；否则浏览器打开 127.0.0.1:6080。VNC 连接密码在连接面板单独显示。进入容器后，填校园 VPN 地址，再自行输入学校账号、密码和验证码。",120,false);
                    WizardText("资源列表出现后仍需验证目标访问。点击下面按钮会检查网址响应，若填写了服务器也会检查 SSH。失败时查看运行记录，核对端口、学校授权及是否仍在初始化。",100,false);
                    var probe=WizardButtons();probe.Controls.Add(ActionButton("验证目标访问",true,async delegate {await VerifyTarget();},false));
                    stepStatus=TextLabel(targetVerified?"目标检查已通过。":"目标尚未验证。",9F,false);stepStatus.Width=550;stepStatus.Height=75;wizardBody.Controls.Add(stepStatus);
                    break;
                case 5:
                    WizardText("SSH 与服务器代理（可选）",45,true);
                    if(string.IsNullOrEmpty(settings.ServerAddress)) WizardText("你没有填写服务器，可以直接使用校园网页。如以后需要 SSH，可返回第 3 步填写自己的服务器地址和用户名。",90,false);
                    else {
                        WizardText("Termius → Hosts → New Host：地址 "+settings.ServerAddress+"，端口 "+settings.SshPort+"，用户名 "+settings.ServerUser+"。密码在 Termius 中自行填写。本助手不代建账号，也不会自动建立反向代理。",110,false);
                        var ssh=WizardButtons();ssh.Controls.Add(ActionButton("打开 Termius",false,delegate {OpenTermius();},false));
                        ssh.Controls.Add(ActionButton("复制 SSH 命令",false,delegate {Clipboard.SetText("ssh -p "+settings.SshPort+" "+settings.ServerUser+"@"+settings.ServerAddress);},false));
                        WizardText("只有服务器需要使用电脑上已有的外网代理时，才需要在 Termius 的 Port Forwarding 新建 Remote Rule：远端监听 127.0.0.1:"+settings.RemoteProxyPort+"，本机目标 127.0.0.1:"+settings.LocalProxyPort+"。保持转发与 Clash 在线；本助手不提供外网代理节点。",130,false);
                        var env=new TextBox {Multiline=true,ReadOnly=true,Width=550,Height=83,Text="export http_proxy=http://127.0.0.1:"+settings.RemoteProxyPort+"\r\nexport https_proxy=http://127.0.0.1:"+settings.RemoteProxyPort+"\r\nexport all_proxy=socks5h://127.0.0.1:"+settings.RemoteProxyPort};wizardBody.Controls.Add(env);
                    }
                    WizardText("点击“下一步”保存完成。此后从连接面板选择自动、校内或 VPN；规则页面默认只有学校域名，没有任何人的私有 IP、用户名或本机路径。",100,false);
                    break;
            }
            wizardBody.ResumeLayout();
        }
        async Task NextWizard() {
            if(busy) return;
            try {
                if(wizardStep==0 && !Environment.Is64BitOperatingSystem) throw new Exception("此版本需要 Windows 64 位系统。");
                if(wizardStep==1) SaveToolPaths(true);
                if(wizardStep==2) {SaveConnectionFields();SaveSettings();GenerateClashFiles();}
                if(wizardStep==3 && !await CheckWizardClash()) return;
                if(wizardStep==4 && !targetVerified && MessageBox.Show(this,"尚未验证访问成功。可以先阅读服务器说明，之后返回此步完成验证。继续？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes) return;
                if(wizardStep==5) {settings.SetupCompleted=true;SaveSettings();tabs.SelectedIndex=1;AppendLog(targetVerified?"配置完成，目标访问检查通过。":"配置已保存；目标访问仍待验证，请回到向导第 5 步检查。");return;}
                wizardStep++;settings.SetupStep=wizardStep;SaveSettings();RenderWizard();
            } catch(Exception ex) {MessageBox.Show(this,ex.Message,"请检查配置",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        string GetTool(string key) {return Convert.ToString(typeof(Settings).GetProperty(key).GetValue(settings,null));}
        void SetTool(string key,string path) {typeof(Settings).GetProperty(key).SetValue(settings,path,null);}
        void AddTool(Tool tool) {
            string path=GetTool(tool.Key);if(string.IsNullOrEmpty(path) || !File.Exists(path)) path=DetectTool(tool.Key);
            WizardText(tool.Name+(tool.Required?" · 必需":"")+(File.Exists(path)?" ✓ 已找到":" · 未找到"),28,true);
            var row=WizardButtons();
            row.Controls.Add(ActionButton("官方下载",false,delegate {OpenUrl(tool.Url);},false));
            if(!string.IsNullOrEmpty(tool.Package)) row.Controls.Add(ActionButton("WinGet 安装",false,delegate {InstallTool(tool);},false));
            row.Controls.Add(ActionButton("选择已安装文件",false,delegate {using(var dialog=new OpenFileDialog {Filter="程序 (*.exe)|*.exe",Title="选择 "+tool.Name}) if(dialog.ShowDialog(this)==DialogResult.OK) toolInputs[tool.Key].Text=dialog.FileName;},false));
            var box=new TextBox {Text=path,Width=550,Margin=new Padding(0,0,0,16)};wizardBody.Controls.Add(box);toolInputs[tool.Key]=box;
        }
        string DetectTool(string key) {
            if(testMode) return "";
            string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),pf=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidates=new List<string>();
            if(key=="DockerPath") candidates.AddRange(new[]{Path.Combine(local,@"Programs\DockerDesktop\resources\bin\docker.exe"),Path.Combine(pf,@"Docker\Docker\resources\bin\docker.exe")});
            if(key=="ClashPath") candidates.AddRange(new[]{Path.Combine(pf,@"Clash Verge\clash-verge.exe"),Path.Combine(pf,@"Clash Verge Rev\clash-verge.exe"),Path.Combine(local,@"Programs\Clash Verge\clash-verge.exe")});
            if(key=="TermiusPath") candidates.Add(Path.Combine(local,@"Programs\Termius\Termius.exe"));
            if(key=="VncViewerPath") candidates.AddRange(new[]{Path.Combine(pf,@"RealVNC\VNC Viewer\vncviewer.exe"),Path.Combine(pf,@"RealVNC\VNC Connect\vncviewer.exe")});
            if(key=="PythonPath") {
                foreach(string dir in new[]{"miniforge3","miniconda3","anaconda3"}) candidates.Add(Path.Combine(user,dir,"python.exe"));
                string parent=Path.Combine(local,@"Programs\Python");
                if(Directory.Exists(parent)) foreach(string dir in Directory.GetDirectories(parent)) candidates.Add(Path.Combine(dir,"python.exe"));
                foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) if(!dir.Contains("WindowsApps") && Directory.Exists(dir)) candidates.Add(Path.Combine(dir,"python.exe"));
                foreach(RegistryKey hive in new[]{Registry.CurrentUser,Registry.LocalMachine}) {
                    try {using(var python=hive.OpenSubKey(@"Software\Python\PythonCore")) if(python!=null) foreach(string version in python.GetSubKeyNames()) using(var install=python.OpenSubKey(version+@"\InstallPath")) if(install!=null) {
                        string exe=Convert.ToString(install.GetValue("ExecutablePath"));if(!string.IsNullOrEmpty(exe))candidates.Add(exe);
                        string dir=Convert.ToString(install.GetValue(""));if(!string.IsNullOrEmpty(dir))candidates.Add(Path.Combine(dir,"python.exe"));
                    }} catch {}
                }
            }
            if(key=="ClashPath") {
                foreach(var process in Process.GetProcessesByName("clash-verge")) try {candidates.Add(process.MainModule.FileName);} catch {}
            }
            return candidates.FirstOrDefault(File.Exists)??"";
        }
        void SaveToolPaths(bool required) {
            foreach(var tool in tools) {
                string path=toolInputs.ContainsKey(tool.Key)?toolInputs[tool.Key].Text.Trim():GetTool(tool.Key);
                if(required && tool.Required && !File.Exists(path)) throw new Exception("未找到 "+tool.Name+"。请先安装，再检测或选择程序文件。");
                SetTool(tool.Key,path);
            }
        }
        void InstallTool(Tool tool) {
            if(testMode) return;
            if(MessageBox.Show(this,"将打开 WinGet 安装 "+tool.Name+"。安装器可能要求管理员权限；请在安装窗口确认条款，Docker/WSL 可能要求重启。现在安装？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            TryOpen(delegate {Process.Start(new ProcessStartInfo("winget.exe","install --exact --id "+tool.Package+" --source winget --interactive") {UseShellExecute=true});});
        }
        void SaveConnectionFields() {
            Uri vpn,probe;
            if(!Uri.TryCreate(inputs["VpnServerUrl"].Text.Trim(),UriKind.Absolute,out vpn) || vpn.Scheme!="https" || !string.IsNullOrEmpty(vpn.UserInfo) || !string.IsNullOrEmpty(vpn.Query)) throw new Exception("VPN 地址需为不含账号或令牌的 https 网址。");
            if(!Uri.TryCreate(inputs["ProbeUrl"].Text.Trim(),UriKind.Absolute,out probe) || (probe.Scheme!="https" && probe.Scheme!="http") || !string.IsNullOrEmpty(probe.UserInfo) || !string.IsNullOrEmpty(probe.Query)) throw new Exception("测试网址需为不含密码或令牌的 http(s) 网址。");
            string domain=inputs["CampusDomain"].Text.Trim().TrimEnd('.').ToLowerInvariant();
            if(Uri.CheckHostName(domain)!=UriHostNameType.Dns || domain.Contains(",") || domain.Contains("/")) throw new Exception("学校域名格式不正确，例如 scut.edu.cn。");
            string server=inputs["ServerAddress"].Text.Trim(),user=inputs["ServerUser"].Text.Trim();
            if(server!="" && (Uri.CheckHostName(server)==UriHostNameType.Unknown || user=="" || !System.Text.RegularExpressions.Regex.IsMatch(user,@"^[A-Za-z0-9_][A-Za-z0-9_.-]*$"))) throw new Exception("请填写有效的服务器主机名或 IPv4 和 SSH 用户名；不填写任何密码。");
            System.Net.IPAddress address;
            if(System.Net.IPAddress.TryParse(server,out address) && address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) throw new Exception("当前版本请使用服务器 IPv4 地址或域名。");
            int sshPort=ReadPort("SshPort"),localPort=ReadPort("LocalProxyPort"),remotePort=ReadPort("RemoteProxyPort");
            if(new[]{1080,8888,5901,6080,54530}.Contains(localPort)) throw new Exception("Clash 端口不能与校园 VPN 的本机端口冲突。");
            string oldDomain=settings.CampusDomain,oldServer=settings.ServerAddress;
            settings.VpnServerUrl=vpn.GetLeftPart(UriPartial.Authority);settings.CampusDomain=domain;settings.ProbeUrl=probe.AbsoluteUri;settings.ServerAddress=server;settings.ServerUser=user;
            settings.SshPort=sshPort;settings.LocalProxyPort=localPort;settings.RemoteProxyPort=remotePort;
            var rules=new List<Rule>{new Rule {enabled=true,kind="suffix",value=domain,route="campus"}};
            if(server!="" && !server.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase) && server!=domain) {
                System.Net.IPAddress ip;
                rules.Insert(0,new Rule {enabled=true,kind=System.Net.IPAddress.TryParse(server,out ip)?"cidr":"domain",value=ip!=null?server+"/32":server,route="campus"});
            }
            var previous=json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
            foreach(var existing in previous.rules) {
                bool managed=existing.route=="campus" && ((existing.kind=="suffix" && existing.value==oldDomain) || (!string.IsNullOrEmpty(oldServer) && (existing.value==oldServer || existing.value==oldServer+"/32")));
                if(!managed && !rules.Any(r=>r.kind==existing.kind && r.value==existing.value)) rules.Add(existing);
            }
            File.WriteAllText(Path.Combine(root,"network-rules.json"),json.Serialize(new RuleFile {version=1,rules=rules}),new UTF8Encoding(false));LoadRules();
        }
        int ReadPort(string field) {int value;if(!int.TryParse(inputs[field].Text,out value)||value<1||value>65535)throw new Exception("端口必须为 1–65535 的整数。");return value;}
        void SaveSettings() {File.WriteAllText(Path.Combine(root,"settings.json"),json.Serialize(settings),new UTF8Encoding(false));}
        void GenerateClashFiles() {
            var ruleFile=json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
            var rules=new List<string>{"DOMAIN,"+new Uri(settings.VpnServerUrl).DnsSafeHost+",DIRECT"};
            foreach(var rule in ruleFile.rules) if(rule.enabled) rules.Add((rule.kind=="cidr"?"IP-CIDR":rule.kind=="domain"?"DOMAIN":"DOMAIN-SUFFIX")+","+rule.value+","+(rule.route=="direct"?"DIRECT":rule.route=="vpn"?"Campus-Docker":"Campus-Network")+(rule.kind=="cidr"?",no-resolve":""));
            string yaml="mixed-port: "+settings.LocalProxyPort+"\nallow-lan: false\nmode: rule\nlog-level: info\ndns:\n  enable: true\n  enhanced-mode: fake-ip\n  nameserver:\n  - https://dns.alidns.com/dns-query\ntun:\n  enable: true\n  stack: gvisor\n  auto-route: true\n  auto-detect-interface: true\n  dns-hijack:\n  - any:53\nproxies:\n- name: Campus-Docker\n  type: socks5\n  server: 127.0.0.1\n  port: 1080\n  udp: false\nproxy-groups:\n- name: Campus-Network\n  type: select\n  proxies:\n  - DIRECT\n  - Campus-Docker\nrules:\n"+string.Join("\n",rules.Select(r=>"- "+r))+"\n- MATCH,DIRECT\n";
            File.WriteAllText(Path.Combine(root,"campus-profile.yaml"),yaml,new UTF8Encoding(false));
            string script="// CampusNetworkAssistant managed extension\nfunction main(config, profileName) {\n const campusName = \"Campus-Network\";\n const dockerName = \"Campus-Docker\";\n config.proxies=(config.proxies||[]).filter(p=>p.name!==campusName && p.name!==dockerName);\n config.proxies.push({name:dockerName,type:\"socks5\",server:\"127.0.0.1\",port:1080,udp:false});\n config[\"proxy-groups\"]=(config[\"proxy-groups\"]||[]).filter(p=>p.name!==campusName);\n config[\"proxy-groups\"].push({name:campusName,type:\"select\",proxies:[\"DIRECT\",dockerName]});\n const campusRules="+json.Serialize(rules)+";\n config.rules=campusRules.concat((config.rules||[]).filter(r=>!campusRules.includes(r)));return config;\n}\n";
            File.WriteAllText(Path.Combine(root,"clash-scut.js"),script,new UTF8Encoding(false));
            AppendLog("已在助手数据目录生成校园配置和扩展脚本；尚未修改 Clash 或当前网络。");
        }
        async Task<bool> CheckWizardClash() {
            if(busy || testMode) return false;
            busy=true;
            try {
                var r=await Run(settings.PythonPath,Q(Path.Combine(root,"network-manager.py"))+" status",false);
                var state=json.Deserialize<Dictionary<string,object>>(r.Output);
                bool ok=(bool)state["clash"] && state.ContainsKey("tun") && state["tun"]!=null && (bool)state["tun"] && Convert.ToString(state["mode"])=="rule" && (bool)state["proxy_port"];
                string message=ok?"分流组、规则模式、TUN 与 Clash 端口检查通过。":"尚未就绪：请启用导入的校园配置 / 全局脚本、规则模式和 TUN，并核对 Clash 端口。";
                if(stepStatus!=null)stepStatus.Text=message;AppendLog(message);return ok;
            } catch(Exception ex) {MessageBox.Show(this,"检查未完成："+ex.Message);return false;}
            finally {busy=false;}
        }
        async Task VerifyTarget() {
            if(busy || testMode) return;
            await Operation("只读检查校园目标访问…",async delegate {
                var r=await Run(settings.PythonPath,Q(Path.Combine(root,"network-manager.py"))+" probe",false);
                var state=json.Deserialize<Dictionary<string,object>>(r.Output);
                targetVerified=state.ContainsKey("reachable") && (bool)state["reachable"];
                string message=Convert.ToString(state["message"]);if(stepStatus!=null)stepStatus.Text=message;AppendLog(message);
            },false);
        }
        void OpenUrl(string url) {TryOpen(delegate {if(!testMode)Process.Start(new ProcessStartInfo(url) {UseShellExecute=true});});}
    }
}
