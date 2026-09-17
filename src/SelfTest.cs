using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SuperiorOneNet {
static class SelfTest {
    static void Check(bool value,string label) { if(!value) throw new Exception(label); }
    public static void Preview(string output) {
        try {
            output=Path.GetFullPath(output);
            string folder=Path.Combine(Path.GetDirectoryName(output),"test-data-preview-"+Guid.NewGuid().ToString("N"));
            var store=new TrafficStore(Path.Combine(folder,"usage.json"));
            string[] apps={"msedge.exe","Steam.exe","OneDrive.exe","Discord.exe","Spotify.exe","Windows Update"};
            long[] downloaded={1845493760,8589934592,419430400,91226112,284164096,104857600};
            long[] uploaded={134217728,24641536,671088640,46137344,3145728,1048576};
            long[] down={2621440,6815744,393216,18432,245760,0};
            long[] up={81920,24576,1392640,45056,4096,0};
            var samples=new System.Collections.Generic.Dictionary<string,Rate>();
            var previewProcesses=new System.Collections.Generic.List<ProcessIdentity>();
            var previewRates=new System.Collections.Generic.Dictionary<string,Rate>();
            for(int i=0;i<apps.Length;i++) {
                string path="C:\\Preview\\"+apps[i];
                int count=i==1?3:i==0?4:1;
                for(int child=0;child<count;child++) {
                    var process=new ProcessIdentity {Id=4100+i*100+child*4,Name=apps[i],Path=path,StartedUtcTicks=DateTime.Today.ToUniversalTime().Ticks+child};
                    previewProcesses.Add(process);
                    long d=child==0?downloaded[i]-(downloaded[i]/count)*(count-1):downloaded[i]/count;
                    long u=child==0?uploaded[i]-(uploaded[i]/count)*(count-1):uploaded[i]/count;
                    store.Add(apps[i],path,d,false,DateTime.Now,process.Id,process.StartedUtcTicks); store.Add(apps[i],path,u,true,DateTime.Now,process.Id,process.StartedUtcTicks);
                    previewRates[process.Key]=new Rate {Name=apps[i],Path=path,Down=child==0?down[i]:0,Up=child==0?up[i]:0};
                }
                if(down[i]+up[i]>0) samples[path]=new Rate {Name=apps[i],Path=path,Down=down[i],Up=up[i]};
            }
            using(var form=new MainWindow(store,true)) {
                form.ShowInTaskbar=false; form.Opacity=0; form.Show();
                form.SetPreviewRates(samples);
                form.SetPreviewProcesses(previewProcesses,previewRates); form.ExpandGroup("C:\\Preview\\Steam.exe");
                for(int i=0;i<60;i++) { double wave=0.55+Math.Sin(i*.29)*.13+Math.Sin(i*.83)*.05; form.AddPreviewSample(12582912*wave,2097152*(.5+Math.Cos(i*.37)*.14)); }
                System.Windows.Forms.Application.DoEvents();
                using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height)); bitmap.Save(output); }
                form.Size=form.MinimumSize; System.Windows.Forms.Application.DoEvents();
                using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileNameWithoutExtension(output)+"-compact.png")); }
                form.Close();
            }
        } catch(Exception ex) { File.WriteAllText(output+".error.txt",ex.ToString()); Environment.ExitCode=1; }
    }
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
                var layout=form.Controls.OfType<System.Windows.Forms.TableLayoutPanel>().Single();
                var grid=layout.Controls.OfType<System.Windows.Forms.DataGridView>().Single();
                Check(grid.SelectedRows.Count==0,"No implicit termination target");
                grid.Rows[0].Selected=true;
                string selected=((TrafficRow)grid.SelectedRows[0].Tag).Key;
                store.Add("download.exe","C:\\download.exe",104857600,false,DateTime.Now);
                form.Render();
                Check(grid.SelectedRows.Count==1 && ((TrafficRow)grid.SelectedRows[0].Tag).Key==selected,"Selection identity survives live reorder");
                var identities=new System.Collections.Generic.List<ProcessIdentity> {
                    new ProcessIdentity {Id=501,StartedUtcTicks=1001,Name="browser.exe",Path="C:\\browser.exe"},
                    new ProcessIdentity {Id=502,StartedUtcTicks=1002,Name="browser.exe",Path="C:\\browser.exe"}
                };
                store.Add("browser.exe","C:\\browser.exe",110,false,DateTime.Now,501,1001);
                store.Add("browser.exe","C:\\browser.exe",220,true,DateTime.Now,502,1002);
                form.SetPreviewProcesses(identities,new System.Collections.Generic.Dictionary<string,Rate>()); form.ExpandGroup("C:\\browser.exe");
                var firstChild=grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(x=>!((TrafficRow)x.Tag).IsGroup && ((TrafficRow)x.Tag).Process.Id==501);
                var secondChild=grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(x=>!((TrafficRow)x.Tag).IsGroup && ((TrafficRow)x.Tag).Process.Id==502);
                Check(Convert.ToInt64(firstChild.Cells[4].Value)==110 && Convert.ToInt64(secondChild.Cells[5].Value)==220,"Per-process daily totals do not include siblings or legacy totals");
                var parentRow=grid.Rows.Cast<System.Windows.Forms.DataGridViewRow>().Single(x=>((TrafficRow)x.Tag).IsGroup && ((TrafficRow)x.Tag).Usage.Path=="C:\\browser.exe");
                Check(firstChild.Cells[0].Style.Padding.Left>parentRow.Cells[0].Style.Padding.Left && firstChild.Index>parentRow.Index && secondChild.Index>parentRow.Index,"Child rows stay under parent and are indented");
                grid.ClearSelection(); firstChild.Selected=true;
                form.Render(); Check(((TrafficRow)grid.SelectedRows[0].Tag).Process.Id==501,"Child selection remains on PID");
                identities[0]=new ProcessIdentity {Id=501,StartedUtcTicks=9999,Name="browser.exe",Path="C:\\browser.exe"};
                form.SetPreviewProcesses(identities,new System.Collections.Generic.Dictionary<string,Rate>());
                Check(grid.SelectedRows.Count==0,"PID reuse clears stale selection");
                form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));
                bitmap.Save(Path.Combine(folder,"ui-smoke.png"));
                form.Close();
            }
            var persisted=new TrafficStore(file).Snapshot();
            Check(persisted.Single(x=>x.Pid==501).Download==110 && persisted.Single(x=>x.Pid==502).Upload==220,"Per-process identity and totals persist");
            using(var current=System.Diagnostics.Process.GetCurrentProcess()) Check(ProcessTarget.Open(current,System.Windows.Forms.Application.ExecutablePath)==null,"Self-termination blocked");
            using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--termination-test-child") {UseShellExecute=false,CreateNoWindow=true}))
            using(var sibling=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--termination-test-child") {UseShellExecute=false,CreateNoWindow=true})) {
                try {
                    Check(ProcessTarget.Open(child,"C:\\not-the-selected-app.exe")==null,"Mismatched executable rejected");
                    var identity=ProcessIdentity.Read(child);
                    var selectedRow=new TrafficRow {Process=identity,Usage=new Usage {Name=identity.Name,Path=identity.Path}};
                    var resolved=ProcessTarget.Resolve(selectedRow);
                    Check(resolved.Count==1 && resolved[0].Id==child.Id,"Single-process action resolves only selected PID");
                    using(var target=resolved[0]) {
                        Check(target!=null,"Owned test child is eligible");
                        Check(target.End(),"Terminate test child");
                        Check(child.WaitForExit(5000),"Target process exited");
                        Check(!target.End(),"Already exited target handled");
                        Check(!sibling.HasExited,"Sibling process remains running");
                    }
                    identity=ProcessIdentity.Read(sibling); identity.StartedUtcTicks++;
                    Check(ProcessTarget.Resolve(new TrafficRow {Process=identity,Usage=new Usage {Name=identity.Name,Path=identity.Path}}).Count==0,"Changed start time rejects reused PID");
                } finally { if(!child.HasExited) { child.Kill(); child.WaitForExit(5000); } if(!sibling.HasExited) { sibling.Kill(); sibling.WaitForExit(5000); } }
            }
            File.WriteAllText(output,"PASS: accounting, persistence, backup recovery, UI rendering, process hierarchy and per-process totals, stable child selection, PID reuse protection, self-protection, real single-process termination, sibling survival, already-exited handling.\r\nTest data: "+folder);
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
