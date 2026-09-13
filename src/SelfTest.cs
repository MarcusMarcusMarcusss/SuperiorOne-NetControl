using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SuperiorOneNet {
static class SelfTest {
    static void Check(bool value,string label) { if(!value) throw new Exception(label); }
    public static void Run(string output) {
        try {
            string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"test-data-"+Guid.NewGuid().ToString("N"));
            string file=Path.Combine(folder,"usage.json"); var store=new TrafficStore(file);
            var day=new DateTime(2026,9,13);
            store.Add("browser.exe","C:\\browser.exe",1024,false,day);
            store.Add("browser.exe","C:\\browser.exe",512,true,day);
            store.Add("browser.exe","C:\\browser.exe",100,false,day.AddDays(1));
            Check(store.Snapshot().Count==2,"Day separation");
            var rate=store.TakeRates().Values.Single(); Check(rate.Down==1124 && rate.Up==512,"Direction accounting");
            Check(store.TakeRates().Count==0,"Rate reset");
            store.Save(); store.Add("browser.exe","C:\\browser.exe",5,true,day); store.Save();
            var loaded=new TrafficStore(file); Check(loaded.Snapshot().Sum(x=>x.Upload)==517,"Persistence");
            Check(MainWindow.Format(1048576)=="1.00 MB","Units");
            File.WriteAllText(file,"broken"); var recovered=new TrafficStore(file); Check(recovered.Snapshot().Sum(x=>x.Upload)==512,"Backup recovery");
            store.Add("browser.exe","C:\\browser.exe",1048576,false,DateTime.Now);
            using(var form=new MainWindow(store,true))
            using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)) {
                form.ShowInTaskbar=false; form.Opacity=0; form.Show(); form.Render(); System.Windows.Forms.Application.DoEvents();
                form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));
                bitmap.Save(Path.Combine(folder,"ui-smoke.png"));
                form.Close();
            }
            File.WriteAllText(output,"PASS: daily separation, upload/download attribution, rate reset, persistence, units, backup recovery, UI rendering.\r\nTest data: "+folder);
        } catch(Exception ex) { File.WriteAllText(output,"FAIL: "+ex); Environment.ExitCode=1; }
    }
    public static void Capture(string output) {
        try {
            var store=new TrafficStore(Path.Combine(Path.GetDirectoryName(output),"capture-usage.json"));
            using(var trace=new NetworkTrace(store.Record)) {
                trace.Start(); Thread.Sleep(1200);
                var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
                var port=((IPEndPoint)listener.LocalEndpoint).Port;
                using(var client=new TcpClient()) {
                    client.Connect(IPAddress.Loopback,port);
                    using(var server=listener.AcceptTcpClient()) {
                        byte[] bytes=new byte[32768]; var stream=client.GetStream(); stream.Write(bytes,0,bytes.Length);
                        int total=0; while(total<bytes.Length) { int n=server.GetStream().Read(bytes,0,Math.Min(bytes.Length-total,bytes.Length)); if(n==0) break; total+=n; }
                    }
                }
                listener.Stop(); Thread.Sleep(2500); trace.QueryLoss();
                var rows=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").ToList();
                Check(rows.Sum(x=>x.Upload)>=32768 && rows.Sum(x=>x.Download)>=32768,"Real TCP events attributed to this process in both directions");
                Check(trace.Error==null,"Consumer error: "+trace.Error);
                string report="PASS: TCP IPv4 upload/download attribution.";
                foreach(var address in new[]{IPAddress.Loopback,IPAddress.IPv6Loopback}) {
                    long beforeUp=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Upload);
                    long beforeDown=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Download);
                    using(var receiver=new UdpClient(new IPEndPoint(address,0)))
                    using(var sender=new UdpClient(address.AddressFamily)) {
                        receiver.Client.ReceiveTimeout=3000;
                        byte[] packet=new byte[4096]; sender.Send(packet,packet.Length,(IPEndPoint)receiver.Client.LocalEndPoint);
                        IPEndPoint remote=null; Check(receiver.Receive(ref remote).Length==4096,"UDP delivery");
                    }
                    Thread.Sleep(2200);
                    long up=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Upload)-beforeUp;
                    long down=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Download)-beforeDown;
                    report+="\r\nUDP loopback "+address+": upload="+up+", download="+down+". "+trace.LastUdp;
                }
                long dnsUp=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Upload);
                long dnsDown=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Download);
                using(var dns=new UdpClient()) {
                    dns.Client.ReceiveTimeout=5000; dns.Connect("1.1.1.1",53);
                    byte[] query={0x12,0x34,1,0,0,1,0,0,0,0,0,0,7,101,120,97,109,112,108,101,3,99,111,109,0,0,1,0,1};
                    dns.Send(query,query.Length); IPEndPoint peer=null;
                    try { dns.Receive(ref peer); } catch(SocketException) { report+="\r\nDNS reply timed out."; }
                }
                Thread.Sleep(2500);
                long sent=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Upload)-dnsUp;
                long received=store.Snapshot().Where(x=>x.Name=="SuperiorOneNet.exe").Sum(x=>x.Download)-dnsDown;
                report+="\r\nUDP external DNS: upload="+sent+", download="+received+". "+trace.LastUdp;
                trace.QueryLoss(); Check(trace.Error==null,"Consumer error");
                File.WriteAllText(output,report+"\r\nLost events/buffers="+trace.LostEvents);
            }
        } catch(Exception ex) { File.WriteAllText(output,"FAIL: "+ex); Environment.ExitCode=1; }
    }
}
}
