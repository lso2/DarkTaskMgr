using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    public sealed class StartupRow : INotifyPropertyChanged
    {
        public StartupEntry Entry;
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Location { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }

        private string _status;
        public string StatusText { get { return _status; } set { if (_status != value) { _status = value; Notify("StatusText"); Notify("StatusBrush"); } } }
        public Brush StatusBrush
        {
            get { return (Brush)(StatusText == "Enabled" ? Application.Current.Resources["OkBrush"] : Application.Current.Resources["TextMutedBrush"]); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
    }

    /// <summary>The Startup tab. It reads the sign-in programs once when shown and enables or disables
    /// the selected one, which writes the same approval record Windows honors. Machine-wide items need
    /// administrator rights, so a denied write is reported rather than silently ignored.</summary>
    public partial class StartupView : UserControl, ITabView
    {
        private readonly StartupProvider _provider = new StartupProvider();
        private readonly ObservableCollection<StartupRow> _rows = new ObservableCollection<StartupRow>();
        private bool _loaded;

        public StartupView()
        {
            InitializeComponent();
            List.ItemsSource = _rows;
            List.PreviewMouseRightButtonDown += RightDown;
            List.ContextMenuOpening += OpenMenu;
            List.ContextMenu = new ContextMenu();
        }

        public void OnActivated(Snapshot latest) { if (!_loaded) Refresh(); }
        public void OnSnapshot(Snapshot s) { }
        public void OnDeactivated() { }

        private void Refresh()
        {
            _loaded = true;
            _rows.Clear();
            var entries = _provider.Enumerate();
            entries.Sort((a, b) =>
            {
                int c = b.Enabled.CompareTo(a.Enabled);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var e in entries)
            {
                _rows.Add(new StartupRow
                {
                    Entry = e,
                    Name = e.Name,
                    Publisher = e.Publisher,
                    Location = e.LocationLabel,
                    Icon = IconCache.ForPath(e.IconPath),
                    StatusText = e.Enabled ? "Enabled" : "Disabled"
                });
            }
        }

        private void RightDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListViewItem)) dep = VisualTreeHelper.GetParent(dep);
            if (dep is ListViewItem item) { item.IsSelected = true; item.Focus(); }
        }

        private void OpenMenu(object sender, ContextMenuEventArgs e)
        {
            var row = List.SelectedItem as StartupRow;
            var cm = List.ContextMenu;
            cm.Items.Clear();
            if (row == null) { e.Handled = true; return; }

            if (row.Entry.Enabled) cm.Items.Add(Item("Disable", () => Toggle(row, false)));
            else cm.Items.Add(Item("Enable", () => Toggle(row, true)));
            cm.Items.Add(new Separator());
            cm.Items.Add(Item("Open file location", () => OpenLocation(row)));
        }

        private void Toggle(StartupRow row, bool enable)
        {
            bool ok = _provider.SetEnabled(row.Entry, enable);
            if (ok) row.StatusText = enable ? "Enabled" : "Disabled";
            else
                MessageBox.Show(Window.GetWindow(this),
                    "Could not change this startup item. Machine-wide entries need administrator rights, so restart DarkTaskMgr as administrator.",
                    "DarkTaskMgr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenLocation(StartupRow row)
        {
            try
            {
                string path = row.Entry.Command;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("startup open location", ex); }
        }

        private static MenuItem Item(string header, Action onClick)
        {
            var m = new MenuItem { Header = header };
            m.Click += (s, e) => onClick();
            return m;
        }
    }
}
