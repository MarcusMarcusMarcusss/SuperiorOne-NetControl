using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SuperiorOneNet {
// Hold the original process handle throughout confirmation to prevent PID reuse.
sealed class ProcessTarget : IDisposable {
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool IsProcessCritical(IntPtr process, out bool critical);
    readonly Process process;
    static readonly int CurrentId=GetCurrentId();
    static int GetCurrentId() { using(var current=Process.GetCurrentProcess()) return current.Id; }
    public int Id { get { return process.Id; } }
    ProcessTarget(Process value) { process=value; }
    public static ProcessTarget Open(Process process, string expectedPath) {
        if(process.Id<=4 || process.Id==CurrentId) return null;
        if(!Path.IsPathRooted(expectedPath)) return null;
        try {
            // Access the handle before inspecting identity and retain it until disposal.
            IntPtr handle=process.Handle;
            if(process.HasExited || !string.Equals(process.MainModule.FileName,expectedPath,StringComparison.OrdinalIgnoreCase)) return null;
            bool critical;
            if(!IsProcessCritical(handle,out critical) || critical) return null;
            return new ProcessTarget(process);
        } catch { return null; }
    }
    public bool End() {
        if(process.HasExited) return false;
        bool critical;
        if(!IsProcessCritical(process.Handle,out critical) || critical) throw new InvalidOperationException("系统关键进程不能结束。");
        process.Kill(); return true;
    }
    public void Dispose() { process.Dispose(); }
    public static List<ProcessTarget> Find(string path) {
        var targets=new List<ProcessTarget>();
        foreach(var process in Process.GetProcesses()) {
            var target=Open(process,path);
            if(target==null) process.Dispose(); else targets.Add(target);
        }
        return targets;
    }
}
}
