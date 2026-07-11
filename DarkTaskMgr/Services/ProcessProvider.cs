using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace DarkTaskMgr.Services
{
    /// <summary>One process as measured this interval. Raw and per-process; the Processes tab groups
    /// these by application, while the Details and Users tabs show them individually.</summary>
    public sealed class ProcInfo
    {
        public int Pid;
        public string Name;          // process name, e.g. "chrome"
        public string Description;   // file description, e.g. "Google Chrome"
        public string ExePath;       // full image path, used to load the icon
        public string UserName;
        public int SessionId;
        public bool HasWindow;
        public string WindowTitle;
        public string Category;      // Apps / Background processes / Windows processes
        public bool Suspended;

        public double CpuPct;
        public long WorkingSetBytes;
        public double DiskBps;
        public double NetBps;        // per-process attribution needs ETW; reported as 0
        public int ThreadCount;
        public int HandleCount;

        public string DisplayName { get { return string.IsNullOrEmpty(Description) ? Name : Description; } }
    }

    /// <summary>
    /// Enumerates every process once a second and works out its CPU share, working set, and disk
    /// throughput. CPU share is the growth in total processor time across the interval over wall time
    /// times the logical core count, the same basis Task Manager uses. Owner and description are
    /// resolved once per process and cached, because translating a SID and reading version info are
    /// the costly parts and neither changes over a process's life.
    /// </summary>
    public sealed class ProcessProvider
    {
        private sealed class Prev { public TimeSpan Cpu; public long IoBytes; public DateTime Seen; }

        private readonly Dictionary<int, Prev> _prev = new Dictionary<int, Prev>();
        private readonly Dictionary<int, string> _userCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _descCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _pathCache = new Dictionary<int, string>();
        private readonly WmiImagePaths _wmi = new WmiImagePaths();
        private readonly int _logical;
        private DateTime _lastTime = DateTime.MinValue;

        public ProcessProvider()
        {
            _logical = Math.Max(1, Environment.ProcessorCount);
        }

        public List<ProcInfo> Sample()
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastTime).TotalSeconds;
            bool haveBaseline = _lastTime != DateTime.MinValue && elapsed >= 0.2;

            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch (Exception ex) { Log.Error("GetProcesses", ex); return new List<ProcInfo>(); }

            var list = new List<ProcInfo>(procs.Length);
            var live = new HashSet<int>();

            foreach (var p in procs)
            {
                int pid;
                try { pid = p.Id; } catch { continue; }
                if (pid == 0) { SafeDispose(p); continue; }
                live.Add(pid);

                try
                {
                    var info = new ProcInfo { Pid = pid, Name = SafeName(p) };

                    TimeSpan cpu = TimeSpan.Zero;
                    bool haveCpu = false;
                    try { cpu = p.TotalProcessorTime; haveCpu = true; } catch { }

                    long ioBytes = NativeMethods.TryGetIoTransferBytes(pid);

                    Prev prev;
                    bool hadPrev = _prev.TryGetValue(pid, out prev);
                    if (haveBaseline && hadPrev && haveCpu)
                    {
                        double dCpu = (cpu - prev.Cpu).TotalSeconds;
                        double pct = dCpu / (elapsed * _logical) * 100.0;
                        info.CpuPct = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                    }
                    if (haveBaseline && hadPrev && ioBytes >= 0 && prev.IoBytes >= 0)
                    {
                        double dio = ioBytes - prev.IoBytes;
                        if (dio > 0) info.DiskBps = dio / elapsed;
                    }
                    _prev[pid] = new Prev { Cpu = cpu, IoBytes = ioBytes, Seen = now };

                    try { info.WorkingSetBytes = p.WorkingSet64; } catch { }
                    try { info.ThreadCount = p.Threads.Count; } catch { }
                    try { info.HandleCount = p.HandleCount; } catch { }
                    try { info.SessionId = p.SessionId; } catch { }
                    try
                    {
                        IntPtr h = p.MainWindowHandle;
                        info.HasWindow = h != IntPtr.Zero;
                        if (info.HasWindow) info.WindowTitle = p.MainWindowTitle;
                    }
                    catch { }

                    info.UserName = ResolveUser(pid);
                    ResolveModule(pid, p, info);
                    info.Suspended = IsSuspended(p);
                    info.Category = Classify(info);

                    list.Add(info);
                }
                catch (Exception ex) { Log.Error("sample pid " + pid, ex); }
                finally { SafeDispose(p); }
            }

            Prune(live);
            _lastTime = now;
            return list;
        }

        private string ResolveUser(int pid)
        {
            if (_userCache.TryGetValue(pid, out string u)) return u;
            u = NativeMethods.TryGetProcessUserName(pid);
            _userCache[pid] = u;
            return u;
        }

        // Image path is resolved through the kernel first, because QueryFullProcessImageName works
        // across the 32 and 64 bit boundary and for far more processes than the managed MainModule
        // read, which is why so many rows were missing an icon before. The description reads the
        // file's version info from that path. Both are resolved once and cached because neither
        // changes over a process's life, and a protected process degrades to a generic icon.
        private void ResolveModule(int pid, Process p, ProcInfo info)
        {
            if (_pathCache.TryGetValue(pid, out string cp))
            {
                info.ExePath = cp;
                info.Description = _descCache.TryGetValue(pid, out string cd) ? cd : "";
                return;
            }
            string path = NativeMethods.TryGetProcessImagePath(pid);
            if (string.IsNullOrEmpty(path))
            {
                try { path = p.MainModule?.FileName ?? ""; } catch { path = ""; }
            }
            // SYSTEM-owned services cannot be opened by a normal user, so their path comes from WMI,
            // whose service reads it with its own privilege. This is what fills the icons for the
            // Intel, audio, and other service processes without needing elevation.
            if (string.IsNullOrEmpty(path)) path = _wmi.Get(pid);
            // Core Windows processes (winlogon, wininit, services, svchost, and the like) are higher
            // integrity than a normal-user app and cannot be opened at all, and WMI hides their path
            // too. They live in System32, so as a last resort the path is guessed from the process
            // name there, which recovers their icon without elevation.
            if (string.IsNullOrEmpty(path) && IsSystemish(info.UserName)) path = GuessSystemImage(info.Name);

            string desc = "";
            if (!string.IsNullOrEmpty(path))
            {
                try { desc = FileVersionInfo.GetVersionInfo(path).FileDescription ?? ""; } catch { desc = ""; }
            }
            info.ExePath = path;
            info.Description = desc;
            // Only a resolved path is cached; a still-empty one is retried next sample, because the
            // throttled WMI refresh may not have run yet on the first attempt.
            if (!string.IsNullOrEmpty(path)) { _pathCache[pid] = path; _descCache[pid] = desc; }
        }

        private static bool IsSuspended(Process p)
        {
            try
            {
                var threads = p.Threads;
                if (threads.Count == 0) return false;
                foreach (ProcessThread t in threads)
                {
                    try
                    {
                        if (t.ThreadState != System.Diagnostics.ThreadState.Wait) return false;
                        if (t.WaitReason != ThreadWaitReason.Suspended) return false;
                    }
                    catch { return false; }
                }
                return true;
            }
            catch { return false; }
        }

        private static string Classify(ProcInfo p)
        {
            if (p.HasWindow) return "Apps";
            string u = p.UserName ?? "";
            if (u == "SYSTEM" || u == "LOCAL SERVICE" || u == "NETWORK SERVICE" || u.Length == 0)
                return "Windows processes";
            return "Background processes";
        }

        private static bool IsSystemish(string user)
        {
            string u = user ?? "";
            return u.Length == 0 || u == "SYSTEM" || u == "LOCAL SERVICE" || u == "NETWORK SERVICE";
        }

        // A core system process's image is almost always System32\<name>.exe (or SysWOW64, or the
        // Windows directory), so this recovers a real file to read the icon from when every other
        // source was blocked by the process's integrity level.
        private static string GuessSystemImage(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "(unknown)") return "";
            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string wow = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                foreach (var dir in new[] { sys, wow, win })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string cand = Path.Combine(dir, name + ".exe");
                    if (File.Exists(cand)) return cand;
                }
            }
            catch { }
            return "";
        }

        private static string SafeName(Process p)
        {
            try { return p.ProcessName; } catch { return "(unknown)"; }
        }

        private static void SafeDispose(Process p) { try { p.Dispose(); } catch { } }

        private void Prune(HashSet<int> live)
        {
            if (_prev.Count > 4096) { _prev.Clear(); return; }
            var dead = new List<int>();
            foreach (var kv in _prev) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var pid in dead) { _prev.Remove(pid); _userCache.Remove(pid); _descCache.Remove(pid); _pathCache.Remove(pid); }
        }
    }

    /// <summary>
    /// A cached map of process id to executable path sourced from WMI, used when a process cannot be
    /// opened directly (SYSTEM-owned services). One batch query fills every process at once, the
    /// result is cached, and a refresh is throttled so a burst of unknown ids costs a single query.
    /// </summary>
    internal sealed class WmiImagePaths
    {
        private readonly object _gate = new object();
        private Dictionary<int, string> _map = new Dictionary<int, string>();
        private DateTime _lastRefresh = DateTime.MinValue;

        public string Get(int pid)
        {
            lock (_gate)
            {
                if (_map.TryGetValue(pid, out string p)) return p;
                if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 3) return "";
                Refresh();
                return _map.TryGetValue(pid, out p) ? p : "";
            }
        }

        private void Refresh()
        {
            _lastRefresh = DateTime.UtcNow;
            var map = new Dictionary<int, string>();

            // Running-process image paths. Readable for most processes, but null for higher-integrity
            // ones when the caller is not elevated.
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath FROM Win32_Process"))
                using (var results = searcher.Get())
                    foreach (ManagementObject mo in results)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            map[pid] = Convert.ToString(mo["ExecutablePath"]) ?? "";
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
            }
            catch (Exception ex) { Log.Error("wmi process paths", ex); }

            // Service image paths from the service configuration, which is registry-based and readable
            // without elevation. This fills the icons for third-party services (backup tools, remote
            // access, vendor helpers) whose running process cannot be opened directly.
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, PathName FROM Win32_Service"))
                using (var results = searcher.Get())
                    foreach (ManagementObject mo in results)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            if (pid <= 0) continue;
                            string exe = ExeFromServicePath(Convert.ToString(mo["PathName"]));
                            if (!string.IsNullOrEmpty(exe) && (!map.TryGetValue(pid, out string cur) || string.IsNullOrEmpty(cur)))
                                map[pid] = exe;
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
            }
            catch (Exception ex) { Log.Error("wmi service paths", ex); }

            _map = map;
        }

        private static string ExeFromServicePath(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1) return cmd.Substring(1, end - 1);
            }
            int ext = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (ext > 0) return cmd.Substring(0, ext + 4);
            int space = cmd.IndexOf(' ');
            return space > 0 ? cmd.Substring(0, space) : cmd;
        }
    }
}
