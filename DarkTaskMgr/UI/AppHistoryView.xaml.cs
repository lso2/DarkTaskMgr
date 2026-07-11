using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    /// <summary>One row in the App history table, populated from a persisted summary.</summary>
    public sealed class AppUsage : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public double CpuSeconds;

        private ImageSource _icon;
        private string _cpuTime = "0:00:00", _peak = "--", _events = "0";
        public ImageSource Icon { get { return _icon; } set { Set(ref _icon, value, "Icon"); } }
        public string CpuTimeText { get { return _cpuTime; } set { Set(ref _cpuTime, value, "CpuTimeText"); } }
        public string PeakText { get { return _peak; } set { Set(ref _peak, value, "PeakText"); } }
        public string EventsText { get { return _events; } set { Set(ref _events, value, "EventsText"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T f, T v, string n)
        {
            if (!EqualityComparer<T>.Default.Equals(f, v)) { f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        }
    }

    /// <summary>
    /// The App history tab. It reads the persistent history store by time range (Today by default,
    /// then this week, this month, or all time), surfaces the busiest applications as cards with the
    /// times their usage peaked and spiked, and lists every application in the table below. The store
    /// records usage for every sample regardless of which tab is open, so switching to All time shows
    /// history from previous sessions too.
    /// </summary>
    public partial class AppHistoryView : UserControl, ITabView
    {
        private sealed class CardRefs
        {
            public Border Root;
            public Image Icon;
            public TextBlock Name, Cpu, Peak, First, Last, Spikes;
        }

        private readonly Dictionary<string, AppUsage> _byApp = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CardRefs> _cards = new Dictionary<string, CardRefs>(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<AppUsage> _rows = new ObservableCollection<AppUsage>();
        private HistoryRange _range = HistoryRange.Today;
        private int _tick;

        public AppHistoryView(Engine engine)
        {
            InitializeComponent();
            List.ItemsSource = _rows;
        }

        public void OnActivated(Snapshot latest) { Refresh(); }
        public void OnDeactivated() { }

        // Refresh on a slower cadence than the sampler, because aggregating the range every second is
        // needless when the figures move slowly.
        public void OnSnapshot(Snapshot s) { if ((++_tick % 2) == 0) Refresh(); }

        private void Range_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == ChipToday) _range = HistoryRange.Today;
            else if (sender == ChipWeek) _range = HistoryRange.Week;
            else if (sender == ChipMonth) _range = HistoryRange.Month;
            else if (sender == ChipAll) _range = HistoryRange.All;
            if (IsLoaded) Refresh();
        }

        private void Refresh()
        {
            var store = App.History;
            if (store == null) return;
            var summaries = store.Query(_range, out int totalEvents);
            bool today = _range == HistoryRange.Today;

            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var desired = new List<AppUsage>(summaries.Count);
            foreach (var sum in summaries)
            {
                live.Add(sum.App);
                if (!_byApp.TryGetValue(sum.App, out var row)) { row = new AppUsage { Name = sum.App }; _byApp[sum.App] = row; }
                row.CpuSeconds = sum.CpuSeconds;
                row.Icon = IconCache.ForPath(sum.IconPath);
                row.CpuTimeText = Duration(sum.CpuSeconds);
                row.PeakText = PeakText(sum, today);
                row.EventsText = sum.EventCount.ToString();
                desired.Add(row);
            }
            foreach (var dead in _byApp.Keys.Where(k => !live.Contains(k)).ToList()) _byApp.Remove(dead);
            ReconcileRows(desired);

            UpdateCards(summaries.Where(s => s.CpuSeconds > 0).Take(6).ToList(), today);

            SinceLabel.Text = totalEvents + (totalEvents == 1 ? " spike recorded" : " spikes recorded");
            TableCaption.Text = "ALL APPLICATIONS  •  " + RangeLabel(_range);
        }

        // ---- Cards ----------------------------------------------------------

        private void UpdateCards(List<AppSummary> top, bool today)
        {
            var names = new HashSet<string>(top.Select(a => a.App), StringComparer.OrdinalIgnoreCase);
            foreach (var name in _cards.Keys.Where(k => !names.Contains(k)).ToList()) _cards.Remove(name);

            foreach (var sum in top)
            {
                if (!_cards.TryGetValue(sum.App, out var card)) { card = CreateCard(); _cards[sum.App] = card; }
                FillCard(card, sum, today);
            }

            Cards.Children.Clear();
            foreach (var sum in top) Cards.Children.Add(_cards[sum.App].Root);
        }

        private CardRefs CreateCard()
        {
            var refs = new CardRefs();
            var outer = new Border { Style = (Style)FindResource("Card"), Width = 258, Margin = new Thickness(0, 0, 10, 10) };
            var panel = new StackPanel();

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            refs.Icon = new Image { Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
            refs.Name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            header.Children.Add(refs.Icon);
            header.Children.Add(refs.Name);
            panel.Children.Add(header);

            var grid = new UniformGrid { Columns = 2 };
            refs.Cpu = AddStat(grid, "CPU time");
            refs.Peak = AddStat(grid, "Peak CPU");
            refs.First = AddStat(grid, "First seen");
            refs.Last = AddStat(grid, "Last active");
            panel.Children.Add(grid);

            refs.Spikes = new TextBlock { FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextMutedBrush") };
            panel.Children.Add(refs.Spikes);

            outer.Child = panel;
            refs.Root = outer;
            return refs;
        }

        private TextBlock AddStat(Panel host, string caption)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 8, 6) };
            sp.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, Foreground = (Brush)FindResource("TextSecondaryBrush") });
            var val = new TextBlock { FontSize = 13.5, Margin = new Thickness(0, 1, 0, 0), Foreground = (Brush)FindResource("TextPrimaryBrush") };
            sp.Children.Add(val);
            host.Children.Add(sp);
            return val;
        }

        private void FillCard(CardRefs card, AppSummary sum, bool today)
        {
            card.Icon.Source = IconCache.ForPath(sum.IconPath);
            card.Name.Text = sum.App;
            card.Cpu.Text = Duration(sum.CpuSeconds);
            card.Peak.Text = PeakText(sum, today);
            card.First.Text = FmtTime(sum.FirstSeen, today);
            card.Last.Text = FmtTime(sum.LastActive, today);

            if (sum.Events.Count > 0)
            {
                var parts = sum.Events.Select(ev => FmtTime(ev.Time, today) + " " + Math.Round(ev.Pct) + "%");
                card.Spikes.Text = "Spikes:  " + string.Join("   •   ", parts);
            }
            else card.Spikes.Text = "No spikes over 20% in range";
        }

        // ---- Table ----------------------------------------------------------

        private void ReconcileRows(List<AppUsage> desired)
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

        // ---- Formatting -----------------------------------------------------

        private static string PeakText(AppSummary sum, bool today)
        {
            return sum.PeakPct <= 0 ? "--" : Math.Round(sum.PeakPct) + "%  at  " + FmtTime(sum.PeakTime, today);
        }

        private static string FmtTime(DateTime t, bool today)
        {
            if (t == default(DateTime)) return "--";
            return today ? t.ToString("HH:mm:ss") : t.ToString("MMM d, HH:mm");
        }

        private static string RangeLabel(HistoryRange r)
        {
            switch (r)
            {
                case HistoryRange.Week: return "This week";
                case HistoryRange.Month: return "This month";
                case HistoryRange.All: return "All time";
                default: return "Today";
            }
        }

        private static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }
    }
}
