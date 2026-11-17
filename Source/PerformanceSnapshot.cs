using System;
using System.Diagnostics;

public sealed class PerformanceSnapshot
{
    public long Memory, Started;
    public double Cpu;
    public int Count, Root;
    public bool Ready;

    public static PerformanceSnapshot Capture(Process[] processes)
    {
        var snapshot = new PerformanceSnapshot();
        foreach (var process in processes)
        {
            try
            {
                long started = process.StartTime.ToUniversalTime().Ticks;
                snapshot.Memory += process.WorkingSet64;
                snapshot.Cpu += process.TotalProcessorTime.TotalMilliseconds;
                snapshot.Count++;
                if (snapshot.Root == 0 || started < snapshot.Started)
                {
                    snapshot.Root = process.Id;
                    snapshot.Started = started;
                    snapshot.Ready = process.MainWindowHandle != IntPtr.Zero;
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return snapshot;
    }
}
