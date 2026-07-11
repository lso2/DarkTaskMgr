using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    /// <summary>One row of the Details tab, updated in place each sample so its figures change without
    /// the list being rebuilt.</summary>
    public sealed class DetailRow : INotifyPropertyChanged
    {
        public int Pid;
        public string Name { get; set; }
        public string Description { get; set; }
        public string PidText { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }

        private string _status = "", _user = "", _cpu = "0%", _mem = "";
        public double CpuPct; public long MemBytes;

        public string StatusText { get { return _status; } set { Set(ref _status, value, "StatusText"); } }
        public string UserName { get { return _user; } set { Set(ref _user, value, "UserName"); } }
        public string CpuText { get { return _cpu; } set { Set(ref _cpu, value, "CpuText"); } }
        public string MemText { get { return _mem; } set { Set(ref _mem, value, "MemText"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T f, T v, string n)
        {
            if (EqualityComparer<T>.Default.Equals(f, v)) return;
            f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    public partial class DetailsView : UserControl, ITabView
    {
        private enum DetSort { Name, Pid, Status, User, Cpu, Mem, Desc }

        private readonly Engine _engine;
        private readonly Dictionary<int, DetailRow> _byPid = new Dictionary<int, DetailRow>();
        private readonly ObservableCollection<DetailRow> _rows = new ObservableCollection<DetailRow>();
        private DetSort _sort = DetSort.Cpu;
        private bool _asc;

        public DetailsView(Engine engine)
        {
            _engine = engine;
            InitializeComponent();
            List.ItemsSource = _rows;
            List.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
            List.PreviewMouseRightButtonDown += RightDown;
            List.ContextMenuOpening += OpenMenu;
            List.ContextMenu = new ContextMenu();
            UpdateHeaders();
        }

        public void OnActivated(Snapshot latest) { if (latest != null) OnSnapshot(latest); }
        public void OnDeactivated() { }

        public void OnSnapshot(Snapshot s)
        {
            if (s?.Processes == null) return;
            var live = new HashSet<int>();
            foreach (var p in s.Processes)
            {
                live.Add(p.Pid);
                if (!_byPid.TryGetValue(p.Pid, out var r))
                {
                    r = new DetailRow
                    {
                        Pid = p.Pid,
                        PidText = p.Pid.ToString(),
                        Name = string.IsNullOrEmpty(p.Name) ? "(unknown)" : p.Name + ".exe",
                        Description = p.Description,
                        Icon = IconCache.ForPath(p.ExePath)
                    };
                    _byPid[p.Pid] = r;
                }
                r.StatusText = p.Suspended ? "Suspended" : "Running";
                r.UserName = p.UserName;
                r.CpuPct = p.CpuPct;
                r.CpuText = UiFormat.Percent1(p.CpuPct);
                r.MemBytes = p.WorkingSetBytes;
                r.MemText = UiFormat.Mb(p.WorkingSetBytes);
            }
            foreach (var dead in new List<int>(_byPid.Keys))
                if (!live.Contains(dead)) _byPid.Remove(dead);

            var desired = new List<DetailRow>(_byPid.Values);
            desired.Sort(Compare);
            Reconcile(desired);
        }

        private int Compare(DetailRow a, DetailRow b)
        {
            int c;
            switch (_sort)
            {
                case DetSort.Name: c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                case DetSort.Pid: c = a.Pid.CompareTo(b.Pid); break;
                case DetSort.Status: c = string.Compare(a.StatusText, b.StatusText, StringComparison.OrdinalIgnoreCase); break;
                case DetSort.User: c = string.Compare(a.UserName, b.UserName, StringComparison.OrdinalIgnoreCase); break;
                case DetSort.Mem: c = a.MemBytes.CompareTo(b.MemBytes); break;
                case DetSort.Desc: c = string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase); break;
                default: c = a.CpuPct.CompareTo(b.CpuPct); break;
            }
            if (c == 0) c = a.Pid.CompareTo(b.Pid);
            return _asc ? c : -c;
        }

        private void Reconcile(List<DetailRow> desired)
        {
            for (int i = 0; i < desired.Count; i++)
            {
                var want = desired[i];
                if (i < _rows.Count && ReferenceEquals(_rows[i], want)) continue;
                int found = -1;
                for (int j = i + 1; j < _rows.Count; j++)
                    if (ReferenceEquals(_rows[j], want)) { found = j; break; }
                if (found >= 0) _rows.Move(found, i);
                else _rows.Insert(i, want);
            }
            while (_rows.Count > desired.Count) _rows.RemoveAt(_rows.Count - 1);
        }

        // ---- Sorting --------------------------------------------------------

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            var h = e.OriginalSource as GridViewColumnHeader;
            if (h?.Column == null) return;
            DetSort key;
            if (h.Column == DName) key = DetSort.Name;
            else if (h.Column == DPid) key = DetSort.Pid;
            else if (h.Column == DStatus) key = DetSort.Status;
            else if (h.Column == DUser) key = DetSort.User;
            else if (h.Column == DCpu) key = DetSort.Cpu;
            else if (h.Column == DMem) key = DetSort.Mem;
            else if (h.Column == DDesc) key = DetSort.Desc;
            else return;

            _asc = _sort == key ? !_asc : (key != DetSort.Cpu && key != DetSort.Mem);
            _sort = key;
            UpdateHeaders();
            var desired = new List<DetailRow>(_byPid.Values);
            desired.Sort(Compare);
            Reconcile(desired);
        }

        private void UpdateHeaders()
        {
            Set(DName, "Name", DetSort.Name);
            Set(DPid, "PID", DetSort.Pid);
            Set(DStatus, "Status", DetSort.Status);
            Set(DUser, "User name", DetSort.User);
            Set(DCpu, "CPU", DetSort.Cpu);
            Set(DMem, "Memory", DetSort.Mem);
            Set(DDesc, "Description", DetSort.Desc);
        }

        private void Set(GridViewColumn col, string caption, DetSort key)
        {
            col.Header = _sort == key ? caption + (_asc ? "  ▲" : "  ▼") : caption;
        }

        // ---- Context menu ---------------------------------------------------

        private void RightDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListViewItem)) dep = VisualTreeHelper.GetParent(dep);
            if (dep is ListViewItem item) { item.IsSelected = true; item.Focus(); }
        }

        private void OpenMenu(object sender, ContextMenuEventArgs e)
        {
            var row = List.SelectedItem as DetailRow;
            var cm = List.ContextMenu;
            cm.Items.Clear();
            if (row == null) { e.Handled = true; return; }

            cm.Items.Add(Item("End task", () => EndTask(row)));
            cm.Items.Add(Item("Open file location", () => OpenLocation(row)));

            var pri = new MenuItem { Header = "Set priority" };
            pri.Items.Add(PriorityItem(row, "Realtime", ProcessPriorityClass.RealTime));
            pri.Items.Add(PriorityItem(row, "High", ProcessPriorityClass.High));
            pri.Items.Add(PriorityItem(row, "Above normal", ProcessPriorityClass.AboveNormal));
            pri.Items.Add(PriorityItem(row, "Normal", ProcessPriorityClass.Normal));
            pri.Items.Add(PriorityItem(row, "Below normal", ProcessPriorityClass.BelowNormal));
            pri.Items.Add(PriorityItem(row, "Low", ProcessPriorityClass.Idle));
            cm.Items.Add(pri);
        }

        private void EndTask(DetailRow row)
        {
            try { using (var p = Process.GetProcessById(row.Pid)) p.Kill(); }
            catch (Exception ex)
            {
                Log.Error("details end task", ex);
                MessageBox.Show(Window.GetWindow(this),
                    "Could not end this process. Restart DarkTaskMgr as administrator to end protected processes.",
                    "DarkTaskMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _engine.RequestRefresh();
        }

        private void OpenLocation(DetailRow row)
        {
            try
            {
                using (var p = Process.GetProcessById(row.Pid))
                {
                    string file = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(file))
                        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) { Log.Error("details open location", ex); }
        }

        private MenuItem PriorityItem(DetailRow row, string label, ProcessPriorityClass cls)
        {
            var m = new MenuItem { Header = label };
            m.Click += (s, e) =>
            {
                try { using (var p = Process.GetProcessById(row.Pid)) p.PriorityClass = cls; }
                catch (Exception ex)
                {
                    Log.Error("set priority", ex);
                    MessageBox.Show(Window.GetWindow(this), "Could not change priority. This may need administrator rights.",
                        "DarkTaskMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            return m;
        }

        private static MenuItem Item(string header, Action onClick)
        {
            var m = new MenuItem { Header = header };
            m.Click += (s, e) => onClick();
            return m;
        }
    }
}
