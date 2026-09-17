using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SuperiorOneNet {
public sealed class Usage {
    public string Day {get;set;} public string Name {get;set;} public string Path {get;set;}
    public long Download {get;set;} public long Upload {get;set;}
    public int Pid {get;set;} public long StartedUtcTicks {get;set;}
}
sealed class Rate { public string Name, Path; public long Down, Up; }
sealed class TrafficStore {
    readonly object gate=new object();
    readonly Dictionary<string,Usage> history=new Dictionary<string,Usage>();
    Dictionary<string,Rate> pending=new Dictionary<string,Rate>();
    Dictionary<string,Rate> pendingProcesses=new Dictionary<string,Rate>();
    readonly Dictionary<int,Tuple<ProcessIdentity,DateTime>> processes=new Dictionary<int,Tuple<ProcessIdentity,DateTime>>();
    readonly string file;
    public string Warning="";
    public TrafficStore(string location) { file=location; Load(); }
    void Load() {
        if(!File.Exists(file)) return;
        try { Read(file); }
        catch(Exception ex) {
            Warning="历史记录读取失败："+ex.Message;
            if(File.Exists(file+".bak")) try { Read(file+".bak"); Warning="已从备份恢复历史记录。"; } catch { }
            // Preserve damaged content before any future save.
            File.Copy(file,file+".damaged-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);
        }
    }
    void Read(string path) {
        var rows=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<List<Usage>>(File.ReadAllText(path));
        if(rows==null || rows.Any(x=>x==null || x.Day==null || x.Path==null || x.Download<0 || x.Upload<0)) throw new InvalidDataException("记录格式无效");
        foreach(var row in rows) history[Key(row.Day,row.Path,row.Pid,row.StartedUtcTicks)]=row;
    }
    static string Key(string day,string path,int pid,long started) { return day+"|"+path+"|"+pid+"|"+started; }
    public void Record(int pid,long size,bool upload,DateTime at) {
        lock(gate) {
            Tuple<ProcessIdentity,DateTime> info;
            if(!processes.TryGetValue(pid,out info) || (DateTime.UtcNow-info.Item2).TotalSeconds>2) {
                var identity=new ProcessIdentity {Id=pid,Name="未识别进程 (PID "+pid+")",Path="PID:"+pid};
                try { using(var p=Process.GetProcessById(pid)) identity=ProcessIdentity.Read(p); } catch { }
                info=Tuple.Create(identity,DateTime.UtcNow); processes[pid]=info;
                if(processes.Count>8192) processes.Clear();
            }
            var current=info.Item1;
            // A delayed event from a previous PID owner must not be charged to a new process.
            if(current.StartedUtcTicks>at.ToUniversalTime().Ticks) current=new ProcessIdentity {Id=pid,Name="已退出进程 (PID "+pid+")",Path="PID:"+pid};
            Add(current.Name,current.Path,size,upload,at,current.Id,current.StartedUtcTicks);
        }
    }
    internal void Add(string name,string path,long size,bool upload,DateTime at,int pid=0,long startedUtcTicks=0) {
        lock(gate) {
            string day=at.ToString("yyyy-MM-dd"), key=Key(day,path,pid,startedUtcTicks);
            Usage row;
            if(!history.TryGetValue(key,out row)) history[key]=row=new Usage {Day=day,Name=name,Path=path,Pid=pid,StartedUtcTicks=startedUtcTicks};
            if(upload) row.Upload+=size; else row.Download+=size;
            Rate rate;
            if(!pending.TryGetValue(path,out rate)) pending[path]=rate=new Rate {Name=name,Path=path};
            if(upload) rate.Up+=size; else rate.Down+=size;
            if(pid>0) {
                string processKey=path+"|"+pid+"|"+startedUtcTicks;
                if(!pendingProcesses.TryGetValue(processKey,out rate)) pendingProcesses[processKey]=rate=new Rate {Name=name,Path=path};
                if(upload) rate.Up+=size; else rate.Down+=size;
            }
        }
    }
    public List<Usage> Snapshot() { lock(gate) return history.Values.Select(x=>new Usage {Day=x.Day,Name=x.Name,Path=x.Path,Download=x.Download,Upload=x.Upload,Pid=x.Pid,StartedUtcTicks=x.StartedUtcTicks}).ToList(); }
    public Dictionary<string,Rate> TakeRates() { lock(gate) { var r=pending; pending=new Dictionary<string,Rate>(); return r; } }
    public Dictionary<string,Rate> TakeRates(out Dictionary<string,Rate> perProcess) { lock(gate) { perProcess=pendingProcesses; pendingProcesses=new Dictionary<string,Rate>(); return TakeRates(); } }
    public void Save() {
        var json=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(Snapshot());
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        File.WriteAllText(file+".tmp",json);
        if(File.Exists(file)) File.Replace(file+".tmp",file,file+".bak"); else File.Move(file+".tmp",file);
    }
}
}
