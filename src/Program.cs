using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SuperiorOneNet {
static class Program {
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
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
    readonly TrafficStore store;
    readonly NetworkTrace trace;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=1000};
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly DataGridView grid=new DataGridView();
    readonly Label headline=new Label(), status=new Label();
    readonly TextBox search=new TextBox {Width=200};
    readonly ComboBox range=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
    readonly DateTimePicker date=new DateTimePicker {Format=DateTimePickerFormat.Short,Width=125};
    readonly CheckBox active=new CheckBox {Text="仅显示正在联网",AutoSize=true,Padding=new Padding(8,3,0,0)};
    readonly NotifyIcon tray;
    Dictionary<string,Rate> rates=new Dictionary<string,Rate>();
    double seconds=1;
    int ticks;
    string saveError="";
    public MainWindow(TrafficStore suppliedStore=null, bool preview=false) {
        Text="SuperiorOne Net · 网络流量监控"; Size=new Size(1120,730); MinimumSize=new Size(1020,550);
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Microsoft YaHei UI",10); BackColor=Color.FromArgb(245,247,251);
        store=suppliedStore ?? new TrafficStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SuperiorOneNet","usage.json"));
        trace=new NetworkTrace(store.Record);
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(24)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        var title=new Label {Text="网络用量   /   SuperiorOne Net",Font=new Font(Font.FontFamily,20,FontStyle.Bold),Dock=DockStyle.Fill,ForeColor=Color.FromArgb(25,37,61)};
        headline.Dock=DockStyle.Fill; headline.Font=new Font(Font.FontFamily,15,FontStyle.Bold); headline.ForeColor=Color.FromArgb(25,104,185); headline.TextAlign=ContentAlignment.MiddleLeft;
        var filters=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
        range.Items.AddRange(new object[]{"今天","本月","全部记录","指定日期"}); range.SelectedIndex=0;
        date.Enabled=false;
        filters.Controls.Add(new Label {Text="查找软件",AutoSize=true,Padding=new Padding(0,5,0,0)}); filters.Controls.Add(search); filters.Controls.Add(range); filters.Controls.Add(date); filters.Controls.Add(active);
        var export=new Button {Text="导出 CSV",AutoSize=true}; export.Click+=delegate { Export(); }; filters.Controls.Add(export);
        grid.Dock=DockStyle.Fill; grid.ReadOnly=true; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor=Color.White; grid.BorderStyle=BorderStyle.None; grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(231,237,246); grid.ColumnHeadersHeight=42;
        grid.RowTemplate.Height=36; grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249,251,254);
        string[] names={"软件","下载速度 ↓","上传速度 ↑","下载用量","上传用量","总用量"};
        for(int i=0;i<names.Length;i++) { grid.Columns.Add("c"+i,names[i]); grid.Columns[i].SortMode=DataGridViewColumnSortMode.Automatic; }
        grid.Columns[0].FillWeight=170;
        grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e) { if(e.ColumnIndex>0 && e.Value!=null) { e.Value=Format(Convert.ToDouble(e.Value))+(e.ColumnIndex<=2?"/s":""); e.FormattingApplied=true; } };
        status.Dock=DockStyle.Fill; status.ForeColor=Color.FromArgb(91,103,121); status.TextAlign=ContentAlignment.MiddleLeft;
        layout.Controls.Add(title,0,0); layout.Controls.Add(headline,0,1); layout.Controls.Add(filters,0,2); layout.Controls.Add(grid,0,3); layout.Controls.Add(status,0,4); Controls.Add(layout);
        tray=new NotifyIcon {Icon=SystemIcons.Application,Text="SuperiorOne Net · 网络监控",Visible=!preview};
        var menu=new ContextMenuStrip(); menu.Items.Add("打开监控面板",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); }); menu.Items.Add("退出并停止监控",null,delegate { Close(); }); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { Show(); WindowState=FormWindowState.Normal; Activate(); };
        Resize+=delegate { if(WindowState==FormWindowState.Minimized) { Hide(); tray.ShowBalloonTip(2000,"网络监控仍在运行","双击此图标可打开面板。",ToolTipIcon.Info); } };
        search.TextChanged+=delegate { Render(); }; range.SelectedIndexChanged+=delegate { date.Enabled=range.SelectedIndex==3; Render(); }; date.ValueChanged+=delegate { Render(); }; active.CheckedChanged+=delegate { Render(); };
        Shown+=delegate { if(preview) return; try { trace.Start(); watch.Restart(); timer.Start(); Render(); } catch(Exception ex) { trace.Dispose(); status.Text="监控未启动："+ex.Message; MessageBox.Show(status.Text,"无法启动监控"); } };
        timer.Tick+=delegate { seconds=Math.Max(.1,watch.Elapsed.TotalSeconds); watch.Restart(); rates=store.TakeRates(); ticks++; if(ticks%10==0) { Save(); trace.QueryLoss(); } Render(); };
        FormClosed+=delegate { timer.Stop(); trace.Dispose(); Save(); tray.Visible=false; tray.Dispose(); if(saveError!="") MessageBox.Show(saveError,"保存失败"); };
    }
    void Save() { try { store.Save(); saveError=""; } catch(Exception ex) { saveError="历史记录保存失败："+ex.Message; } }
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
        grid.Rows.Clear();
        foreach(var row in rows) { Rate rate; rates.TryGetValue(row.Path,out rate); int index=grid.Rows.Add(row.Name,rate==null?0:rate.Down/seconds,rate==null?0:rate.Up/seconds,row.Download,row.Upload,row.Download+row.Upload); grid.Rows[index].Cells[0].ToolTipText=row.Path; }
        if(sort>=0) grid.Sort(grid.Columns[sort],direction==SortOrder.Ascending?System.ComponentModel.ListSortDirection.Ascending:System.ComponentModel.ListSortDirection.Descending);
        if(first>=0 && first<grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex=first;
        headline.Text="↓ "+Format(rates.Values.Sum(x=>x.Down)/seconds)+"/s     ↑ "+Format(rates.Values.Sum(x=>x.Up)/seconds)+"/s       筛选用量  "+Format(rows.Sum(x=>x.Download+x.Upload));
        status.Text=saveError!=""?saveError:trace.Error!=null?trace.Error:trace.LostEvents>0?"监控中 · 已丢失 "+trace.LostEvents+" 个事件/缓冲区，统计可能偏低。":store.Warning!=""?store.Warning:"● 监控中 · "+rows.Count+" 个软件 · 速度为当前实时值，用量按所选日期统计\n含局域网及本机网络活动；仅记录程序运行期间。最小化后继续监控，关闭窗口即退出。";
    }
    internal static string Format(double n) { string[] units={"B","KB","MB","GB","TB"}; int i=0; while(n>=1024 && i<units.Length-1) { n/=1024; i++; } return n.ToString(i==0?"0":"0.00")+" "+units[i]; }
    static string Csv(string s) { if(s.Length>0 && "=+-@".IndexOf(s[0])>=0) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
    void Export() { using(var dialog=new SaveFileDialog {Filter="CSV 文件|*.csv",FileName="网络用量-"+DateTime.Now.ToString("yyyyMMdd")+".csv"}) if(dialog.ShowDialog()==DialogResult.OK) try { var lines=new List<string>{"软件,路径,下载字节,上传字节,总字节"}; lines.AddRange(VisibleUsage().Select(x=>Csv(x.Name)+","+Csv(x.Path)+","+x.Download+","+x.Upload+","+(x.Download+x.Upload))); File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true)); } catch(Exception ex) { MessageBox.Show(ex.Message,"导出失败"); } }
}
}
