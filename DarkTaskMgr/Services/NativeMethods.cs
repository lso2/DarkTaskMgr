using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DarkTaskMgr.Services
{
    /// <summary>
    /// The thin layer of Win32 the managed classes do not expose: physical memory totals, per-process
    /// I/O byte counters (the basis of the Disk column, because .NET's Process type does not surface
    /// them), and the owning account of a process. Every entry point is wrapped by a guarded helper
    /// so a denied or exited process degrades to a neutral value instead of throwing.
    /// </summary>
    internal static class NativeMethods
    {
        // ---- Memory ----------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static bool ReadMemory(out double memoryLoadPct, out ulong totalPhys, out ulong availPhys)
        {
            memoryLoadPct = 0; totalPhys = 0; availPhys = 0;
            try
            {
                var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (!GlobalMemoryStatusEx(ref m)) return false;
                memoryLoadPct = m.dwMemoryLoad;
                totalPhys = m.ullTotalPhys;
                availPhys = m.ullAvailPhys;
                return true;
            }
            catch (Exception ex) { Log.Error("GlobalMemoryStatusEx", ex); return false; }
        }

        // ---- Per-process I/O -------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
            IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

        /// <summary>Read + write transfer bytes since process start, or -1 when access is denied.</summary>
        public static long TryGetIoTransferBytes(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return -1;
            try
            {
                if (GetProcessIoCounters(h, out IO_COUNTERS c))
                    return (long)(c.ReadTransferCount + c.WriteTransferCount);
                return -1;
            }
            catch { return -1; }
            finally { CloseHandle(h); }
        }

        /// <summary>Full image path of a process, resolved through the kernel so it works across the
        /// 32 and 64 bit boundary that defeats the managed MainModule read. Empty when the process is
        /// protected and cannot be opened even for limited query.</summary>
        public static string TryGetProcessImagePath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }

        private const int TokenUser = 1;

        /// <summary>Owning account of a process as a short name, or "" when it cannot be read.</summary>
        public static string TryGetProcessUserName(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return "";
            IntPtr tok = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(h, TOKEN_QUERY, out tok)) return "";
                GetTokenInformation(tok, TokenUser, IntPtr.Zero, 0, out int len);
                if (len <= 0) return "";
                buf = Marshal.AllocHGlobal(len);
                if (!GetTokenInformation(tok, TokenUser, buf, len, out len)) return "";
                IntPtr pSid = Marshal.ReadIntPtr(buf); // TOKEN_USER begins with SID_AND_ATTRIBUTES.Sid
                if (pSid == IntPtr.Zero) return "";
                var sid = new SecurityIdentifier(pSid);
                string full = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
                int slash = full.LastIndexOf('\\');
                return slash >= 0 ? full.Substring(slash + 1) : full;
            }
            catch { return ""; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (tok != IntPtr.Zero) CloseHandle(tok);
                CloseHandle(h);
            }
        }
    }
}
