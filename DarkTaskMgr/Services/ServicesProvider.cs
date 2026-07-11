using System;
using System.Collections.Generic;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;

namespace DarkTaskMgr.Services
{
    public sealed class ServiceInfo
    {
        public string Name;
        public string DisplayName;
        public int Pid;          // 0 when not running
        public string Status;    // Running / Stopped
        public string Description;
        public string Group;
    }

    /// <summary>
    /// Lists Windows services through WMI, which is the one source that carries the process id, state,
    /// and description together, and reads each service's load-order group from its registry key
    /// because WMI does not expose it. Starting and stopping go through the service control manager,
    /// which needs administrator rights, so those calls surface a clear result to the caller.
    /// </summary>
    public sealed class ServicesProvider
    {
        public List<ServiceInfo> Enumerate()
        {
            var list = new List<ServiceInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DisplayName, ProcessId, State, Description FROM Win32_Service"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        try
                        {
                            string name = Convert.ToString(mo["Name"]);
                            int pid = 0;
                            try { pid = Convert.ToInt32(mo["ProcessId"]); } catch { }
                            string state = Convert.ToString(mo["State"]);
                            list.Add(new ServiceInfo
                            {
                                Name = name,
                                DisplayName = Convert.ToString(mo["DisplayName"]),
                                Pid = pid,
                                Status = NormalizeState(state),
                                Description = Convert.ToString(mo["Description"]),
                                Group = ReadGroup(name)
                            });
                        }
                        catch (Exception ex) { Log.Error("service row", ex); }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex) { Log.Error("enumerate services", ex); }
            return list;
        }

        private static string NormalizeState(string state)
        {
            if (string.IsNullOrEmpty(state)) return "";
            if (state.Equals("Running", StringComparison.OrdinalIgnoreCase)) return "Running";
            if (state.Equals("Stopped", StringComparison.OrdinalIgnoreCase)) return "Stopped";
            return state;
        }

        private static string ReadGroup(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    return Convert.ToString(key?.GetValue("Group")) ?? "";
                }
            }
            catch { return ""; }
        }

        /// <summary>Start or stop a service and wait briefly for the transition. Returns null on
        /// success, otherwise a short message explaining why it did not happen.</summary>
        public string SetRunning(string serviceName, bool run)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (run)
                    {
                        if (sc.Status == ServiceControllerStatus.Running) return null;
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                    else
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped) return null;
                        if (!sc.CanStop) return "This service does not allow stopping.";
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                return null;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return "Timed out waiting for the service to change state.";
            }
            catch (InvalidOperationException ex)
            {
                Log.Error("service control", ex);
                return "Access denied. Restart DarkTaskMgr as administrator to control services.";
            }
            catch (Exception ex)
            {
                Log.Error("service control", ex);
                return ex.Message;
            }
        }
    }
}
