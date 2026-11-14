using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("华工网络助手")]
[assembly: System.Reflection.AssemblyProduct("华工网络助手")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace CampusNetwork {
    public class Settings {
        public string PythonPath { get; set; }
        public string TermiusPath { get; set; }
        public string ServerAddress { get; set; }
        public int SshPort { get; set; }
    }
    public class Rule {
        public bool enabled { get; set; }
        public string kind { get; set; }
        public string value { get; set; }
        public string route { get; set; }
    }
    public class RuleFile {
        public int version { get; set; }
        public List<Rule> rules { get; set; }
    }
    class Result { public int Exit; public string Output; }

    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [STAThread] static void Main(string[] args) {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (!File.Exists(Path.Combine(root, "settings.json"))) {
                MessageBox.Show("请从完整的程序文件夹运行华工网络助手。桌面可以放快捷方式。", "华工网络助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var instance = new System.Threading.Mutex(false, "Local\\SCUT-NetworkAssistant");
            bool owns = args.Contains("--smoke-test") || instance.WaitOne(0);
            if (!owns) {
                var existing=Process.GetProcessesByName("SCUT-Network").FirstOrDefault(p=>p.Id!=Process.GetCurrentProcess().Id && p.MainWindowHandle!=IntPtr.Zero);
                if(existing!=null) {ShowWindow(existing.MainWindowHandle,9); SetForegroundWindow(existing.MainWindowHandle);}
                instance.Dispose(); return;
            }
            try {
                MainForm form = new MainForm(root, args.Contains("--smoke-test"));
                if (args.Contains("--smoke-test")) {
                    form.Shown += async delegate {
                        await form.RefreshStatus();
                        if (args.Length > 1) form.SaveSmokeReport(args[1]);
                        form.Close();
                    };
                }
                Application.Run(form);
            } catch (Exception ex) {
                MessageBox.Show("程序未能启动：" + ex.Message, "华工网络助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } finally {if(!args.Contains("--smoke-test")) instance.ReleaseMutex(); instance.Dispose();}
        }
    }

    public class MainForm : Form {
        readonly string root;
        readonly Settings settings;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly Color accent = Color.FromArgb(0, 103, 192);
        readonly Color ink = Color.FromArgb(28, 33, 43);
        readonly List<Button> mutationButtons = new List<Button>();
        readonly RichTextBox log = new RichTextBox();
        readonly DataGridView grid = new DataGridView();
        readonly Label routeValue = new Label(), clashValue = new Label(), vpnValue = new Label();
        readonly Label taskLabel = new Label(), passwordLabel = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly TabControl tabs = new TabControl();
        readonly Button cancel = new Button();
        bool busy, cancelled, operationActive;
        bool dirty, loadingRules;
        Process currentProcess;
        string lastState = "";
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        public MainForm(string path, bool smokeTest) {
            root = path;
            settings = json.Deserialize<Settings>(File.ReadAllText(Path.Combine(root, "settings.json")));
            if (!File.Exists(settings.PythonPath)) throw new Exception("找不到配置中的 Python，请检查 settings.json。");
            Text = "华工网络助手";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1120, 790);
            MinimumSize = new Size(1020, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 249, 252);
            ForeColor = ink;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BuildUi();
            LoadRules();
            Shown += async delegate { int round = 2; try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch {} if(!smokeTest) await RefreshStatus(); };
            timer.Interval = 12000;
            timer.Tick += async delegate { if (!busy) await RefreshStatus(); };
            if(!smokeTest) timer.Start();
            FormClosing += OnClosing;
        }

        Label TextLabel(string text, float size, bool bold) {
            return new Label { Text = text, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), AutoSize = false, ForeColor = ink };
        }
        Button ActionButton(string title, bool primary, EventHandler action, bool mutation) {
            var b = new Button { Text = title, Height = 40, Width = 140, Margin = new Padding(0, 0, 12, 10), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : ink };
            b.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(215, 221, 230);
            b.Click += action;
            if (mutation) mutationButtons.Add(b);
            return b;
        }
        TabPage Page(string title) { return new TabPage(title) { BackColor = BackColor, Padding = new Padding(22) }; }

        void BuildUi() {
            var side = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Color.FromArgb(238, 242, 249), Padding = new Padding(22, 28, 18, 20) };
            var brand = TextLabel("华工\n网络助手", 14F, true); brand.Dock = DockStyle.Top; brand.Height = 106; brand.ForeColor = accent;
            var description = TextLabel("校园连接 · 网络分流\n\nA800\n10.48.12.129\n\n本机代理\n127.0.0.1:7897", 8.5F, false); description.Dock = DockStyle.Top; description.Height = 210;
            var foot = TextLabel("EasyConnect 运行在 Docker\n账号验证在 RealVNC 完成\n\n关闭本窗口保留网络连接", 8F, false); foot.Dock = DockStyle.Bottom; foot.Height = 160; foot.ForeColor = Color.DimGray;
            side.Controls.Add(description); side.Controls.Add(brand); side.Controls.Add(foot);
            var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20) };
            var heading = TextLabel("选择你的网络路径", 22F, true); heading.Dock = DockStyle.Top; heading.Height = 50;
            var sub = TextLabel("校内直连，校外连接校园 VPN；网段和域名按你的规则分流。", 10F, false); sub.Dock = DockStyle.Top; sub.Height = 40; sub.ForeColor = Color.DimGray;
            tabs.Dock = DockStyle.Fill; tabs.Font = Font; tabs.Padding = new Point(16, 9);
            TabPage home = Page("连接面板"), rules = Page("网段与域名"), logs = Page("运行记录");
            tabs.TabPages.AddRange(new[] {home, rules, logs});
            BuildHome(home); BuildRules(rules);
            log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BackColor = Color.White; log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9F); logs.Controls.Add(log);
            main.Controls.Add(tabs); main.Controls.Add(sub); main.Controls.Add(heading);
            Controls.Add(main); Controls.Add(side);
        }

        void BuildHome(TabPage page) {
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, Height = 520, ColumnCount = 1, RowCount = 7 };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] {110, 32, 65, 85, 108, 70, 40}) stack.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            for (int i=0; i<3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            cards.Controls.Add(StatusCard("当前校园路径", routeValue), 0, 0);
            cards.Controls.Add(StatusCard("Clash 分流", clashValue), 1, 0);
            cards.Controls.Add(StatusCard("Docker VPN", vpnValue), 2, 0);
            stack.Controls.Add(cards, 0, 0);
            var choose = TextLabel("快捷连接", 11F, true); choose.Dock = DockStyle.Fill; stack.Controls.Add(choose, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            actions.Controls.Add(ActionButton("自动选择网络", true, async delegate { await Connect(""); }, true));
            actions.Controls.Add(ActionButton("校内直连", false, async delegate { await Connect("-CampusOnly"); }, true));
            actions.Controls.Add(ActionButton("校外校园 VPN", false, async delegate { await Connect("-VpnOnly"); }, true));
            stack.Controls.Add(actions, 0, 2);
            var info = TextLabel("自动模式会先验证 A800 的 SSH，能直连就跳过 Docker。\n“跟随快捷模式”的规则随这里切换；固定出口保持你的选择。\n换网络后重新点击连接；服务器代理请在 Termius 启动原有 Remote Rule。", 9F, false); info.Dock = DockStyle.Fill; info.ForeColor = Color.DimGray; stack.Controls.Add(info, 0, 3);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill };
            tools.Controls.Add(ActionButton("打开 Termius", false, delegate { OpenTermius(); }, false));
            tools.Controls.Add(ActionButton("RealVNC 登录", false, delegate { OpenVnc(); }, false));
            tools.Controls.Add(ActionButton("停止校园 VPN", false, async delegate { await StopVpn(); }, true));
            tools.Controls.Add(ActionButton("刷新状态", false, async delegate { await RefreshStatus(); }, true));
            tools.Controls.Add(ActionButton("VNC 连接密码", false, delegate { ShowPassword(); }, false));
            stack.Controls.Add(tools, 0, 4);
            passwordLabel.Text = "RealVNC 地址 127.0.0.1:5901\n校园账号与验证码请在 RealVNC 内填写。"; passwordLabel.Dock = DockStyle.Fill;
            stack.Controls.Add(passwordLabel, 0, 5);
            var task = new Panel { Dock = DockStyle.Fill };
            taskLabel.Text = "就绪"; taskLabel.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Marquee; progress.Dock = DockStyle.Bottom; progress.Height = 4; progress.Visible = false;
            cancel.Text = "取消等待"; cancel.Dock = DockStyle.Right; cancel.Width = 100; cancel.Visible = false;
            cancel.Click += delegate { cancelled = true; if (currentProcess != null) { try { currentProcess.Kill(); } catch {} } };
            task.Controls.Add(taskLabel); task.Controls.Add(cancel); task.Controls.Add(progress);
            stack.Controls.Add(task, 0, 6); page.Controls.Add(stack);
        }

        Panel StatusCard(string title, Label value) {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0,0,12,15), Padding = new Padding(14) };
            var caption = TextLabel(title, 9F, false); caption.Dock = DockStyle.Top; caption.Height = 25; caption.ForeColor = Color.DimGray;
            value.Text = "读取中…"; value.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold); value.Dock = DockStyle.Fill;
            panel.Controls.Add(value); panel.Controls.Add(caption); return panel;
        }

        void BuildRules(TabPage page) {
            var tip = TextLabel("支持 IP / CIDR 和 www 域名，也可粘贴完整网址。规则从上到下匹配。\n“域名及子域名”例如 scut.edu.cn，也匹配 www.scut.edu.cn；网址只取域名。", 9F, false);
            tip.Dock = DockStyle.Top; tip.Height = 65;
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false; grid.RowTemplate.Height = 37; grid.ColumnHeadersHeight = 36; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234,239,247);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(221,236,252); grid.DefaultCellStyle.SelectionForeColor = ink;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="enabled", HeaderText="启用", FillWeight=12});
            var kinds = new DataGridViewComboBoxColumn { Name="kind", HeaderText="类型", FillWeight=29, FlatStyle=FlatStyle.Flat };
            kinds.Items.AddRange("IP / 网段", "单个域名", "域名及子域名"); grid.Columns.Add(kinds);
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="value", HeaderText="IP 网段 / 域名 / 网址", FillWeight=53});
            var routes = new DataGridViewComboBoxColumn {Name="route", HeaderText="网络出口", FillWeight=34, FlatStyle=FlatStyle.Flat};
            routes.Items.AddRange("跟随快捷模式", "直连", "校园 VPN"); grid.Columns.Add(routes);
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            grid.CellValueChanged += delegate { if (!loadingRules) dirty = true; };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 105 };
            controls.Controls.Add(ActionButton("添加规则", false, delegate { grid.Rows.Add(true, "单个域名", "", "跟随快捷模式"); dirty=true; grid.CurrentCell=grid.Rows[grid.Rows.Count-1].Cells[2]; grid.BeginEdit(true); }, true));
            controls.Controls.Add(ActionButton("删除选中", false, delegate { if (grid.CurrentRow != null) { grid.Rows.Remove(grid.CurrentRow); dirty=true; } }, true));
            controls.Controls.Add(ActionButton("上移", false, delegate { MoveRule(-1); }, true));
            controls.Controls.Add(ActionButton("下移", false, delegate { MoveRule(1); }, true));
            controls.Controls.Add(ActionButton("保存并应用", true, async delegate { await ApplyRules(); }, true));
            controls.Controls.Add(ActionButton("放弃修改", false, delegate { LoadRules(); }, true));
            page.Controls.Add(grid); page.Controls.Add(controls); page.Controls.Add(tip);
        }

        void MoveRule(int delta) {
            grid.EndEdit(); if (grid.CurrentRow == null) return;
            int i=grid.CurrentRow.Index, target=i+delta; if (target<0 || target>=grid.Rows.Count) return;
            object[] values=grid.Rows[i].Cells.Cast<DataGridViewCell>().Select(c=>c.Value).ToArray();
            grid.Rows.RemoveAt(i); grid.Rows.Insert(target, values); grid.CurrentCell=grid.Rows[target].Cells[2]; dirty=true;
        }
        void LoadRules() {
            loadingRules=true;
            try {
                var file=json.Deserialize<RuleFile>(File.ReadAllText(Path.Combine(root,"network-rules.json")));
                grid.Rows.Clear();
                foreach(var r in file.rules) grid.Rows.Add(r.enabled, r.kind=="cidr"?"IP / 网段":r.kind=="domain"?"单个域名":"域名及子域名", r.value,
                    r.route=="direct"?"直连":r.route=="vpn"?"校园 VPN":"跟随快捷模式");
                dirty=false;
            } finally { loadingRules=false; }
        }
        RuleFile ReadGrid() {
            grid.EndEdit();
            return new RuleFile {version=1, rules=grid.Rows.Cast<DataGridViewRow>().Select(r=>new Rule {
                enabled=Convert.ToBoolean(r.Cells[0].Value), kind=Convert.ToString(r.Cells[1].Value)=="IP / 网段"?"cidr":Convert.ToString(r.Cells[1].Value)=="单个域名"?"domain":"suffix",
                value=Convert.ToString(r.Cells[2].Value), route=Convert.ToString(r.Cells[3].Value)=="直连"?"direct":Convert.ToString(r.Cells[3].Value)=="校园 VPN"?"vpn":"campus"
            }).ToList()};
        }

        string Q(string value) { return "\""+value.Replace("\"","\\\"")+"\""; }
        string Ps(string value) { return "'"+value.Replace("'","''")+"'"; }
        string PowershellArgs(string file, string flags) {
            string command="[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); $OutputEncoding=[Console]::OutputEncoding; & "+Ps(Path.Combine(root,file))+" "+flags;
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "+Q(command);
        }

        async Task<Result> Run(string executable, string arguments, bool showLog) {
            var psi=new ProcessStartInfo(executable,arguments) {WorkingDirectory=root, UseShellExecute=false, CreateNoWindow=true,
                RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8};
            var output=new StringBuilder();
            using(var p=new Process {StartInfo=psi}) {
                currentProcess=p;
                try {
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if(e.Data!=null) { lock(output) output.AppendLine(e.Data); if(showLog) AppendLog(e.Data); } };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if(e.Data!=null && showLog) AppendLog(e.Data); };
                    p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    await Task.Run(()=>p.WaitForExit());
                    return new Result {Exit=p.ExitCode, Output=output.ToString()};
                } finally {currentProcess=null;}
            }
        }
        void AppendLog(string value) {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try {BeginInvoke(new Action<string>(AppendLog), value);} catch {} return; }
            if(value.IndexOf("VNC password:",StringComparison.OrdinalIgnoreCase)>=0 || value.Contains("VNC 密码："))
                value="RealVNC 连接密码可在连接面板单独显示。";
            if(log.TextLength>140000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+value+Environment.NewLine); log.ScrollToCaret();
        }
        async Task Operation(string title, Func<Task> action, bool cancellable = true) {
            if(busy) return;
            busy=true; operationActive=true; cancelled=false; progress.Visible=true; cancel.Visible=cancellable; taskLabel.Text=title;
            foreach(var b in mutationButtons) b.Enabled=false;
            AppendLog(title);
            try { await action(); taskLabel.Text=cancelled?"已取消等待；已启动的服务保持运行。":"操作完成"; }
            catch(Exception ex) {taskLabel.Text="未完成："+ex.Message; AppendLog(taskLabel.Text);}
            finally {busy=false; operationActive=false; progress.Visible=false; cancel.Visible=false; foreach(var b in mutationButtons) b.Enabled=true;}
            await RefreshStatus();
        }
        async Task Connect(string flag) {
            if (dirty && MessageBox.Show(this,"规则有未保存修改。继续连接会使用上次保存的规则。", "继续连接",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK) return;
            await Operation("正在连接，请查看运行记录；需要认证时会打开 RealVNC。", async delegate {
                Result r=await Run("powershell.exe",PowershellArgs("Launch-SCUT.ps1",flag+" -NoPause"),true);
                if(r.Exit!=0 && !cancelled) throw new Exception("连接流程未完成，请查看运行记录。");
            });
        }
        async Task ApplyRules() {
            RuleFile rules=ReadGrid();
            string temp=Path.Combine(Path.GetTempPath(),"scut-rules-"+Guid.NewGuid().ToString("N")+".json");
            await Operation("正在校验并应用网段与域名规则…",async delegate {
                try {
                    File.WriteAllText(temp,json.Serialize(rules),new UTF8Encoding(false));
                    Result r=await Run(settings.PythonPath,Q(Path.Combine(root,"network-manager.py"))+" apply --input "+Q(temp),false);
                    var result=json.Deserialize<Dictionary<string,object>>(r.Output);
                    if(r.Exit!=0 || !(bool)result["ok"]) throw new Exception(Convert.ToString(result["error"]));
                    LoadRules(); AppendLog("规则已保存并生效；其他 Clash 节点选择已保留。");
                    if(Convert.ToInt32(result["restore_warnings"])>0) AppendLog("部分原有节点选择未能恢复，请在 Clash 检查。");
                } finally {if(File.Exists(temp)) File.Delete(temp);}
            },false);
        }
        async Task StopVpn() {
            await Operation("正在停止校园 VPN，校园路径将切回直连…",async delegate {
                Result route=await Run(settings.PythonPath,Q(Path.Combine(root,"campus-route.py"))+" direct",true);
                if(route.Exit!=0 && !cancelled) throw new Exception("直连切换失败，请先检查 Clash。");
                if(cancelled) return;
                string docker=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs\\DockerDesktop\\resources\\bin\\docker.exe");
                Result r=await Run(File.Exists(docker)?docker:"docker.exe","--context desktop-linux stop --time 10 scut-easyconnect",true);
                if(r.Exit!=0 && !cancelled) throw new Exception("容器停止命令未完成；若 Docker 已关闭，无需重复停止。");
            });
        }
        public async Task RefreshStatus() {
            if(busy || IsDisposed) return;
            // Serialize status probes with button actions so cancellation always targets the active task.
            busy=true;
            try {
                Result r=await Run(settings.PythonPath,Q(Path.Combine(root,"network-manager.py"))+" status",false);
                if(r.Exit!=0) {clashValue.Text="状态读取失败"; return;}
                var state=json.Deserialize<Dictionary<string,object>>(r.Output); lastState=r.Output;
                bool online=state.ContainsKey("clash") && (bool)state["clash"];
                string selected=state.ContainsKey("selected")?Convert.ToString(state["selected"]):"";
                routeValue.Text=selected=="DIRECT"?"校园直连":selected=="SCUT-EasyConnect-Docker"?"Docker VPN":"未就绪";
                clashValue.Text=online?((bool)state["tun"]?"TUN 已开启":"TUN 未开启"):"未连接";
                if(online && Convert.ToString(state["mode"])!="rule") clashValue.Text="请切规则模式";
                vpnValue.Text=(bool)state["vpn_port"]?"本机端口可用":"未就绪 / 已关闭";
            } catch {clashValue.Text="状态读取失败";}
            finally {busy=false;}
        }
        void OpenTermius() {TryOpen(delegate {Process.Start(new ProcessStartInfo(settings.TermiusPath) {UseShellExecute=true});});}
        void OpenVnc() {
            TryOpen(delegate {
                string[] paths={@"D:\Program Files\RealVNC\VNC Viewer\vncviewer.exe",@"C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe",@"C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe"};
                string viewer=paths.FirstOrDefault(File.Exists);
                if(viewer==null) throw new Exception("未找到 RealVNC Viewer，请先安装或手动打开。");
                Process.Start(new ProcessStartInfo(viewer,"127.0.0.1:5901") {UseShellExecute=true});
            });
        }
        void ShowPassword() {
            TryOpen(delegate {
                if(passwordLabel.Tag!=null) {passwordLabel.Tag=null; passwordLabel.Text="RealVNC 地址 127.0.0.1:5901\n校园账号与验证码请在 RealVNC 内填写。";return;}
                string entry=File.ReadAllLines(Path.Combine(root,".env")).FirstOrDefault(l=>l.StartsWith("VNC_PASSWORD="));
                if(entry==null) throw new Exception("VNC 密码尚未生成，请先启动校园 VPN。");
                passwordLabel.Text="RealVNC 127.0.0.1:5901\n本地连接密码："+entry.Substring(13)+"（再次点击隐藏）"; passwordLabel.Tag=true;
            });
        }
        void TryOpen(Action action) {try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);}}
        void OnClosing(object sender, FormClosingEventArgs e) {
            if(operationActive) {MessageBox.Show(this,"任务正在执行，请先取消等待或等待完成。",Text); e.Cancel=true;return;}
            if(dirty && MessageBox.Show(this,"还有未保存的规则，确定关闭吗？",Text,MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK) {e.Cancel=true;return;}
            timer.Stop();
        }
        public void SaveSmokeReport(string path) {File.WriteAllText(path,json.Serialize(new {created=true,rows=grid.Rows.Count,status=lastState,route=routeValue.Text}),new UTF8Encoding(false));}
    }
}
