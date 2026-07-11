using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    /// <summary>A tab that wants the sampling stream. OnActivated fires when the tab is shown,
    /// OnSnapshot on every sample while it is the active tab, and OnDeactivated when it is hidden.</summary>
    public interface ITabView
    {
        void OnActivated(Snapshot latest);
        void OnSnapshot(Snapshot s);
        void OnDeactivated();
    }

    public partial class MainWindow : Window
    {
        private readonly Engine _engine;
        private readonly Dictionary<RadioButton, FrameworkElement> _tabs = new Dictionary<RadioButton, FrameworkElement>();
        private FrameworkElement _active;

        private ProcessesView _processes;
        private PerformanceView _performance;
        private AppHistoryView _appHistory;
        private StartupView _startup;
        private UsersView _users;
        private DetailsView _details;
        private ServicesView _services;

        public MainWindow(Engine engine)
        {
            _engine = engine;
            InitializeComponent();

            AdminBadge.Visibility = App.IsElevated() ? Visibility.Visible : Visibility.Collapsed;

            _processes = new ProcessesView(_engine);
            _performance = new PerformanceView(_engine);
            _appHistory = new AppHistoryView(_engine);
            _startup = new StartupView();
            _users = new UsersView(_engine);
            _details = new DetailsView(_engine);
            _services = new ServicesView();

            AddView(TabProcesses, _processes);
            AddView(TabPerformance, _performance);
            AddView(TabAppHistory, _appHistory);
            AddView(TabStartup, _startup);
            AddView(TabUsers, _users);
            AddView(TabDetails, _details);
            AddView(TabServices, _services);

            _processes.RequestDetails += pid => { TabDetails.IsChecked = true; };

            StateChanged += (s, e) => UpdateMaximizeChrome();
            Loaded += (s, e) => { UpdateMaximizeChrome(); ActivateTab(TabProcesses); };

            _engine.Updated += Engine_Updated;
        }

        private void AddView(RadioButton tab, FrameworkElement view)
        {
            view.Visibility = Visibility.Collapsed;
            Host.Children.Add(view);
            _tabs[tab] = view;
        }

        private void Engine_Updated(object sender, Snapshot snap)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStatusBar(snap);
                (_active as ITabView)?.OnSnapshot(snap);
            }));
        }

        private void UpdateStatusBar(Snapshot s)
        {
            StatusLeft.Text = "Processes: " + s.ProcessCount + "     Threads: " + s.ThreadTotal + "     Handles: " + s.HandleTotal;
            StatusRight.Text = "CPU " + UiFormat.PercentInt(s.System.CpuPct)
                               + "      Memory " + UiFormat.PercentInt(s.System.MemUsedPct)
                               + "      Disk " + UiFormat.PercentInt(s.System.DiskActivePct)
                               + "      Network " + UiFormat.PercentInt(s.System.NetUtilPct);
        }

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            var tab = sender as RadioButton;
            if (tab == null || !_tabs.ContainsKey(tab)) return;
            if (!IsLoaded) return;
            ActivateTab(tab);
        }

        private void ActivateTab(RadioButton tab)
        {
            if (!_tabs.TryGetValue(tab, out var view)) return;
            if (_active == view) return;

            (_active as ITabView)?.OnDeactivated();
            if (_active != null) _active.Visibility = Visibility.Collapsed;

            _active = view;
            view.Visibility = Visibility.Visible;
            (view as ITabView)?.OnActivated(_engine.Latest);
            _engine.RequestRefresh();
        }

        // ---- Window buttons and dragging ------------------------------------

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            try { DragMove(); } catch { }
        }

        private void Min_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void Max_Click(object sender, RoutedEventArgs e) { ToggleMaximize(); }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // A borderless window overflows the screen by the resize border when maximized, so the root
        // is inset by that amount and its corners squared off, which keeps the content fully visible.
        // The caption glyph is built from its code point (0xE923 restore, 0xE922 maximize) so the
        // Segoe MDL2 Assets character is created at runtime with no literal glyph in the source.
        private void UpdateMaximizeChrome()
        {
            bool max = WindowState == WindowState.Maximized;
            RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
            RootBorder.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(10);
            MaxGlyph.Text = char.ConvertFromUtf32(max ? 0xE923 : 0xE922);
        }

        // ---- Menus ----------------------------------------------------------

        private void FileMenu_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu();
            cm.Items.Add(MenuItemFor("Run new task", RunNewTask));
            cm.Items.Add(new Separator());
            cm.Items.Add(MenuItemFor("Exit", () => Close()));
            Open(cm, FileMenuBtn);
        }

        private void OptionsMenu_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu();

            var onTop = new MenuItem { Header = "Always on top", IsCheckable = true, IsChecked = Topmost };
            onTop.Click += (s, a) => Topmost = onTop.IsChecked;
            cm.Items.Add(onTop);

            if (!App.IsElevated())
                cm.Items.Add(MenuItemFor("Run as administrator", App.RestartElevated));

            cm.Items.Add(new Separator());

            cm.Items.Add(SpeedItem("High (0.5s)", 500, false));
            cm.Items.Add(SpeedItem("Normal (1s)", 1000, false));
            cm.Items.Add(SpeedItem("Low (2s)", 2000, false));
            cm.Items.Add(SpeedItem("Paused", 1000, true));

            Open(cm, OptionsMenuBtn);
        }

        private void ViewMenu_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu();
            cm.Items.Add(MenuItemFor("Refresh now", () => _engine.RequestRefresh()));
            cm.Items.Add(new Separator());

            var group = new MenuItem { Header = "Group by type", IsCheckable = true, IsChecked = _processes.Grouped };
            group.Click += (s, a) => _processes.SetGrouped(group.IsChecked);
            cm.Items.Add(group);
            cm.Items.Add(MenuItemFor("Expand all", () => _processes.ExpandAll(true)));
            cm.Items.Add(MenuItemFor("Collapse all", () => _processes.ExpandAll(false)));
            Open(cm, ViewMenuBtn);
        }

        private MenuItem SpeedItem(string label, int ms, bool paused)
        {
            bool active = paused ? _engine.Paused : (!_engine.Paused && _engine.IntervalMs == ms);
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = active };
            item.Click += (s, a) =>
            {
                _engine.Paused = paused;
                if (!paused) { _engine.IntervalMs = ms; _engine.RequestRefresh(); }
            };
            return item;
        }

        private static MenuItem MenuItemFor(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, a) => onClick();
            return item;
        }

        private static void Open(ContextMenu cm, UIElement target)
        {
            cm.PlacementTarget = target;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        // ---- Run new task ---------------------------------------------------

        private void RunNewTask()
        {
            string cmd = RunDialog.Show(this);
            if (string.IsNullOrWhiteSpace(cmd)) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo { FileName = cmd.Trim(), UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start '" + cmd + "'.\n\n" + ex.Message, "DarkTaskMgr",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _engine.Updated -= Engine_Updated; } catch { }
            base.OnClosed(e);
        }
    }
}
