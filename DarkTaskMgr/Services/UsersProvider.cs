using System;
using System.Collections.Generic;
using System.Linq;

namespace DarkTaskMgr.Services
{
    public sealed class UserSession
    {
        public int SessionId;
        public string UserName;
        public string Status;
        public int ProcessCount;
        public double CpuPct;
        public long MemBytes;
        public double DiskBps;
        public List<ProcInfo> Processes = new List<ProcInfo>();
    }

    /// <summary>
    /// Groups the current process snapshot into the sessions signed in to the machine, so the Users
    /// tab can show each account with the load it is responsible for. Session 0 holds the service
    /// host processes and is labeled as the system session, while an interactive sign-in gets the
    /// human account name taken from the processes running under it.
    /// </summary>
    public static class UsersProvider
    {
        public static List<UserSession> Build(IEnumerable<ProcInfo> processes)
        {
            var map = new Dictionary<int, UserSession>();
            foreach (var p in processes)
            {
                if (!map.TryGetValue(p.SessionId, out var s))
                {
                    s = new UserSession { SessionId = p.SessionId, Status = "Active" };
                    map[p.SessionId] = s;
                }
                s.Processes.Add(p);
                s.ProcessCount++;
                s.CpuPct += p.CpuPct;
                s.MemBytes += p.WorkingSetBytes;
                s.DiskBps += p.DiskBps;
            }

            foreach (var s in map.Values)
                s.UserName = NameForSession(s);

            return map.Values.OrderByDescending(s => s.SessionId == 0 ? -1 : s.CpuPct).ToList();
        }

        private static string NameForSession(UserSession s)
        {
            if (s.SessionId == 0) return "System (services)";

            var human = s.Processes
                .Select(p => p.UserName)
                .Where(u => !string.IsNullOrEmpty(u)
                            && u != "SYSTEM" && u != "LOCAL SERVICE" && u != "NETWORK SERVICE")
                .GroupBy(u => u)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return human != null ? human.Key : ("Session " + s.SessionId);
        }
    }
}
