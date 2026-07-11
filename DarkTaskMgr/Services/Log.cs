using System;
using System.IO;
using System.Text;

namespace DarkTaskMgr.Services
{
    /// <summary>
    /// A plain rolling file logger under %APPDATA%\DarkTaskMgr, never the Windows Event Log. Every
    /// call is guarded so logging can never crash the app, because a monitor that dies while
    /// reporting a problem is worse than a lost log line.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        private const long MaxBytes = 512 * 1024;

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkTaskMgr");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "debug.log");
                Roll();
                Write("INFO", "----- session start -----");
            }
            catch { _path = null; }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }

        public static void Error(string msg, Exception ex = null)
        {
            Write("ERR ", ex == null ? msg : msg + " :: " + ex.GetType().Name + ": " + ex.Message);
        }

        private static void Roll()
        {
            try
            {
                if (_path != null && File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    string bak = _path + ".1";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(_path, bak);
                }
            }
            catch { }
        }

        private static void Write(string level, string msg)
        {
            if (_path == null) return;
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + level + "  " + msg + Environment.NewLine;
                lock (Gate) File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch { }
        }
    }
}
