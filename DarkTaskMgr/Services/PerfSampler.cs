using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace DarkTaskMgr.Services
{
    /// <summary>One system-wide reading: the numbers behind the column-header percentages and the
    /// Performance tab graphs.</summary>
    public sealed class SystemStats
    {
        public double CpuPct;
        public double CpuGhz;        // effective clock
        public double CpuBaseGhz;

        public double MemUsedPct;
        public ulong MemUsedBytes;
        public ulong MemTotalBytes;
        public ulong CommitUsedBytes;
        public ulong CommitLimitBytes;

        public double DiskActivePct;
        public double DiskReadBps;
        public double DiskWriteBps;

        public double NetUtilPct;
        public double NetSentBps;
        public double NetRecvBps;

        public double UptimeSeconds;
    }

    /// <summary>
    /// Wraps the Windows performance counters into one cheap Read(). Every counter is created inside a
    /// guard, so a machine that is missing a counter set reports zero for that metric rather than
    /// failing to start. Rate counters are primed once so the first real read spans a full interval.
    /// </summary>
    public sealed class PerfSampler : IDisposable
    {
        private PerformanceCounter _cpuTime, _cpuPerf, _uptime;
        private PerformanceCounter _diskTime, _diskRead, _diskWrite;
        private PerformanceCounter _commitBytes, _commitLimit;

        private sealed class Nic
        {
            public PerformanceCounter Sent;
            public PerformanceCounter Recv;
            public PerformanceCounter Bandwidth;
        }
        private readonly List<Nic> _nics = new List<Nic>();

        public double BaseGhz { get; private set; }

        public PerfSampler()
        {
            BaseGhz = ReadBaseMhz() / 1000.0;

            _cpuTime = Try("Processor Information", "% Processor Time", "_Total")
                       ?? Try("Processor", "% Processor Time", "_Total");
            _cpuPerf = Try("Processor Information", "% Processor Performance", "_Total");
            _uptime = Try("System", "System Up Time", null);

            _diskTime = Try("PhysicalDisk", "% Disk Time", "_Total");
            _diskRead = Try("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWrite = Try("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

            _commitBytes = Try("Memory", "Committed Bytes", null);
            _commitLimit = Try("Memory", "Commit Limit", null);

            BuildNics();
            Read(); // prime rate counters
        }

        private void BuildNics()
        {
            try
            {
                var cat = new PerformanceCounterCategory("Network Interface");
                foreach (var name in cat.GetInstanceNames())
                {
                    string low = name.ToLowerInvariant();
                    if (low.Contains("loopback") || low.Contains("isatap") || low.Contains("teredo")) continue;
                    var nic = new Nic
                    {
                        Sent = Try("Network Interface", "Bytes Sent/sec", name),
                        Recv = Try("Network Interface", "Bytes Received/sec", name),
                        Bandwidth = Try("Network Interface", "Current Bandwidth", name)
                    };
                    if (nic.Sent != null || nic.Recv != null) _nics.Add(nic);
                }
            }
            catch (Exception ex) { Log.Error("enumerate NICs", ex); }
        }

        private static PerformanceCounter Try(string cat, string name, string instance)
        {
            try
            {
                var c = instance == null
                    ? new PerformanceCounter(cat, name, true)
                    : new PerformanceCounter(cat, name, instance, true);
                c.NextValue();
                return c;
            }
            catch (Exception ex) { Log.Error("counter " + cat + "\\" + name, ex); return null; }
        }

        public SystemStats Read()
        {
            var s = new SystemStats();
            s.CpuBaseGhz = BaseGhz;
            s.CpuPct = Clamp(Next(_cpuTime), 0, 100);
            double perf = _cpuPerf != null ? Math.Max(0, Next(_cpuPerf)) : 100.0;
            s.CpuGhz = BaseGhz > 0 ? BaseGhz * perf / 100.0 : 0;
            s.UptimeSeconds = Math.Max(0, Next(_uptime));

            if (NativeMethods.ReadMemory(out double load, out ulong total, out ulong avail))
            {
                s.MemUsedPct = load;
                s.MemTotalBytes = total;
                s.MemUsedBytes = total > avail ? total - avail : 0;
            }
            s.CommitUsedBytes = (ulong)Math.Max(0, Next(_commitBytes));
            s.CommitLimitBytes = (ulong)Math.Max(0, Next(_commitLimit));

            s.DiskActivePct = Clamp(Next(_diskTime), 0, 100);
            s.DiskReadBps = Math.Max(0, Next(_diskRead));
            s.DiskWriteBps = Math.Max(0, Next(_diskWrite));

            ReadNetwork(s);
            return s;
        }

        private void ReadNetwork(SystemStats s)
        {
            double sent = 0, recv = 0, bestUtil = 0;
            foreach (var nic in _nics)
            {
                double sBps = Next(nic.Sent);
                double rBps = Next(nic.Recv);
                sent += sBps;
                recv += rBps;
                double bw = Next(nic.Bandwidth); // bits/sec
                if (bw > 0)
                {
                    double util = (Math.Max(sBps, rBps) * 8.0) / bw * 100.0;
                    if (util > bestUtil) bestUtil = util;
                }
            }
            s.NetSentBps = sent;
            s.NetRecvBps = recv;
            s.NetUtilPct = Clamp(bestUtil, 0, 100);
        }

        private static float Next(PerformanceCounter c)
        {
            if (c == null) return 0f;
            try { return c.NextValue(); }
            catch { return 0f; }
        }

        private static int ReadBaseMhz()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                {
                    if (key?.GetValue("~MHz") is int mhz && mhz > 0) return mhz;
                }
            }
            catch (Exception ex) { Log.Error("base mhz registry", ex); }
            return 0;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || v < lo) return lo;
            return v > hi ? hi : v;
        }

        public void Dispose()
        {
            var all = new List<PerformanceCounter> { _cpuTime, _cpuPerf, _uptime, _diskTime, _diskRead, _diskWrite, _commitBytes, _commitLimit };
            foreach (var n in _nics) { all.Add(n.Sent); all.Add(n.Recv); all.Add(n.Bandwidth); }
            foreach (var c in all) try { c?.Dispose(); } catch { }
        }
    }
}
