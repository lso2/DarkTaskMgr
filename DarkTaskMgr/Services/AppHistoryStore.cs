using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DarkTaskMgr.Services
{
    public enum HistoryRange { Today, Week, Month, All }

    /// <summary>One recorded high-usage moment: an application crossing the spike threshold.</summary>
    public sealed class AppEvent
    {
        public DateTime Time;
        public double Pct;
        public string App;
    }

    /// <summary>An application's usage aggregated over a queried range.</summary>
    public sealed class AppSummary
    {
        public string App;
        public string IconPath = "";
        public double CpuSeconds;
        public double PeakPct;
        public DateTime PeakTime;
        public DateTime FirstSeen;
        public DateTime LastActive;
        public int EventCount;
        public readonly List<AppEvent> Events = new List<AppEvent>(); // recent first, capped
    }

    /// <summary>
    /// Persistent application-usage history under %APPDATA%\DarkTaskMgr\history. Usage is rolled up
    /// per application per day, and every high-usage spike is logged with its time, so the App history
    /// tab can show Today, this week, this month, or all time and survive a restart. Accumulation runs
    /// on the sampler thread for every sample regardless of which tab is open, and all access is
    /// guarded by one lock so a background save can never tear a read.
    /// </summary>
    public sealed class AppHistoryStore
    {
        private sealed class Roll
        {
            public double CpuSeconds;
            public double PeakPct;
            public long PeakUnix;
            public long FirstUnix;
            public long LastUnix;
            public string IconPath = "";
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double SpikePct = 20;
        private const int EventCooldownSec = 60;
        private const int MaxEvents = 5000;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Dictionary<string, Roll>> _daily =
            new Dictionary<string, Dictionary<string, Roll>>();               // date -> app -> roll
        private readonly List<AppEvent> _events = new List<AppEvent>();       // chronological
        private readonly Dictionary<string, DateTime> _lastEvent =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private string _dailyPath, _eventsPath;
        private DateTime _lastAccum = DateTime.MinValue;
        private int _sinceSave;

        public void Load()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkTaskMgr", "history");
                Directory.CreateDirectory(dir);
                _dailyPath = Path.Combine(dir, "daily.dat");
                _eventsPath = Path.Combine(dir, "events.dat");
                LoadDaily();
                LoadEvents();
            }
            catch (Exception ex) { Log.Error("history load", ex); }
        }

        private void LoadDaily()
        {
            if (_dailyPath == null || !File.Exists(_dailyPath)) return;
            foreach (var line in File.ReadAllLines(_dailyPath))
            {
                var f = line.Split('\t');
                if (f.Length < 8) continue;
                try
                {
                    var roll = new Roll
                    {
                        CpuSeconds = D(f[2]), PeakPct = D(f[3]), PeakUnix = L(f[4]),
                        FirstUnix = L(f[5]), LastUnix = L(f[6]), IconPath = f[7]
                    };
                    if (!_daily.TryGetValue(f[0], out var day))
                    {
                        day = new Dictionary<string, Roll>(StringComparer.OrdinalIgnoreCase);
                        _daily[f[0]] = day;
                    }
                    day[f[1]] = roll;
                }
                catch { }
            }
        }

        private void LoadEvents()
        {
            if (_eventsPath == null || !File.Exists(_eventsPath)) return;
            foreach (var line in File.ReadAllLines(_eventsPath))
            {
                var f = line.Split('\t');
                if (f.Length < 3) continue;
                try { _events.Add(new AppEvent { Time = FromUnix(L(f[0])), Pct = D(f[1]), App = f[2] }); }
                catch { }
            }
        }

        public void Accumulate(Snapshot snap)
        {
            if (snap?.Processes == null) return;
            var now = DateTime.Now;
            double interval = _lastAccum == DateTime.MinValue ? 0 : (now - _lastAccum).TotalSeconds;
            _lastAccum = now;
            int cores = Math.Max(1, snap.LogicalProcessors);
            long nowUnix = ToUnix(now);
            string dateKey = now.ToString("yyyy-MM-dd", Inv);

            var agg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in snap.Processes)
            {
                string app = string.IsNullOrEmpty(pr.DisplayName) ? pr.Name : pr.DisplayName;
                agg.TryGetValue(app, out double c);
                agg[app] = c + pr.CpuPct;
                if (!paths.ContainsKey(app) && !string.IsNullOrEmpty(pr.ExePath)) paths[app] = pr.ExePath;
            }

            lock (_gate)
            {
                if (!_daily.TryGetValue(dateKey, out var day))
                {
                    day = new Dictionary<string, Roll>(StringComparer.OrdinalIgnoreCase);
                    _daily[dateKey] = day;
                }
                foreach (var kv in agg)
                {
                    string app = Sanitize(kv.Key);
                    double cpu = kv.Value;
                    if (!day.TryGetValue(app, out var roll)) { roll = new Roll(); day[app] = roll; }
                    if (roll.FirstUnix == 0) roll.FirstUnix = nowUnix;
                    if (interval > 0 && interval < 30) roll.CpuSeconds += cpu / 100.0 * cores * interval;
                    if (cpu >= 1) roll.LastUnix = nowUnix;
                    if (cpu > roll.PeakPct) { roll.PeakPct = cpu; roll.PeakUnix = nowUnix; }
                    if (roll.IconPath.Length == 0 && paths.TryGetValue(kv.Key, out string pth)) roll.IconPath = Sanitize(pth);

                    if (cpu >= SpikePct &&
                        (!_lastEvent.TryGetValue(kv.Key, out var last) || (now - last).TotalSeconds >= EventCooldownSec))
                    {
                        _lastEvent[kv.Key] = now;
                        _events.Add(new AppEvent { Time = now, Pct = cpu, App = app });
                        if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
                    }
                }
            }

            if (++_sinceSave >= 30) { _sinceSave = 0; Save(); }
        }

        /// <summary>Aggregate every application over the range and attach its recent spike events.</summary>
        public List<AppSummary> Query(HistoryRange range, out int totalEvents)
        {
            DateTime start = RangeStart(range);
            var map = new Dictionary<string, AppSummary>(StringComparer.OrdinalIgnoreCase);
            int evCount = 0;

            lock (_gate)
            {
                foreach (var dkv in _daily)
                {
                    if (!DateTime.TryParseExact(dkv.Key, "yyyy-MM-dd", Inv, DateTimeStyles.None, out DateTime date)) continue;
                    if (date.Date < start.Date) continue;
                    foreach (var akv in dkv.Value)
                    {
                        var roll = akv.Value;
                        if (!map.TryGetValue(akv.Key, out var sum)) { sum = new AppSummary { App = akv.Key }; map[akv.Key] = sum; }
                        sum.CpuSeconds += roll.CpuSeconds;
                        if (roll.PeakPct > sum.PeakPct) { sum.PeakPct = roll.PeakPct; sum.PeakTime = FromUnix(roll.PeakUnix); }
                        DateTime first = FromUnix(roll.FirstUnix);
                        if (roll.FirstUnix > 0 && (sum.FirstSeen == default(DateTime) || first < sum.FirstSeen)) sum.FirstSeen = first;
                        DateTime lastA = FromUnix(roll.LastUnix);
                        if (lastA > sum.LastActive) sum.LastActive = lastA;
                        if (sum.IconPath.Length == 0 && roll.IconPath.Length > 0) sum.IconPath = roll.IconPath;
                    }
                }

                for (int i = _events.Count - 1; i >= 0; i--)
                {
                    var e = _events[i];
                    if (e.Time.Date < start.Date) break; // events are chronological, so nothing older qualifies
                    evCount++;
                    if (map.TryGetValue(e.App, out var sum))
                    {
                        sum.EventCount++;
                        if (sum.Events.Count < 6) sum.Events.Add(e);
                    }
                }
            }

            totalEvents = evCount;
            return map.Values.OrderByDescending(s => s.CpuSeconds).ToList();
        }

        public void Save()
        {
            try
            {
                lock (_gate)
                {
                    if (_dailyPath == null) return;
                    var sb = new StringBuilder();
                    foreach (var dkv in _daily)
                        foreach (var akv in dkv.Value)
                        {
                            var r = akv.Value;
                            sb.Append(dkv.Key).Append('\t').Append(akv.Key).Append('\t')
                              .Append(r.CpuSeconds.ToString("0.###", Inv)).Append('\t')
                              .Append(r.PeakPct.ToString("0.###", Inv)).Append('\t')
                              .Append(r.PeakUnix).Append('\t').Append(r.FirstUnix).Append('\t')
                              .Append(r.LastUnix).Append('\t').Append(r.IconPath).Append('\n');
                        }
                    File.WriteAllText(_dailyPath, sb.ToString());

                    var eb = new StringBuilder();
                    foreach (var e in _events)
                        eb.Append(ToUnix(e.Time)).Append('\t').Append(e.Pct.ToString("0.###", Inv)).Append('\t').Append(e.App).Append('\n');
                    File.WriteAllText(_eventsPath, eb.ToString());
                }
            }
            catch (Exception ex) { Log.Error("history save", ex); }
        }

        private static DateTime RangeStart(HistoryRange r)
        {
            DateTime today = DateTime.Today;
            switch (r)
            {
                case HistoryRange.Today: return today;
                case HistoryRange.Week: { int diff = (7 + (int)(today.DayOfWeek - DayOfWeek.Monday)) % 7; return today.AddDays(-diff); }
                case HistoryRange.Month: return new DateTime(today.Year, today.Month, 1);
                default: return DateTime.MinValue;
            }
        }

        private static double D(string s) { return double.TryParse(s, NumberStyles.Any, Inv, out double v) ? v : 0; }
        private static long L(string s) { return long.TryParse(s, NumberStyles.Any, Inv, out long v) ? v : 0; }
        private static long ToUnix(DateTime t) { try { return new DateTimeOffset(t).ToUnixTimeSeconds(); } catch { return 0; } }
        private static DateTime FromUnix(long u) { if (u <= 0) return default(DateTime); try { return DateTimeOffset.FromUnixTimeSeconds(u).LocalDateTime; } catch { return default(DateTime); } }
        private static string Sanitize(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
