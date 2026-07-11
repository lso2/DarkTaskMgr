using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    /// <summary>The Processes tab: applications grouped from the live snapshot, with heat-map cells,
    /// click-to-sort headers, an optional grouping by type, and a right-click menu that ends tasks.</summary>
    public partial class ProcessesView : UserControl, ITabView
    {
        private readonly Engine _engine;
        private readonly ProcessTree _tree = new ProcessTree();
        private readonly CollectionViewSource _cvs = new CollectionViewSource();

        private ColHeaderVM _hName, _hStatus, _hCpu, _hMem, _hDisk, _hNet;

        public event Action<int> RequestDetails;

        public ProcessesView(Engine engine)
        {
            _engine = engine;
            InitializeComponent();

            _hName = new ColHeaderVM { Caption = "Name", Sort = ProcSort.Name };
            _hStatus = new ColHeaderVM { Caption = "Status", Sort = ProcSort.Status };
            _hCpu = new ColHeaderVM { Caption = "CPU", HasPercent = true, PercentText = "0%", Sort = ProcSort.Cpu };
            _hMem = new ColHeaderVM { Caption = "Memory", HasPercent = true, PercentText = "0%", Sort = ProcSort.Mem };
            _hDisk = new ColHeaderVM { Caption = "Disk", HasPercent = true, PercentText = "0%", Sort = ProcSort.Disk };
            _hNet = new ColHeaderVM { Caption = "Network", HasPercent = true, PercentText = "0%", Sort = ProcSort.Net };

            ColName.Header = _hName; ColStatus.Header = _hStatus; ColCpu.Header = _hCpu;
            ColMem.Header = _hMem; ColDisk.Header = _hDisk; ColNet.Header = _hNet;

            _cvs.Source = _tree.Rows;
            List.ItemsSource = _cvs.View;

            List.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
            List.PreviewMouseRightButtonDown += List_RightDown;
            List.ContextMenuOpening += List_ContextMenuOpening;
            List.ContextMenu = new ContextMenu();

            UpdateSortGlyphs();
        }

        public bool Grouped { get { return _tree.Grouped; } }

        public void SetGrouped(bool grouped)
        {
            _cvs.GroupDescriptions.Clear();
            if (grouped) _cvs.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            _tree.SetGrouped(grouped);
        }

        public void ExpandAll(bool expand) { _tree.ExpandAll(expand); }

        // ---- ITabView -------------------------------------------------------

        public void OnActivated(Snapshot latest) { if (latest != null) OnSnapshot(latest); }
        public void OnDeactivated() { }

        public void OnSnapshot(Snapshot s)
        {
            if (s == null) return;
            _tree.Update(s.Processes, s.System);
            _hCpu.PercentText = UiFormat.PercentInt(s.System.CpuPct);
            _hMem.PercentText = UiFormat.PercentInt(s.System.MemUsedPct);
            _hDisk.PercentText = UiFormat.PercentInt(s.System.DiskActivePct);
            _hNet.PercentText = UiFormat.PercentInt(s.System.NetUtilPct);
        }

        // ---- Sorting --------------------------------------------------------

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            var h = e.OriginalSource as GridViewColumnHeader;
            var vm = h?.Content as ColHeaderVM;
            if (vm == null) return;
            bool asc = _tree.CurrentSort == vm.Sort
                ? !_tree.Ascending
                : (vm.Sort == ProcSort.Name || vm.Sort == ProcSort.Status);
            _tree.SetSort(vm.Sort, asc);
            UpdateSortGlyphs();
        }

        private void UpdateSortGlyphs()
        {
            foreach (var vm in new[] { _hName, _hStatus, _hCpu, _hMem, _hDisk, _hNet })
                vm.SortGlyph = vm.Sort == _tree.CurrentSort ? (_tree.Ascending ? "▲" : "▼") : "";
        }

        private void Expander_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as ProcRow;
            _tree.ToggleExpand(row);
        }

        // ---- Context menu ---------------------------------------------------

        private void List_RightDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindItem(e.OriginalSource as DependencyObject);
            if (item != null) { item.IsSelected = true; item.Focus(); }
        }

        private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var row = List.SelectedItem as ProcRow;
            var cm = List.ContextMenu;
            cm.Items.Clear();
            if (row == null) { e.Handled = true; return; }

            if (row.ExpanderVisible)
            {
                cm.Items.Add(Item(row.IsExpanded ? "Collapse" : "Expand", () => _tree.ToggleExpand(row)));
                cm.Items.Add(new Separator());
            }
            cm.Items.Add(Item("End task", () => EndTask(row)));
            cm.Items.Add(Item("Open file location", () => OpenFileLocation(row)));
            cm.Items.Add(Item("Search online", () => SearchOnline(row)));
            if (row.Pid != 0 && RequestDetails != null)
                cm.Items.Add(Item("Go to details", () => RequestDetails(row.Pid)));
        }

        private void EndTask(ProcRow row)
        {
            var pids = _tree.PidsFor(row);
            string failed = null;
            foreach (int pid in pids)
            {
                try { using (var p = Process.GetProcessById(pid)) p.Kill(); }
                catch (Exception ex) { failed = ex.Message; Log.Error("end task " + pid, ex); }
            }
            if (failed != null)
                MessageBox.Show(Window.GetWindow(this),
                    "Could not end '" + row.DisplayName + "'. It may be a protected process.\n\nRestart DarkTaskMgr as administrator to end it.",
                    "DarkTaskMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
            _engine.RequestRefresh();
        }

        private void OpenFileLocation(ProcRow row)
        {
            try
            {
                int pid = row.Pid;
                if (pid == 0) { var l = _tree.PidsFor(row); if (l.Count > 0) pid = l[0]; }
                if (pid == 0) return;
                using (var p = Process.GetProcessById(pid))
                {
                    string file = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(file))
                        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) { Log.Error("open file location", ex); }
        }

        private void SearchOnline(ProcRow row)
        {
            try
            {
                string q = Uri.EscapeDataString(row.DisplayName);
                Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + q) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("search online", ex); }
        }

        private static MenuItem Item(string header, Action onClick)
        {
            var m = new MenuItem { Header = header };
            m.Click += (s, e) => onClick();
            return m;
        }

        private static ListViewItem FindItem(DependencyObject src)
        {
            while (src != null && !(src is ListViewItem)) src = VisualTreeHelper.GetParent(src);
            return src as ListViewItem;
        }
    }
}
