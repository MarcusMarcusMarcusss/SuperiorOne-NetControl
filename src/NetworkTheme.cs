using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SuperiorOneNet {
static class NetworkTheme {
    public static readonly Color Background=Color.FromArgb(10,14,20);
    public static readonly Color Panel=Color.FromArgb(17,24,34);
    public static readonly Color Border=Color.FromArgb(37,49,64);
    public static readonly Color Text=Color.FromArgb(230,239,248);
    public static readonly Color Muted=Color.FromArgb(141,159,180);
    public static readonly Color Download=Color.FromArgb(58,209,222);
    public static readonly Color Upload=Color.FromArgb(129,151,255);
    public static void Button(Button button, bool danger) {
        button.FlatStyle=FlatStyle.Flat; button.BackColor=danger?Color.FromArgb(55,29,36):Panel;
        button.ForeColor=danger?Color.FromArgb(255,154,161):Text;
        button.FlatAppearance.BorderColor=danger?Color.FromArgb(110,54,65):Border;
        button.FlatAppearance.MouseOverBackColor=Color.FromArgb(38,52,69);
        button.Cursor=Cursors.Hand; button.Padding=new Padding(9,2,9,2);
    }
    public static void Wifi(Graphics g, Rectangle bounds, Color color) {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        float cx=bounds.X+bounds.Width/2f, cy=bounds.Y+bounds.Height*.78f;
        using(var pen=new Pen(color,3)) {
            pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round;
            for(int i=1;i<=3;i++) { float r=bounds.Width*i*.16f; g.DrawArc(pen,cx-r,cy-r,r*2,r*2,222,96); }
        }
        using(var brush=new SolidBrush(color)) g.FillEllipse(brush,cx-3,cy-3,6,6);
    }
}
sealed class NetworkButton : Button {
    bool hovering;
    public NetworkButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true); }
    protected override void OnMouseEnter(EventArgs e) { hovering=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) {
        Color background=Enabled?hovering?FlatAppearance.MouseOverBackColor:BackColor:NetworkTheme.Panel;
        using(var brush=new SolidBrush(background)) e.Graphics.FillRectangle(brush,ClientRectangle);
        using(var pen=new Pen(Enabled?FlatAppearance.BorderColor:NetworkTheme.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:NetworkTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-8,Height-8),ForeColor,background);
    }
}
sealed class NetworkBrand : Control {
    public NetworkBrand() { DoubleBuffered=true; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        NetworkTheme.Wifi(e.Graphics,new Rectangle(2,9,56,56),NetworkTheme.Download);
        using(var title=new Font("Segoe UI",19,FontStyle.Bold))
            TextRenderer.DrawText(e.Graphics,"SuperiorOne Net",title,new Rectangle(74,8,Width-80,35),NetworkTheme.Text,TextFormatFlags.Left|TextFormatFlags.NoPadding);
        using(var sub=new Font("Microsoft YaHei UI",9))
            TextRenderer.DrawText(e.Graphics,"NETWORK MONITOR   /   网络流量监控",sub,new Rectangle(76,47,Width-80,25),NetworkTheme.Muted,TextFormatFlags.Left|TextFormatFlags.NoPadding);
    }
}
sealed class NetworkDashboard : Control {
    readonly List<double> down=new List<double>(),up=new List<double>();
    double download,upload,total;
    int activeCount;
    public string Connection="正在读取网络状态";
    public string CaptureState="准备监控";
    public bool Preview;
    public NetworkDashboard() { DoubleBuffered=true; BackColor=NetworkTheme.Background; }
    public void SetValues(double d,double u,double t,int count) { download=d; upload=u; total=t; activeCount=count; Invalidate(); }
    public void AddSample(double d,double u) { down.Add(d); up.Add(u); if(down.Count>60) {down.RemoveAt(0);up.RemoveAt(0);} Invalidate(); }
    void TextAt(Graphics g,string text,float size,FontStyle style,Color color,Rectangle rect) {
        using(var font=new Font("Microsoft YaHei UI",size,style))
            TextRenderer.DrawText(g,text,font,rect,color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        int gap=12, cardWidth=(Width-gap*3)/4;
        string[] labels={"↓  实时下载","↑  实时上传","已筛选流量","正在联网的软件"};
        string[] values={MainWindow.Format(download)+"/s",MainWindow.Format(upload)+"/s",MainWindow.Format(total),activeCount.ToString()+" 个"};
        Color[] colors={NetworkTheme.Download,NetworkTheme.Upload,NetworkTheme.Text,NetworkTheme.Text};
        for(int i=0;i<4;i++) {
            var box=new Rectangle(i*(cardWidth+gap),0,cardWidth,91);
            using(var b=new SolidBrush(NetworkTheme.Panel)) g.FillRectangle(b,box);
            using(var p=new Pen(NetworkTheme.Border)) g.DrawRectangle(p,box.X,box.Y,box.Width-1,box.Height-1);
            TextAt(g,labels[i],9,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(box.X+16,10,box.Width-30,22));
            TextAt(g,values[i],21,FontStyle.Bold,colors[i],new Rectangle(box.X+16,37,box.Width-30,40));
        }
        var lower=new Rectangle(0,103,Width-1,Height-104);
        if(lower.Height<100) return;
        using(var b=new SolidBrush(NetworkTheme.Panel)) g.FillRectangle(b,lower);
        using(var pen=new Pen(NetworkTheme.Border)) g.DrawRectangle(pen,lower);
        int infoWidth=Math.Min(270,Width/4);
        NetworkTheme.Wifi(g,new Rectangle(16,119,42,42),NetworkTheme.Download);
        TextAt(g,Preview?"界面预览 · 示例数据":CaptureState,10,FontStyle.Bold,NetworkTheme.Download,new Rectangle(70,119,infoWidth-78,31));
        TextAt(g,Connection,9,FontStyle.Regular,NetworkTheme.Text,new Rectangle(20,167,infoWidth-32,28));
        TextAt(g,"所有软件 · 上传 / 下载",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(20,196,infoWidth-32,25));
        TextAt(g,"仅统计监控运行期间",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(20,221,infoWidth-32,23));
        var plot=new Rectangle(infoWidth+18,139,Width-infoWidth-39,Height-173);
        TextAt(g,"流量趋势",9,FontStyle.Bold,NetworkTheme.Text,new Rectangle(plot.X,108,150,25));
        TextAt(g,"↓ 下载     ↑ 上传",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(Width-155,108,145,25));
        using(var pen=new Pen(NetworkTheme.Border)) {
            g.DrawLine(pen,infoWidth,119,infoWidth,Height-16);
            for(int i=0;i<=4;i++) g.DrawLine(pen,plot.Left,plot.Top+i*plot.Height/4,plot.Right,plot.Top+i*plot.Height/4);
            for(int i=0;i<=10;i++) g.DrawLine(pen,plot.Left+i*plot.Width/10,plot.Top,plot.Left+i*plot.Width/10,plot.Bottom);
        }
        double maximum=Math.Max(1024,down.Concat(up).DefaultIfEmpty(0).Max()*1.15);
        DrawSeries(g,down,plot,maximum,NetworkTheme.Download);
        DrawSeries(g,up,plot,maximum,NetworkTheme.Upload);
        TextAt(g,MainWindow.Format(maximum)+"/s",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(plot.Left+5,plot.Top+2,150,19));
        TextAt(g,"最近 60 次采样",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(plot.Left,Height-27,180,20));
        TextAt(g,"现在",8,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(plot.Right-35,Height-27,40,20));
        if(down.Count<2) TextAt(g,"等待网络流量…",9,FontStyle.Regular,NetworkTheme.Muted,new Rectangle(plot.X+plot.Width/2-70,plot.Y+plot.Height/2-12,160,24));
    }
    static void DrawSeries(Graphics g,List<double> values,Rectangle plot,double max,Color color) {
        if(values.Count<2) return;
        var points=values.Select((v,i)=>new PointF(plot.Right-(values.Count-1-i)*plot.Width/59f,plot.Bottom-(float)(v/max)*plot.Height)).ToArray();
        using(var path=new GraphicsPath()) {
            path.AddLines(points); path.AddLine(points[points.Length-1],new PointF(points[points.Length-1].X,plot.Bottom));
            path.AddLine(new PointF(points[points.Length-1].X,plot.Bottom),new PointF(points[0].X,plot.Bottom)); path.CloseFigure();
            using(var brush=new SolidBrush(Color.FromArgb(22,color))) g.FillPath(brush,path);
        }
        using(var pen=new Pen(color,2)) g.DrawLines(pen,points);
    }
}
}
