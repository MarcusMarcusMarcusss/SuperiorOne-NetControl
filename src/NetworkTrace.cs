using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace SuperiorOneNet {
// Native layouts below are the Windows x64 ETW ABI. The build deliberately targets x64.
sealed class NetworkTrace : IDisposable {
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)] static extern uint StartTraceW(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint control);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern ulong OpenTraceW(IntPtr logfile);
    [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void RecordCallback(IntPtr record);
    readonly string name = "SuperiorOneNet.Network";
    readonly RecordCallback callback;
    readonly Action<int,long,bool,DateTime> receive;
    ulong session, consumer = ulong.MaxValue;
    IntPtr props, logfile, loggerName;
    Thread worker;
    volatile bool stopping;
    public string Error;
    public string LastUdp="No UDP event received";
    public uint LostEvents { get; private set; }
    static readonly Guid Tcp = new Guid("9a280ac0-c8e0-11d1-84e2-00c04fb998a2");
    static readonly Guid Udp = new Guid("bf3a50c5-a9c9-4988-a005-2df0b7c80f80");
    public NetworkTrace(Action<int,long,bool,DateTime> action) { receive=action; callback=OnRecord; }
    static IntPtr Zero(int size) { var p=Marshal.AllocHGlobal(size); Marshal.Copy(new byte[size],0,p,size); return p; }
    public void Start() {
        props=Zero(2048);
        Marshal.WriteInt32(props,0,2048);
        Marshal.StructureToPtr(new Guid("f08ef433-65d5-46a4-a5d7-32b2151afe91"),IntPtr.Add(props,24),false);
        Marshal.WriteInt32(props,40,1); // performance counter clock
        Marshal.WriteInt32(props,44,0x20000); // WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(props,48,64);
        Marshal.WriteInt32(props,52,32);
        Marshal.WriteInt32(props,56,256);
        Marshal.WriteInt32(props,64,0x02000100); // system logger + real time
        Marshal.WriteInt32(props,68,1);
        Marshal.WriteInt32(props,72,0x00010000); // TCP/IP (includes UDP)
        Marshal.WriteInt32(props,116,120);
        uint result=StartTraceW(out session,name,props);
        if(result==183) { // Recover only our private session after an unclean exit.
            ControlTraceW(0,name,props,1);
            result=StartTraceW(out session,name,props);
        }
        if(result!=0) { session=0; throw new Win32Exception((int)result,"无法启动网络监控："+new Win32Exception((int)result).Message); }
        logfile=Zero(448); loggerName=Marshal.StringToHGlobalUni(name);
        Marshal.WriteIntPtr(logfile,8,loggerName);
        Marshal.WriteInt32(logfile,28,0x10000100); // event record + real time
        Marshal.WriteIntPtr(logfile,424,Marshal.GetFunctionPointerForDelegate(callback));
        consumer=OpenTraceW(logfile);
        if(consumer==ulong.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        ulong openedConsumer=consumer;
        worker=new Thread(delegate() { uint code=ProcessTrace(new[]{openedConsumer},1,IntPtr.Zero,IntPtr.Zero); if(!stopping) Error="监控已中断（"+code+"），请重新启动程序。"; });
        worker.IsBackground=true; worker.Start();
    }
    void OnRecord(IntPtr record) {
        try {
            Guid provider=(Guid)Marshal.PtrToStructure(IntPtr.Add(record,24),typeof(Guid));
            byte opcode=Marshal.ReadByte(record,45), version=Marshal.ReadByte(record,42);
            if(provider==Udp) LastUdp="UDP opcode="+opcode+", version="+version;
            if(provider!=Tcp && provider!=Udp) return;
            // Version 0 has a different payload; supported Windows versions emit v1/v2.
            if(version==0 || (opcode!=10 && opcode!=11 && opcode!=26 && opcode!=27)) return;
            if((ushort)Marshal.ReadInt16(record,86)<8) return;
            IntPtr data=Marshal.ReadIntPtr(record,96);
            int pid=Marshal.ReadInt32(data);
            long size=(uint)Marshal.ReadInt32(data,4);
            DateTime at=DateTime.FromFileTimeUtc(Marshal.ReadInt64(record,16)).ToLocalTime();
            receive(pid,size,opcode==10 || opcode==26,at);
        } catch(Exception ex) { Error="部分网络事件处理失败："+ex.Message; }
    }
    public void QueryLoss() {
        if(session!=0 && ControlTraceW(session,name,props,0)==0)
            LostEvents=(uint)Marshal.ReadInt32(props,88)+(uint)Marshal.ReadInt32(props,100);
    }
    public void Dispose() {
        stopping=true;
        if(session!=0) { ControlTraceW(session,name,props,1); session=0; }
        if(consumer!=ulong.MaxValue) { CloseTrace(consumer); consumer=ulong.MaxValue; }
        if(worker!=null) worker.Join();
        if(props!=IntPtr.Zero) { Marshal.FreeHGlobal(props); props=IntPtr.Zero; }
        if(logfile!=IntPtr.Zero) { Marshal.FreeHGlobal(logfile); logfile=IntPtr.Zero; }
        if(loggerName!=IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName=IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}
}
