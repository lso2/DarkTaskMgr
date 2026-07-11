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
    public sealed class ServiceRow : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Group { get; set; }

        private string _pid = "", _status = "";
        public int Pid;
        public string PidText { get { return _pid; } set { Set(ref _pid, value, "PidText"); } }
        public string StatusText { get { return _status; } set { Set(ref _status, value, "StatusText"); Notify("StatusBrush"); } }

        public Brush StatusBrush
        {
            get
            {
                var app = Application.Current;
                return (Brush)(StatusText == "Running" ? app.Resources["OkBrush"] : app.Resources["TextMutedBrush"]);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T f, T v, string n) { if (!EqualityComparer<T>.Default.Equals(f, v)) { f = v; Notify(n); } }
        private void Notify(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
    }

    /// <summary>The Services tab. It lists services through WMI, refreshes on a slow cadence because
    /// service state changes rarely, and starts or stops the selected service through the service
    /// control manager, reporting back when the action needs administrator rights.</summary>
    public partial class ServicesView : UserControl, ITabView
    {
        private enum SSort { Name, Pid, Desc, Status, Group }

        private readonly ServicesProvider _provider = new ServicesProvider();
        private readonly Dictionary<string, ServiceRow> _byName = new Dictionary<string, ServiceRow>(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<ServiceRow> _rows = new ObservableCollection<ServiceRow>();
        private SSort _sort = SSort.Name;
        private bool _asc = true;
        private int _tick;

        public ServicesView()
        {
            InitializeComponent();
            List.ItemsSource = _rows;
            List.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
            List.PreviewMouseRightButtonDown += RightDown;
            List.ContextMenuOpening += OpenMenu;
            List.ContextMenu = new ContextMenu();
            UpdateHeaders();
        }

        public void OnActivated(Snapshot latest) { Refresh(); }
        public void OnDeactivated() { }

        // Service state changes seldom, so re-query every fifth sample rather than every second.
        public void OnSnapshot(Snapshot s)
        {
            if ((++_tick % 5) == 0) Refresh();
        }

        private void Refresh()
        {
            var services = _provider.Enumerate();
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var svc in services)
            {
                if (string.IsNullOrEmpty(svc.Name)) continue;
                live.Add(svc.Name);
                if (!_byName.TryGetValue(svc.Name, out var r))
                {
                    r = new ServiceRow { Name = svc.Name, DisplayName = svc.DisplayName, Group = svc.Group };
                    _byName[svc.Name] = r;
                }
                r.PidText = svc.Pid > 0 ? svc.Pid.ToString() : "";
                r.Pid = svc.Pid;
                r.StatusText = svc.Status;
            }
            foreach (var dead in new List<string>(_byName.Keys))
                if (!live.Contains(dead)) _byName.Remove(dead);

            var desired = new List<ServiceRow>(_byName.Values);
            desired.Sort(Compare);
            Reconcile(desired);
        }

        private int Compare(ServiceRow a, ServiceRow b)
        {
            int c;
            switch (_sort)
            {
                case SSort.Pid: c = a.Pid.CompareTo(b.Pid); break;
                case SSort.Desc: c = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); break;
                case SSort.Status: c = string.Compare(a.StatusText, b.StatusText, StringComparison.OrdinalIgnoreCase); break;
                case SSort.Group: c = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase); break;
                default: c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
            }
            if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return _asc ? c : -c;
        }

        private void Reconcile(List<ServiceRow> desired)
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

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            var h = e.OriginalSource as GridViewColumnHeader;
            if (h?.Column == null) return;
            SSort key;
            if (h.Column == SName) key = SSort.Name;
            else if (h.Column == SPid) key = SSort.Pid;
            else if (h.Column == SDesc) key = SSort.Desc;
            else if (h.Column == SStatus) key = SSort.Status;
            else if (h.Column == SGroup) key = SSort.Group;
            else return;
            _asc = _sort == key ? !_asc : true;
            _sort = key;
            UpdateHeaders();
            var desired = new List<ServiceRow>(_byName.Values);
            desired.Sort(Compare);
            Reconcile(desired);
        }

        private void UpdateHeaders()
        {
            SetH(SName, "Name", SSort.Name);
            SetH(SPid, "PID", SSort.Pid);
            SetH(SDesc, "Description", SSort.Desc);
            SetH(SStatus, "Status", SSort.Status);
            SetH(SGroup, "Group", SSort.Group);
        }

        private void SetH(GridViewColumn col, string caption, SSort key)
        {
            col.Header = _sort == key ? caption + (_asc ? "  ▲" : "  ▼") : caption;
        }

        private void RightDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListViewItem)) dep = VisualTreeHelper.GetParent(dep);
            if (dep is ListViewItem item) { item.IsSelected = true; item.Focus(); }
        }

        private void OpenMenu(object sender, ContextMenuEventArgs e)
        {
            var row = List.SelectedItem as ServiceRow;
            var cm = List.ContextMenu;
            cm.Items.Clear();
            if (row == null) { e.Handled = true; return; }

            bool running = row.StatusText == "Running";
            cm.Items.Add(Item("Start", () => Control(row, true), !running));
            cm.Items.Add(Item("Stop", () => Control(row, false), running));
            cm.Items.Add(Item("Restart", () => { Control(row, false); Control(row, true); }, running));
            cm.Items.Add(new Separator());
            cm.Items.Add(Item("Open Services (services.msc)", OpenServicesMsc, true));
        }

        private void Control(ServiceRow row, bool run)
        {
            string err = _provider.SetRunning(row.Name, run);
            if (err != null)
                MessageBox.Show(Window.GetWindow(this), err, "DarkTaskMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
        }

        private void OpenServicesMsc()
        {
            try { Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open services.msc", ex); }
        }

        private static MenuItem Item(string header, Action onClick, bool enabled)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            m.Click += (s, e) => onClick();
            return m;
        }
    }
}
