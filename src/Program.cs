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
    readonly ProcessCatalog catalog=new ProcessCatalog();
    readonly HashSet<string> expanded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    List<ProcessIdentity> liveProcesses=new List<ProcessIdentity>();
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
    Dictionary<string,Rate> processRates=new Dictionary<string,Rate>();
    int sortColumn=6;
    bool sortDescending=true;
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
        string[] names={"软件 / 子进程","PID","下载速度 ↓","上传速度 ↑","下载用量","上传用量","总用量"};
        for(int i=0;i<names.Length;i++) { grid.Columns.Add("c"+i,names[i]); grid.Columns[i].SortMode=DataGridViewColumnSortMode.Programmatic; }
        grid.Columns[0].FillWeight=205; grid.Columns[1].FillWeight=55;
        grid.Columns[0].HeaderCell.ToolTipText="点击左侧箭头展开软件；缩进的每一行是一个独立进程。";
        for(int i=1;i<grid.Columns.Count;i++) {
            grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
            grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;
        }
        grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e) { if(e.ColumnIndex>=2 && e.Value!=null) {
            double value=Convert.ToDouble(e.Value);
            double scale=e.ColumnIndex<=3?1048576:1073741824;
            double heat=Math.Min(1,value/scale);
            e.CellStyle.BackColor=Color.FromArgb(17+(int)(heat*5),24+(int)(heat*25),34+(int)(heat*26));
            if(e.ColumnIndex==2) e.CellStyle.ForeColor=NetworkTheme.Download;
            if(e.ColumnIndex==3) e.CellStyle.ForeColor=NetworkTheme.Upload;
            e.Value=Format(value)+(e.ColumnIndex<=3?"/s":""); e.FormattingApplied=true;
        } };
        grid.ColumnHeaderMouseClick+=delegate(object sender,DataGridViewCellMouseEventArgs e) { if(e.ColumnIndex<0) return; if(sortColumn==e.ColumnIndex) sortDescending=!sortDescending; else {sortColumn=e.ColumnIndex;sortDescending=e.ColumnIndex>=2;} Render(); };
        grid.CellClick+=delegate(object sender,DataGridViewCellEventArgs e) { if(e.RowIndex<0 || e.ColumnIndex!=0) return; var cell=grid.GetCellDisplayRectangle(0,e.RowIndex,false); if(grid.PointToClient(Cursor.Position).X<cell.Left+32) ToggleGroup(grid.Rows[e.RowIndex].Tag as TrafficRow); };
        grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e) { if(e.RowIndex<0 || e.ColumnIndex!=0) return; var cell=grid.GetCellDisplayRectangle(0,e.RowIndex,false); if(grid.PointToClient(Cursor.Position).X>=cell.Left+32) ToggleGroup(grid.Rows[e.RowIndex].Tag as TrafficRow); };
        grid.KeyDown+=delegate(object sender,KeyEventArgs e) { var row=SelectedTrafficRow(); if(row==null) return; if(e.KeyCode==Keys.Right && row.IsGroup) { expanded.Add(row.Usage.Path); Render(); e.Handled=true; } if(e.KeyCode==Keys.Left && row.IsGroup) { expanded.Remove(row.Usage.Path); Render(); e.Handled=true; } };
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
        Shown+=delegate { if(preview) return; catalog.Refresh(); try { trace.Start(); monitoring=true; watch.Restart(); timer.Start(); Render(); } catch(Exception ex) { trace.Dispose(); monitoring=false; dashboard.CaptureState="监控未启动"; dashboard.Invalidate(); status.Text="监控未启动："+ex.Message; MessageBox.Show(status.Text,"无法启动监控"); } };
        timer.Tick+=delegate { seconds=Math.Max(.1,watch.Elapsed.TotalSeconds); watch.Restart(); rates=store.TakeRates(out processRates); liveProcesses=catalog.Snapshot(); dashboard.AddSample(rates.Values.Sum(x=>x.Down)/seconds,rates.Values.Sum(x=>x.Up)/seconds); ticks++; if(ticks%3==0) catalog.Refresh(); if(ticks%10==0) { Save(); trace.QueryLoss(); RefreshConnection(); } Render(); };
        FormClosed+=delegate { timer.Stop(); catalog.Dispose(); trace.Dispose(); Save(); tray.Visible=false; tray.Dispose(); if(saveError!="") MessageBox.Show(saveError,"保存失败"); };
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
    internal void SetPreviewProcesses(List<ProcessIdentity> processes,Dictionary<string,Rate> sample) { liveProcesses=processes; processRates=sample; Render(); }
    internal void ExpandGroup(string path) { expanded.Add(path); Render(); }
    void Save() { try { store.Save(); saveError=""; } catch(Exception ex) { saveError="历史记录保存失败："+ex.Message; } }
    TrafficRow SelectedTrafficRow() { return grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as TrafficRow:null; }
    void ToggleGroup(TrafficRow row) { if(row==null || !row.IsGroup) return; if(!expanded.Add(row.Usage.Path)) expanded.Remove(row.Usage.Path); Render(); }
    void UpdateEndButton() {
        var selected=SelectedTrafficRow();
        endTask.Text=selected==null?"结束任务":selected.IsGroup?"结束整组":"结束进程";
        endTask.Enabled=!dashboard.Preview && selected!=null && Path.IsPathRooted(selected.Usage.Path) && !string.Equals(selected.Usage.Path,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase)
            && (selected.IsGroup || selected.Process.StartedUtcTicks>0);
    }
    void EndSelected() {
        var selected=SelectedTrafficRow(); if(selected==null || dashboard.Preview) return;
        bool resume=timer.Enabled;
        timer.Stop();
        var targets=new List<ProcessTarget>();
        try {
            targets=ProcessTarget.Resolve(selected);
            if(targets.Count==0) { MessageBox.Show(this,"此软件已退出，或其进程受系统保护／无法访问。","无法结束任务",MessageBoxButtons.OK,MessageBoxIcon.Information); return; }
            string prompt=(selected.IsGroup?"结束 "+selected.Usage.Name+" 整组的 "+targets.Count+" 个进程？":"仅结束 "+selected.Usage.Name+" 的这个进程（PID "+selected.Process.Id+"）？")+"\n\n"+selected.Usage.Path+"\n\n未保存的内容可能丢失，软件可能自动重启已结束的子进程。";
            if(MessageBox.Show(this,prompt,"结束任务",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            int ended=0,exited=0; var errors=new List<string>();
            foreach(var target in targets) try { if(target.End()) ended++; else exited++; } catch(Exception ex) { errors.Add("PID "+target.Id+"："+ex.Message); }
            actionStatus="已结束 "+ended+" 个进程"+(exited>0?"，另有 "+exited+" 个已退出":"")+"。历史用量已保留。";
            if(errors.Count>0) MessageBox.Show(this,actionStatus+"\n\n部分进程未能结束：\n"+string.Join("\n",errors),"结束任务结果",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        } catch(Exception ex) { MessageBox.Show(this,ex.Message,"无法结束任务",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        finally { foreach(var target in targets) target.Dispose(); catalog.Refresh(); if(resume) timer.Start(); Render(); }
    }
    List<Usage> PeriodUsage() {
        string today=DateTime.Now.ToString("yyyy-MM-dd"), month=DateTime.Now.ToString("yyyy-MM");
        return store.Snapshot().Where(x=>range.SelectedIndex==2 || (range.SelectedIndex==0 && x.Day==today) || (range.SelectedIndex==1 && x.Day.StartsWith(month)) || (range.SelectedIndex==3 && x.Day==date.Value.ToString("yyyy-MM-dd"))).ToList();
    }
    List<Usage> VisibleUsage(List<Usage> period=null) {
        return (period??PeriodUsage()).GroupBy(x=>x.Path).Select(g=>new Usage {Name=g.First().Name,Path=g.Key,Download=g.Sum(x=>x.Download),Upload=g.Sum(x=>x.Upload)})
            .Where(x=>(x.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0 || x.Path.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0 || liveProcesses.Any(p=>p.Path==x.Path && p.Id.ToString()==search.Text.Trim())) && (!active.Checked || rates.ContainsKey(x.Path))).ToList();
    }
    Rate RowRate(TrafficRow row) { Rate rate; if(row.IsGroup) rates.TryGetValue(row.Usage.Path,out rate); else processRates.TryGetValue(row.Process.Key,out rate); return rate; }
    object SortValue(TrafficRow row) {
        var rate=RowRate(row);
        switch(sortColumn) { case 0:return row.Usage.Name; case 1:return row.IsGroup?0:row.Process.Id; case 2:return rate==null?0:rate.Down; case 3:return rate==null?0:rate.Up; case 4:return row.Usage.Download; case 5:return row.Usage.Upload; default:return row.Usage.Download+row.Usage.Upload; }
    }
    IEnumerable<TrafficRow> SortRows(IEnumerable<TrafficRow> rows) { return (sortDescending?rows.OrderByDescending(SortValue):rows.OrderBy(SortValue)).ThenBy(x=>x.Key,StringComparer.OrdinalIgnoreCase); }
    void AddGridRow(TrafficRow row,string label) {
        var rate=RowRate(row); var usage=row.Usage;
        int index=grid.Rows.Add(label,row.IsGroup?"—":row.Process.Id.ToString(),rate==null?0:rate.Down/seconds,rate==null?0:rate.Up/seconds,usage.Download,usage.Upload,usage.Download+usage.Upload);
        var view=grid.Rows[index]; view.Tag=row;
        view.Cells[0].Style.Padding=new Padding(row.IsGroup?7:38,0,7,0);
        view.Cells[0].Style.ForeColor=row.IsGroup?NetworkTheme.Text:NetworkTheme.Muted;
        view.Cells[0].ToolTipText=usage.Path+(row.IsGroup?"\n软件合计包含已退出进程和旧版记录；子行只列当前运行的进程。":"\nPID "+row.Process.Id+" · 仅结束这一进程；用量从开始监控后累计。");
    }
    internal void Render() {
        var period=PeriodUsage(); var rows=VisibleUsage(period);
        int first=grid.FirstDisplayedScrollingRowIndex;
        var selected=SelectedTrafficRow(); string selectedKey=selected==null?null:selected.Key;
        grid.Rows.Clear();
        foreach(var parent in SortRows(rows.Select(x=>new TrafficRow {Usage=x}))) {
            var children=liveProcesses.Where(p=>string.Equals(p.Path,parent.Usage.Path,StringComparison.OrdinalIgnoreCase)).ToList();
            bool open=expanded.Contains(parent.Usage.Path);
            AddGridRow(parent,(children.Count==0?"   ":open?"▾  ":"▸  ")+parent.Usage.Name+(children.Count>0?"  ("+children.Count+")":""));
            if(open) {
                var childRows=children.Select(p=>new TrafficRow {Process=p,Usage=new Usage {Name=p.Name,Path=p.Path,Pid=p.Id,StartedUtcTicks=p.StartedUtcTicks,
                    Download=period.Where(x=>x.Path==p.Path && x.Pid==p.Id && x.StartedUtcTicks==p.StartedUtcTicks).Sum(x=>x.Download),
                    Upload=period.Where(x=>x.Path==p.Path && x.Pid==p.Id && x.StartedUtcTicks==p.StartedUtcTicks).Sum(x=>x.Upload)}});
                foreach(var child in SortRows(childRows)) AddGridRow(child,"└  "+child.Usage.Name);
            }
        }
        for(int i=0;i<grid.Columns.Count;i++) grid.Columns[i].HeaderCell.SortGlyphDirection=i==sortColumn?(sortDescending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
        if(first>=0 && first<grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex=first;
        grid.ClearSelection();
        if(selectedKey!=null) foreach(DataGridViewRow row in grid.Rows) if(((TrafficRow)row.Tag).Key==selectedKey) { row.Selected=true; break; }
        UpdateEndButton();
        dashboard.CaptureState=monitoring && trace.Error==null?"正在监控":"监控未运行";
        dashboard.SetValues(rates.Values.Sum(x=>x.Down)/seconds,rates.Values.Sum(x=>x.Up)/seconds,rows.Sum(x=>x.Download+x.Upload),rates.Count);
        status.Text=saveError!=""?saveError:trace.Error!=null?trace.Error:trace.LostEvents>0?"监控中 · 已丢失 "+trace.LostEvents+" 个事件/缓冲区，统计可能偏低。":store.Warning!=""?store.Warning:"● 监控中 · "+rows.Count+" 个软件 · 速度为当前实时值，用量按所选日期统计\n含局域网及本机网络活动；仅记录程序运行期间。最小化后继续监控，关闭窗口即退出。";
        if(actionStatus!="" && saveError=="" && trace.Error==null) status.Text=actionStatus+"\n监控继续运行 · 选中软件后可结束任务。";
        if(!monitoring && !dashboard.Preview && trace.Error==null && saveError=="") status.Text="监控未运行 · 仍可查看已保存的历史记录。";
        if(dashboard.Preview) status.Text="界面预览 · 当前为示例数据，未启动网络采集。\n点击箭头展开软件；选中缩进的子进程，可只结束该进程。";
    }
    internal static string Format(double n) { string[] units={"B","KB","MB","GB","TB"}; int i=0; while(n>=1024 && i<units.Length-1) { n/=1024; i++; } return n.ToString(i==0?"0":"0.00")+" "+units[i]; }
    static string Csv(string s) { if(s.Length>0 && "=+-@".IndexOf(s[0])>=0) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
    void Export() { using(var dialog=new SaveFileDialog {Filter="CSV 文件|*.csv",FileName="网络用量-"+DateTime.Now.ToString("yyyyMMdd")+".csv"}) if(dialog.ShowDialog()==DialogResult.OK) try { var lines=new List<string>{"软件,路径,下载字节,上传字节,总字节"}; lines.AddRange(VisibleUsage().Select(x=>Csv(x.Name)+","+Csv(x.Path)+","+x.Download+","+x.Upload+","+(x.Download+x.Upload))); File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true)); } catch(Exception ex) { MessageBox.Show(ex.Message,"导出失败"); } }
}
}
