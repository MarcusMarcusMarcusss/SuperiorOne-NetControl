using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SuperiorOneNet {
sealed class ProcessIdentity {
    public int Id;
    public long StartedUtcTicks;
    public string Name, Path;
    public string Key { get { return Path+"|"+Id+"|"+StartedUtcTicks; } }
    public static ProcessIdentity Read(Process process) {
        var result=new ProcessIdentity {Id=process.Id,Name="未识别进程 (PID "+process.Id+")",Path="PID:"+process.Id};
        try { result.Name=process.ProcessName+".exe"; result.Path=result.Name; } catch { }
        try { result.StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks; } catch { }
        try { result.Path=process.MainModule.FileName; } catch { }
        return result;
    }
}
sealed class ProcessCatalog : IDisposable {
    readonly object gate=new object();
    List<ProcessIdentity> snapshot=new List<ProcessIdentity>();
    int refreshing;
    volatile bool stopped;
    public List<ProcessIdentity> Snapshot() { lock(gate) return snapshot.ToList(); }
    public void Refresh() {
        if(stopped || Interlocked.CompareExchange(ref refreshing,1,0)!=0) return;
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                var items=new List<ProcessIdentity>();
                foreach(var process in Process.GetProcesses()) using(process) {
                    if(stopped) continue;
                    try { if(!process.HasExited) items.Add(ProcessIdentity.Read(process)); } catch { }
                }
                lock(gate) if(!stopped) snapshot=items;
            } catch { } finally { Interlocked.Exchange(ref refreshing,0); }
        });
    }
    public void Dispose() { stopped=true; }
}
sealed class TrafficRow {
    public Usage Usage;
    public ProcessIdentity Process;
    public bool IsGroup { get { return Process==null; } }
    public string Key { get { return IsGroup?"app|"+Usage.Path:"process|"+Process.Key; } }
}
}
