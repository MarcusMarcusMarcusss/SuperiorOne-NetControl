using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SuperiorOneNet {
static class Program {
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--termination-test-child")) { Thread.Sleep(30000); return; }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--preview")) { SelfTest.Preview(args.Length>1?args[1]:"preview.png"); return; }
        if(args.Contains("--self-test")) { SelfTest.Run(args.Length>1?args[1]:"self-test.txt"); return; }
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) {UseShellExecute=true,Verb="runas",Arguments=string.Join(" ",args.Select(x=>"\""+x+"\""))}); }
            catch(Exception ex) { MessageBox.Show("需要管理员权限才能按软件监控网络。\n"+ex.Message,"SuperiorOne Net"); }
            return;
        }
        bool first;
        using(var mutex=new Mutex(true,"Local\\SuperiorOneNet.Desktop",out first)) {
            if(!first) { MessageBox.Show("程序已在运行，请查看右下角系统托盘。","SuperiorOne Net"); return; }
            if(args.Contains("--capture-test")) { SelfTest.Capture(args[1]); return; }
            try { Application.Run(new MainWindow()); }
            catch(Exception ex) { MessageBox.Show(ex.ToString(),"启动失败"); }
        }
    }
}
sealed class MainWindow : Form {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    readonly TrafficStore store;
    readonly NetworkTrace trace;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=1000};
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly DataGridView grid=new DataGridView();
    readonly Label status=new Label();
    readonly NetworkDashboard dashboard=new NetworkDashboard();
    readonly TextBox search=new TextBox {Width=175};
    readonly ComboBox range=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
    readonly DateTimePicker date=new DateTimePicker {Format=DateTimePickerFormat.Short,Width=125};
    readonly CheckBox active=new CheckBox {Text="仅显示正在联网",AutoSize=true,Padding=new Padding(8,3,0,0)};
    readonly NotifyIcon tray;
    readonly Button endTask=new NetworkButton {Text="结束任务",AutoSize=true,Enabled=false,Height=32};
    string actionStatus="";
    Dictionary<string,Rate> rates=new Dictionary<string,Rate>();
    double seconds=1;
    int ticks;
    string saveError="";
    bool monitoring;
    public MainWindow(TrafficStore suppliedStore=null, bool preview=false) {
        Text="SuperiorOne Net · 网络流量监控"; Size=new Size(1220,900); MinimumSize=new Size(1050,740);
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Microsoft YaHei UI",9); BackColor=NetworkTheme.Background; ForeColor=NetworkTheme.Text;
        HandleCreated+=delegate { try { int dark=1; DwmSetWindowAttribute(Handle,20,ref dark,4); int caption=0x00140e0a; DwmSetWindowAttribute(Handle,35,ref caption,4); } catch(DllNotFoundException) { } };
        store=suppliedStore ?? new TrafficStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SuperiorOneNet","usage.json"));
        trace=new NetworkTrace(store.Record);
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(24,10,24,10)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,276));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        var title=new NetworkBrand {Dock=DockStyle.Fill};
        dashboard.Dock=DockStyle.Fill; dashboard.Preview=preview; RefreshConnection();
        var filters=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,5,0,0)};
        search.BackColor=NetworkTheme.Panel; search.ForeColor=NetworkTheme.Text; search.BorderStyle=BorderStyle.FixedSingle;
        range.BackColor=NetworkTheme.Panel; range.ForeColor=NetworkTheme.Text; range.FlatStyle=FlatStyle.Flat;
        range.DrawMode=DrawMode.OwnerDrawFixed;
        range.DrawItem+=delegate(object sender,DrawItemEventArgs e) { if(e.Index<0) return; using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(34,61,80):NetworkTheme.Panel)) e.Graphics.FillRectangle(brush,e.Bounds); TextRenderer.DrawText(e.Graphics,range.Items[e.Index].ToString(),Font,e.Bounds,NetworkTheme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter); };
        range.Items.AddRange(new object[]{"今天","本月","全部记录","指定日期"}); range.SelectedIndex=0;
        date.Enabled=false; date.Visible=false;
        filters.Controls.Add(new Label {Text="查找软件",AutoSize=true,Padding=new Padding(0,5,0,0)}); filters.Controls.Add(search); filters.Controls.Add(range); filters.Controls.Add(date); filters.Controls.Add(active);
        var export=new NetworkButton {Text="导出 CSV",AutoSize=true}; NetworkTheme.Button(export,false); export.Click+=delegate { Export(); }; filters.Controls.Add(export);
        NetworkTheme.Button(endTask,true); endTask.MinimumSize=new Size(108,32);
        grid.Dock=DockStyle.Fill; grid.ReadOnly=true; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor=NetworkTheme.Panel; grid.BorderStyle=BorderStyle.None; grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(24,34,47); grid.ColumnHeadersHeight=44;
        grid.ColumnHeadersDefaultCellStyle.ForeColor=NetworkTheme.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Color.FromArgb(24,34,47);
        grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
        grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal; grid.GridColor=NetworkTheme.Border;
        grid.RowTemplate.Height=38; grid.MultiSelect=false;
        grid.DefaultCellStyle.BackColor=NetworkTheme.Panel; grid.DefaultCellStyle.ForeColor=NetworkTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(29,61,78);
        grid.DefaultCellStyle.SelectionForeColor=Color.White;
        grid.DefaultCellStyle.Padding=new Padding(7,0,7,0);
        string[] names={"软件","下载速度 ↓","上传速度 ↑","下载用量","上传用量","总用量"};
        for(int i=0;i<names.Length;i++) { grid.Columns.Add("c"+i,names[i]); grid.Columns[i].SortMode=DataGridViewColumnSortMode.Automatic; }
        grid.Columns[0].FillWeight=170;
        for(int i=1;i<grid.Columns.Count;i++) {
            grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
            grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;
        }
        grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e) { if(e.ColumnIndex>0 && e.Value!=null) {
            double value=Convert.ToDouble(e.Value);
            double scale=e.ColumnIndex<=2?1048576:1073741824;
            double heat=Math.Min(1,value/scale);
            e.CellStyle.BackColor=Color.FromArgb(17+(int)(heat*5),24+(int)(heat*25),34+(int)(heat*26));
            if(e.ColumnIndex==1) e.CellStyle.ForeColor=NetworkTheme.Download;
            if(e.ColumnIndex==2) e.CellStyle.ForeColor=NetworkTheme.Upload;
            e.Value=Format(value)+(e.ColumnIndex<=2?"/s":""); e.FormattingApplied=true;
        } };
        status.Dock=DockStyle.Fill; status.ForeColor=NetworkTheme.Muted; status.TextAlign=ContentAlignment.MiddleLeft;
        var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));
        header.Controls.Add(title,0,0); header.Controls.Add(endTask,1,0); endTask.Anchor=AnchorStyles.Right;
        endTask.Click+=delegate { EndSelected(); };
        grid.SelectionChanged+=delegate { UpdateEndButton(); };
        var listTitle=new Label {Text="应用网络活动    /    APPLICATIONS",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=NetworkTheme.Text};
        layout.Controls.Add(header,0,0); layout.Controls.Add(dashboard,0,1); layout.Controls.Add(listTitle,0,2); layout.Controls.Add(filters,0,3); layout.Controls.Add(grid,0,4); layout.Controls.Add(status,0,5); Controls.Add(layout);
        tray=new NotifyIcon {Icon=SystemIcons.Application,Text="SuperiorOne Net · 网络监控",Visible=!preview};
        var menu=new ContextMenuStrip(); menu.Items.Add("打开监控面板",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); }); menu.Items.Add("退出并停止监控",null,delegate { Close(); }); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { Show(); WindowState=FormWindowState.Normal; Activate(); };
        Resize+=delegate { if(WindowState==FormWindowState.Minimized) { Hide(); tray.ShowBalloonTip(2000,"网络监控仍在运行","双击此图标可打开面板。",ToolTipIcon.Info); } };
        search.TextChanged+=delegate { Render(); }; range.SelectedIndexChanged+=delegate { date.Enabled=range.SelectedIndex==3; date.Visible=date.Enabled; Render(); }; date.ValueChanged+=delegate { Render(); }; active.CheckedChanged+=delegate { Render(); };
        Shown+=delegate { if(preview) return; try { trace.Start(); monitoring=true; watch.Restart(); timer.Start(); Render(); } catch(Exception ex) { trace.Dispose(); monitoring=false; dashboard.CaptureState="监控未启动"; dashboard.Invalidate(); status.Text="监控未启动："+ex.Message; MessageBox.Show(status.Text,"无法启动监控"); } };
        timer.Tick+=delegate { seconds=Math.Max(.1,watch.Elapsed.TotalSeconds); watch.Restart(); rates=store.TakeRates(); dashboard.AddSample(rates.Values.Sum(x=>x.Down)/seconds,rates.Values.Sum(x=>x.Up)/seconds); ticks++; if(ticks%10==0) { Save(); trace.QueryLoss(); RefreshConnection(); } Render(); };
        FormClosed+=delegate { timer.Stop(); trace.Dispose(); Save(); tray.Visible=false; tray.Dispose(); if(saveError!="") MessageBox.Show(saveError,"保存失败"); };
    }
    void RefreshConnection() {
        try {
            var nic=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up && x.NetworkInterfaceType!=NetworkInterfaceType.Loopback && x.NetworkInterfaceType!=NetworkInterfaceType.Tunnel)
                .OrderByDescending(x=>x.GetIPProperties().GatewayAddresses.Count>0).ThenByDescending(x=>x.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).FirstOrDefault();
            dashboard.Connection=nic==null?"未检测到活动网卡":(nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211?"Wi-Fi":"网络")+" · "+nic.Name;
        } catch { dashboard.Connection="网卡信息暂不可用"; }
    }
    internal void SetPreviewRates(Dictionary<string,Rate> sample) { rates=sample; seconds=1; dashboard.Connection="Wi-Fi · 界面示例"; Render(); }
    internal void AddPreviewSample(double download,double upload) { dashboard.AddSample(download,upload); }
    void Save() { try { store.Save(); saveError=""; } catch(Exception ex) { saveError="历史记录保存失败："+ex.Message; } }
    Usage SelectedUsage() { return grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as Usage:null; }
    void UpdateEndButton() {
        var selected=SelectedUsage();
        endTask.Enabled=selected!=null && Path.IsPathRooted(selected.Path) && !string.Equals(selected.Path,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase);
    }
    void EndSelected() {
        var selected=SelectedUsage(); if(selected==null) return;
        timer.Stop();
        var targets=new List<ProcessTarget>();
        try {
            targets=ProcessTarget.Find(selected.Path);
            if(targets.Count==0) { MessageBox.Show(this,"此软件已退出，或其进程受系统保护／无法访问。","无法结束任务",MessageBoxButtons.OK,MessageBoxIcon.Information); return; }
            string prompt="结束 "+selected.Name+" 的 "+targets.Count+" 个进程？\n\n"+selected.Path+"\n\n软件会立即关闭，未保存的内容可能丢失。";
            if(MessageBox.Show(this,prompt,"结束任务",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            int ended=0,exited=0; var errors=new List<string>();
            foreach(var target in targets) try { if(target.End()) ended++; else exited++; } catch(Exception ex) { errors.Add("PID "+target.Id+"："+ex.Message); }
            actionStatus="已结束 "+ended+" 个进程"+(exited>0?"，另有 "+exited+" 个已退出":"")+"。历史用量已保留。";
            if(errors.Count>0) MessageBox.Show(this,actionStatus+"\n\n部分进程未能结束：\n"+string.Join("\n",errors),"结束任务结果",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        } catch(Exception ex) { MessageBox.Show(this,ex.Message,"无法结束任务",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        finally { foreach(var target in targets) target.Dispose(); timer.Start(); Render(); }
    }
    List<Usage> VisibleUsage() {
        string today=DateTime.Now.ToString("yyyy-MM-dd"), month=DateTime.Now.ToString("yyyy-MM");
        var rows=store.Snapshot().Where(x=>range.SelectedIndex==2 || (range.SelectedIndex==0 && x.Day==today) || (range.SelectedIndex==1 && x.Day.StartsWith(month)) || (range.SelectedIndex==3 && x.Day==date.Value.ToString("yyyy-MM-dd")));
        return rows.GroupBy(x=>x.Path).Select(g=>new Usage {Name=g.First().Name,Path=g.Key,Download=g.Sum(x=>x.Download),Upload=g.Sum(x=>x.Upload)})
            .Where(x=>(x.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0 || x.Path.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0) && (!active.Checked || rates.ContainsKey(x.Path))).OrderByDescending(x=>x.Download+x.Upload).ToList();
    }
    internal void Render() {
        var rows=VisibleUsage();
        int sort=grid.SortedColumn==null?-1:grid.SortedColumn.Index; var direction=grid.SortOrder;
        int first=grid.FirstDisplayedScrollingRowIndex;
        var selected=SelectedUsage(); string selectedPath=selected==null?null:selected.Path;
        grid.Rows.Clear();
        foreach(var row in rows) { Rate rate; rates.TryGetValue(row.Path,out rate); int index=grid.Rows.Add(row.Name,rate==null?0:rate.Down/seconds,rate==null?0:rate.Up/seconds,row.Download,row.Upload,row.Download+row.Upload); grid.Rows[index].Cells[0].ToolTipText=row.Path; grid.Rows[index].Tag=row; }
        if(sort>=0) grid.Sort(grid.Columns[sort],direction==SortOrder.Ascending?System.ComponentModel.ListSortDirection.Ascending:System.ComponentModel.ListSortDirection.Descending);
        if(first>=0 && first<grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex=first;
        grid.ClearSelection();
        if(selectedPath!=null) foreach(DataGridViewRow row in grid.Rows) if(((Usage)row.Tag).Path==selectedPath) { row.Selected=true; break; }
        UpdateEndButton();
        dashboard.CaptureState=monitoring && trace.Error==null?"正在监控":"监控未运行";
        dashboard.SetValues(rates.Values.Sum(x=>x.Down)/seconds,rates.Values.Sum(x=>x.Up)/seconds,rows.Sum(x=>x.Download+x.Upload),rates.Count);
        status.Text=saveError!=""?saveError:trace.Error!=null?trace.Error:trace.LostEvents>0?"监控中 · 已丢失 "+trace.LostEvents+" 个事件/缓冲区，统计可能偏低。":store.Warning!=""?store.Warning:"● 监控中 · "+rows.Count+" 个软件 · 速度为当前实时值，用量按所选日期统计\n含局域网及本机网络活动；仅记录程序运行期间。最小化后继续监控，关闭窗口即退出。";
        if(actionStatus!="" && saveError=="" && trace.Error==null) status.Text=actionStatus+"\n监控继续运行 · 选中软件后可结束任务。";
        if(!monitoring && !dashboard.Preview && trace.Error==null && saveError=="") status.Text="监控未运行 · 仍可查看已保存的历史记录。";
        if(dashboard.Preview) status.Text="界面预览 · 当前为示例数据，未启动网络采集。\n正常启动程序后，列表与流量曲线将显示实际监控结果。";
    }
    internal static string Format(double n) { string[] units={"B","KB","MB","GB","TB"}; int i=0; while(n>=1024 && i<units.Length-1) { n/=1024; i++; } return n.ToString(i==0?"0":"0.00")+" "+units[i]; }
    static string Csv(string s) { if(s.Length>0 && "=+-@".IndexOf(s[0])>=0) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
    void Export() { using(var dialog=new SaveFileDialog {Filter="CSV 文件|*.csv",FileName="网络用量-"+DateTime.Now.ToString("yyyyMMdd")+".csv"}) if(dialog.ShowDialog()==DialogResult.OK) try { var lines=new List<string>{"软件,路径,下载字节,上传字节,总字节"}; lines.AddRange(VisibleUsage().Select(x=>Csv(x.Name)+","+Csv(x.Path)+","+x.Download+","+x.Upload+","+(x.Download+x.Upload))); File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true)); } catch(Exception ex) { MessageBox.Show(ex.Message,"导出失败"); } }
}
}
