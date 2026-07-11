using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DarkTaskMgr.Services
{
    public enum StartupSource { HkcuRun, HklmRun, Hklm32Run, UserFolder, CommonFolder }

    public sealed class StartupEntry
    {
        public string Name;
        public string Publisher;
        public string Command;
        public string IconPath;       // resolved executable, used to load the icon
        public bool Enabled;
        public StartupSource Source;
        public string ApprovedName;   // value name used inside the StartupApproved key
        public string LocationLabel { get { return SourceLabel(Source); } }

        public static string SourceLabel(StartupSource s)
        {
            switch (s)
            {
                case StartupSource.HkcuRun: return "Registry (current user)";
                case StartupSource.HklmRun: return "Registry (all users)";
                case StartupSource.Hklm32Run: return "Registry (all users, 32-bit)";
                case StartupSource.UserFolder: return "Startup folder (current user)";
                case StartupSource.CommonFolder: return "Startup folder (all users)";
            }
            return "";
        }
    }

    /// <summary>
    /// Reads the programs that launch at sign-in from the four places Windows keeps them, the per-user
    /// and machine Run keys and the two Startup folders, and reports whether each is enabled by
    /// consulting the StartupApproved records that the Startup tab of Task Manager also writes.
    /// Enabling or disabling flips that same record, so the change is what Windows itself honors.
    /// </summary>
    public sealed class StartupProvider
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Run32Path = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        public List<StartupEntry> Enumerate()
        {
            var list = new List<StartupEntry>();
            ReadRun(list, Registry.CurrentUser, RunPath, StartupSource.HkcuRun);
            ReadRun(list, Registry.LocalMachine, RunPath, StartupSource.HklmRun);
            ReadRun(list, Registry.LocalMachine, Run32Path, StartupSource.Hklm32Run);
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserFolder);
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonFolder);
            return list;
        }

        private void ReadRun(List<StartupEntry> list, RegistryKey hive, string path, StartupSource src)
        {
            try
            {
                using (var key = hive.OpenSubKey(path, false))
                {
                    if (key == null) return;
                    foreach (var name in key.GetValueNames())
                    {
                        string cmd = Convert.ToString(key.GetValue(name)) ?? "";
                        string exe = ExeFromCommand(cmd);
                        list.Add(new StartupEntry
                        {
                            Name = name,
                            Command = cmd,
                            IconPath = exe,
                            ApprovedName = name,
                            Source = src,
                            Publisher = PublisherOf(exe),
                            Enabled = IsEnabled(src, name)
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Error("read run " + path, ex); }
        }

        private void ReadFolder(List<StartupEntry> list, string folder, StartupSource src)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (var file in Directory.GetFiles(folder))
                {
                    string fn = Path.GetFileName(file);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = fn.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(fn),
                        Command = target,
                        IconPath = target,
                        ApprovedName = fn,
                        Source = src,
                        Publisher = PublisherOf(target),
                        Enabled = IsEnabled(src, fn)
                    });
                }
            }
            catch (Exception ex) { Log.Error("read folder " + folder, ex); }
        }

        private bool IsEnabled(StartupSource src, string name)
        {
            try
            {
                RegistryKey hive; string sub;
                ApprovedLocation(src, out hive, out sub);
                using (var key = hive.OpenSubKey(sub, false))
                {
                    var data = key?.GetValue(name) as byte[];
                    if (data == null || data.Length == 0) return true; // no record means enabled
                    return (data[0] & 1) == 0;                          // even = enabled, odd = disabled
                }
            }
            catch { return true; }
        }

        /// <summary>Flip a startup entry on or off by writing the StartupApproved record Windows reads.
        /// Machine-wide entries need administrator rights, so this returns false when the write is
        /// denied rather than pretending it succeeded.</summary>
        public bool SetEnabled(StartupEntry e, bool enable)
        {
            try
            {
                RegistryKey hive; string sub;
                ApprovedLocation(e.Source, out hive, out sub);
                using (var key = hive.CreateSubKey(sub))
                {
                    if (key == null) return false;
                    byte[] data = new byte[12];
                    data[0] = (byte)(enable ? 2 : 3);
                    if (!enable)
                    {
                        long ft = DateTime.UtcNow.ToFileTimeUtc();
                        BitConverter.GetBytes(ft).CopyTo(data, 4);
                    }
                    key.SetValue(e.ApprovedName, data, RegistryValueKind.Binary);
                }
                e.Enabled = enable;
                return true;
            }
            catch (Exception ex) { Log.Error("set startup enabled", ex); return false; }
        }

        private static void ApprovedLocation(StartupSource src, out RegistryKey hive, out string sub)
        {
            switch (src)
            {
                case StartupSource.HklmRun: hive = Registry.LocalMachine; sub = ApprovedRun; break;
                case StartupSource.Hklm32Run: hive = Registry.LocalMachine; sub = ApprovedRun32; break;
                case StartupSource.CommonFolder: hive = Registry.LocalMachine; sub = ApprovedFolder; break;
                case StartupSource.UserFolder: hive = Registry.CurrentUser; sub = ApprovedFolder; break;
                default: hive = Registry.CurrentUser; sub = ApprovedRun; break;
            }
        }

        private static string ExeFromCommand(string cmd)
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

        private static string PublisherOf(string exePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    return fvi.CompanyName ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string ResolveShortcut(string lnkPath)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return lnkPath;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnkPath);
                string target = sc.TargetPath;
                return string.IsNullOrEmpty(target) ? lnkPath : target;
            }
            catch { return lnkPath; }
        }
    }
}
