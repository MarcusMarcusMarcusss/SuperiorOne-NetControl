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
}
sealed class Rate { public string Name, Path; public long Down, Up; }
sealed class TrafficStore {
    readonly object gate=new object();
    readonly Dictionary<string,Usage> history=new Dictionary<string,Usage>();
    Dictionary<string,Rate> pending=new Dictionary<string,Rate>();
    readonly Dictionary<int,Tuple<string,string,DateTime>> processes=new Dictionary<int,Tuple<string,string,DateTime>>();
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
        foreach(var row in rows) history[row.Day+"|"+row.Path]=row;
    }
    public void Record(int pid,long size,bool upload,DateTime at) {
        lock(gate) {
            Tuple<string,string,DateTime> info;
            if(!processes.TryGetValue(pid,out info) || (DateTime.UtcNow-info.Item3).TotalSeconds>2) {
                string name="未识别进程 (PID "+pid+")", path="PID:"+pid;
                try { using(var p=Process.GetProcessById(pid)) { name=p.ProcessName+".exe"; path=name; try { path=p.MainModule.FileName; } catch { } } } catch { }
                info=Tuple.Create(name,path,DateTime.UtcNow); processes[pid]=info;
                if(processes.Count>8192) processes.Clear();
            }
            Add(info.Item1,info.Item2,size,upload,at);
        }
    }
    internal void Add(string name,string path,long size,bool upload,DateTime at) {
        lock(gate) {
            string day=at.ToString("yyyy-MM-dd"), key=day+"|"+path;
            Usage row;
            if(!history.TryGetValue(key,out row)) history[key]=row=new Usage {Day=day,Name=name,Path=path};
            if(upload) row.Upload+=size; else row.Download+=size;
            Rate rate;
            if(!pending.TryGetValue(path,out rate)) pending[path]=rate=new Rate {Name=name,Path=path};
            if(upload) rate.Up+=size; else rate.Down+=size;
        }
    }
    public List<Usage> Snapshot() { lock(gate) return history.Values.Select(x=>new Usage {Day=x.Day,Name=x.Name,Path=x.Path,Download=x.Download,Upload=x.Upload}).ToList(); }
    public Dictionary<string,Rate> TakeRates() { lock(gate) { var r=pending; pending=new Dictionary<string,Rate>(); return r; } }
    public void Save() {
        var json=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(Snapshot());
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        File.WriteAllText(file+".tmp",json);
        if(File.Exists(file)) File.Replace(file+".tmp",file,file+".bak"); else File.Move(file+".tmp",file);
    }
}
}
