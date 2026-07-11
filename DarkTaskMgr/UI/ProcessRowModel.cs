using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    public enum ProcSort { Name, Status, Cpu, Mem, Disk, Net }

    /// <summary>One visible line in the Processes table: an application (which may aggregate several
    /// processes) or, when expanded, one of its child processes. It raises change notifications for
    /// the derived text and heat values so the grid animates in place without being rebuilt.</summary>
    public sealed class ProcRow : INotifyPropertyChanged
    {
        public string Key;
        public int Level;
        public int Pid;
        public string Category;
        public string DisplayName;

        private int _count = 1;
        private bool _expVis, _expanded;
        private ImageSource _icon;
        private string _nameText, _statusText = "";

        public ImageSource Icon { get { return _icon; } set { Set(ref _icon, value, "Icon"); } }
        private string _cpuText = "0%", _memText, _diskText, _netText;
        private double _cpuHeat, _memHeat, _diskHeat, _netHeat;

        public string NameText { get { return _nameText; } private set { Set(ref _nameText, value, "NameText"); } }
        public bool ExpanderVisible { get { return _expVis; } set { Set(ref _expVis, value, "ExpanderVisible"); } }
        public bool IsExpanded { get { return _expanded; } set { Set(ref _expanded, value, "IsExpanded"); } }
        public string StatusText { get { return _statusText; } set { Set(ref _statusText, value, "StatusText"); } }

        public string CpuText { get { return _cpuText; } private set { Set(ref _cpuText, value, "CpuText"); } }
        public string MemText { get { return _memText; } private set { Set(ref _memText, value, "MemText"); } }
        public string DiskText { get { return _diskText; } private set { Set(ref _diskText, value, "DiskText"); } }
        public string NetText { get { return _netText; } private set { Set(ref _netText, value, "NetText"); } }

        public double CpuHeat { get { return _cpuHeat; } private set { Set(ref _cpuHeat, value, "CpuHeat"); } }
        public double MemHeat { get { return _memHeat; } private set { Set(ref _memHeat, value, "MemHeat"); } }
        public double DiskHeat { get { return _diskHeat; } private set { Set(ref _diskHeat, value, "DiskHeat"); } }
        public double NetHeat { get { return _netHeat; } private set { Set(ref _netHeat, value, "NetHeat"); } }

        // Sort keys, kept as raw numbers so the comparer never parses formatted strings.
        public double CpuPct, DiskBps, NetBps;
        public long MemBytes;
        public int Count { get { return _count; } }

        public void SetCount(int count)
        {
            if (_count == count && _nameText != null) return;
            _count = count;
            NameText = count > 1 ? DisplayName + "  (" + count + ")" : DisplayName;
        }

        public void Apply(double cpu, long mem, double disk, double net,
                          double cpuHeat, double memHeat, double diskHeat, double netHeat)
        {
            CpuPct = cpu; MemBytes = mem; DiskBps = disk; NetBps = net;
            CpuText = UiFormat.Percent1(cpu);
            MemText = UiFormat.Mb(mem);
            DiskText = UiFormat.DiskRate(disk);
            NetText = UiFormat.NetRate(net);
            CpuHeat = cpuHeat; MemHeat = memHeat; DiskHeat = diskHeat; NetHeat = netHeat;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Live two-line column header (percentage over label) with a sort caret.</summary>
    public sealed class ColHeaderVM : INotifyPropertyChanged
    {
        public ProcSort Sort;
        private string _caption, _pctText, _glyph = "";
        public bool HasPercent { get; set; }
        public string Caption { get { return _caption; } set { Set(ref _caption, value, "Caption"); } }
        public string PercentText { get { return _pctText; } set { Set(ref _pctText, value, "PercentText"); } }
        public string SortGlyph { get { return _glyph; } set { Set(ref _glyph, value, "SortGlyph"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T f, T v, string n)
        {
            if (EqualityComparer<T>.Default.Equals(f, v)) return;
            f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>
    /// Turns the flat per-process snapshot into the grouped, sortable, expandable rows the Processes
    /// tab shows. Row objects persist across samples and are reordered in place, so selection and the
    /// scroll position survive an update and the numbers change without the list flickering.
    /// </summary>
    public sealed class ProcessTree
    {
        private sealed class Parent
        {
            public ProcRow Row;
            public readonly Dictionary<int, ProcRow> Children = new Dictionary<int, ProcRow>();
        }

        public ObservableCollection<ProcRow> Rows { get; } = new ObservableCollection<ProcRow>();
        public bool Grouped { get; private set; }

        private readonly Dictionary<string, Parent> _parents = new Dictionary<string, Parent>();
        private ProcSort _sort = ProcSort.Cpu;
        private bool _asc;
        private List<ProcInfo> _lastProcs;
        private SystemStats _lastSys;

        public void SetGrouped(bool grouped)
        {
            Grouped = grouped;
            if (_lastProcs != null) Update(_lastProcs, _lastSys);
        }

        public void SetSort(ProcSort sort, bool ascending)
        {
            _sort = sort; _asc = ascending;
            if (_lastProcs != null) Update(_lastProcs, _lastSys);
        }

        public ProcSort CurrentSort { get { return _sort; } }
        public bool Ascending { get { return _asc; } }

        /// <summary>The process ids a row stands for: a child is itself, an application is all of its
        /// members, so ending an application row ends every process under it.</summary>
        public List<int> PidsFor(ProcRow row)
        {
            var list = new List<int>();
            if (row == null) return list;
            if (row.Level == 1) { list.Add(row.Pid); return list; }
            if (_parents.TryGetValue(row.Key, out var p) && p.Children.Count > 0) list.AddRange(p.Children.Keys);
            else if (row.Pid != 0) list.Add(row.Pid);
            return list;
        }

        public void ToggleExpand(ProcRow row)
        {
            if (row == null || !row.ExpanderVisible) return;
            row.IsExpanded = !row.IsExpanded;
            if (_lastProcs != null) Rebuild();
        }

        public void ExpandAll(bool expand)
        {
            foreach (var p in _parents.Values)
                if (p.Children.Count > 0) p.Row.IsExpanded = expand;
            Rebuild();
        }

        public void Update(List<ProcInfo> procs, SystemStats sys)
        {
            _lastProcs = procs; _lastSys = sys;
            if (procs == null) return;

            var groups = new Dictionary<string, List<ProcInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in procs)
            {
                string key = string.IsNullOrEmpty(p.DisplayName) ? p.Name : p.DisplayName;
                if (!groups.TryGetValue(key, out var l)) { l = new List<ProcInfo>(); groups[key] = l; }
                l.Add(p);
            }

            double maxDisk = 1;
            foreach (var g in groups.Values)
            {
                double d = g.Sum(x => x.DiskBps);
                if (d > maxDisk) maxDisk = d;
            }
            double totalMem = sys != null && sys.MemTotalBytes > 0 ? sys.MemTotalBytes : 1;
            double sysCpu = sys != null ? Math.Max(sys.CpuPct, 3) : 100;
            double sysMemFrac = sys != null ? Math.Max(sys.MemUsedPct / 100.0, 0.05) : 1;

            // Drop groups that no longer exist.
            foreach (var dead in _parents.Keys.Where(k => !groups.ContainsKey(k)).ToList())
                _parents.Remove(dead);

            foreach (var kv in groups)
            {
                var members = kv.Value;
                if (!_parents.TryGetValue(kv.Key, out var parent))
                {
                    parent = new Parent { Row = new ProcRow { Key = kv.Key, Level = 0, DisplayName = kv.Key } };
                    _parents[kv.Key] = parent;
                }

                double cpu = members.Sum(m => m.CpuPct);
                long mem = members.Sum(m => m.WorkingSetBytes);
                double disk = members.Sum(m => m.DiskBps);
                double net = members.Sum(m => m.NetBps);

                var row = parent.Row;
                row.Category = CategoryOf(members);
                row.Pid = members.Count == 1 ? members[0].Pid : 0;
                row.Icon = IconCache.ForPath(IconPathOf(members));
                row.SetCount(members.Count);
                row.StatusText = members.Any(m => m.Suspended) ? "Suspended" : "";
                row.ExpanderVisible = members.Count > 1 && !Grouped;
                row.Apply(cpu, mem, disk, net,
                          cpu / sysCpu,
                          (mem / totalMem) / sysMemFrac,
                          disk / maxDisk, 0);

                SyncChildren(parent, members, sys, totalMem, sysCpu, sysMemFrac, maxDisk);
            }

            Rebuild();
        }

        private void SyncChildren(Parent parent, List<ProcInfo> members, SystemStats sys,
                                  double totalMem, double sysCpu, double sysMemFrac, double maxDisk)
        {
            if (members.Count <= 1) { parent.Children.Clear(); return; }
            var live = new HashSet<int>();
            foreach (var m in members)
            {
                live.Add(m.Pid);
                if (!parent.Children.TryGetValue(m.Pid, out var cr))
                {
                    cr = new ProcRow { Key = parent.Row.Key + "#" + m.Pid, Level = 1, Pid = m.Pid, DisplayName = parent.Row.DisplayName };
                    cr.SetCount(1);
                    cr.Icon = IconCache.ForPath(m.ExePath);
                    parent.Children[m.Pid] = cr;
                }
                cr.Category = parent.Row.Category;
                cr.StatusText = m.Suspended ? "Suspended" : "";
                cr.Apply(m.CpuPct, m.WorkingSetBytes, m.DiskBps, m.NetBps,
                         m.CpuPct / sysCpu,
                         (m.WorkingSetBytes / totalMem) / sysMemFrac,
                         m.DiskBps / maxDisk, 0);
            }
            foreach (var dead in parent.Children.Keys.Where(k => !live.Contains(k)).ToList())
                parent.Children.Remove(dead);
        }

        private void Rebuild()
        {
            var parents = _parents.Values.Select(p => p).ToList();
            parents.Sort((a, b) => Compare(a.Row, b.Row));

            var desired = new List<ProcRow>(parents.Count);
            foreach (var p in parents)
            {
                desired.Add(p.Row);
                if (!Grouped && p.Row.IsExpanded && p.Children.Count > 0)
                {
                    var kids = p.Children.Values.ToList();
                    kids.Sort(Compare);
                    desired.AddRange(kids);
                }
            }
            Reconcile(desired);
        }

        private int Compare(ProcRow a, ProcRow b)
        {
            int c;
            switch (_sort)
            {
                case ProcSort.Name: c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); break;
                case ProcSort.Status: c = string.Compare(a.StatusText, b.StatusText, StringComparison.OrdinalIgnoreCase); break;
                case ProcSort.Mem: c = a.MemBytes.CompareTo(b.MemBytes); break;
                case ProcSort.Disk: c = a.DiskBps.CompareTo(b.DiskBps); break;
                case ProcSort.Net: c = a.NetBps.CompareTo(b.NetBps); break;
                default: c = a.CpuPct.CompareTo(b.CpuPct); break;
            }
            if (c == 0) c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            return _asc ? c : -c;
        }

        // Match the collection to the desired order using moves and inserts, so the same row instances
        // stay in place and the ListView keeps its selection and scroll offset.
        private void Reconcile(List<ProcRow> desired)
        {
            for (int i = 0; i < desired.Count; i++)
            {
                var want = desired[i];
                if (i < Rows.Count && ReferenceEquals(Rows[i], want)) continue;

                int found = -1;
                for (int j = i + 1; j < Rows.Count; j++)
                    if (ReferenceEquals(Rows[j], want)) { found = j; break; }

                if (found >= 0) Rows.Move(found, i);
                else Rows.Insert(i, want);
            }
            while (Rows.Count > desired.Count) Rows.RemoveAt(Rows.Count - 1);
        }

        private static string IconPathOf(List<ProcInfo> members)
        {
            foreach (var m in members)
                if (!string.IsNullOrEmpty(m.ExePath)) return m.ExePath;
            return "";
        }

        private static string CategoryOf(List<ProcInfo> members)
        {
            if (members.Any(m => m.HasWindow)) return "Apps";
            bool allSystem = members.All(m =>
            {
                string u = m.UserName ?? "";
                return u.Length == 0 || u == "SYSTEM" || u == "LOCAL SERVICE" || u == "NETWORK SERVICE";
            });
            return allSystem ? "Windows processes" : "Background processes";
        }
    }
}
