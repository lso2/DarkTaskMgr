using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    /// <summary>The Performance tab: a rail of live mini-graphs on the left and a large graph with the
    /// selected resource's figures on the right, fed from the engine's rolling history.</summary>
    public partial class PerformanceView : UserControl, ITabView
    {
        private enum Metric { Cpu, Mem, Disk, Net }
        private Metric _metric = Metric.Cpu;
        private Snapshot _last;

        public PerformanceView(Engine engine)
        {
            InitializeComponent();
        }

        public void OnActivated(Snapshot latest) { if (latest != null) OnSnapshot(latest); }
        public void OnDeactivated() { }

        public void OnSnapshot(Snapshot s)
        {
            if (s == null) return;
            _last = s;
            var sys = s.System;

            CpuVal.Text = UiFormat.PercentInt(sys.CpuPct);
            MemVal.Text = UiFormat.PercentInt(sys.MemUsedPct);
            DiskVal.Text = UiFormat.PercentInt(sys.DiskActivePct);
            NetVal.Text = UiFormat.PercentInt(sys.NetUtilPct);

            CpuMini.SetData(Series(s, Metric.Cpu), Col("CpuBrush"), 100);
            MemMini.SetData(Series(s, Metric.Mem), Col("MemBrush"), 100);
            DiskMini.SetData(Series(s, Metric.Disk), Col("DiskBrush"), 100);
            NetMini.SetData(Series(s, Metric.Net), Col("NetBrush"), 100);

            UpdateDetail(s);
        }

        private void Rail_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == RailCpu) _metric = Metric.Cpu;
            else if (sender == RailMem) _metric = Metric.Mem;
            else if (sender == RailDisk) _metric = Metric.Disk;
            else if (sender == RailNet) _metric = Metric.Net;
            if (_last != null) UpdateDetail(_last);
        }

        private void UpdateDetail(Snapshot s)
        {
            var sys = s.System;
            StatsPanel.Children.Clear();

            switch (_metric)
            {
                case Metric.Cpu:
                    DetailTitle.Text = "CPU";
                    DetailSub.Text = s.LogicalProcessors + " logical processors   •   base " + UiFormat.Ghz(s.BaseGhz);
                    DetailValue.Text = UiFormat.PercentInt(sys.CpuPct);
                    DetailGraph.SetData(Series(s, Metric.Cpu), Col("CpuBrush"), 100);
                    AddStat("Utilization", UiFormat.PercentInt(sys.CpuPct));
                    AddStat("Speed", UiFormat.Ghz(sys.CpuGhz));
                    AddStat("Processes", s.ProcessCount.ToString());
                    AddStat("Threads", s.ThreadTotal.ToString());
                    AddStat("Handles", s.HandleTotal.ToString());
                    AddStat("Up time", UiFormat.Uptime(sys.UptimeSeconds));
                    break;
                case Metric.Mem:
                    DetailTitle.Text = "Memory";
                    DetailSub.Text = UiFormat.Gb(sys.MemTotalBytes) + " total";
                    DetailValue.Text = UiFormat.PercentInt(sys.MemUsedPct);
                    DetailGraph.SetData(Series(s, Metric.Mem), Col("MemBrush"), 100);
                    AddStat("In use", UiFormat.Gb(sys.MemUsedBytes));
                    AddStat("Available", UiFormat.Gb(sys.MemTotalBytes > sys.MemUsedBytes ? sys.MemTotalBytes - sys.MemUsedBytes : 0));
                    AddStat("Committed", UiFormat.Gb(sys.CommitUsedBytes) + " / " + UiFormat.Gb(sys.CommitLimitBytes));
                    AddStat("Total", UiFormat.Gb(sys.MemTotalBytes));
                    break;
                case Metric.Disk:
                    DetailTitle.Text = "Disk";
                    DetailSub.Text = "Physical disks (total)";
                    DetailValue.Text = UiFormat.PercentInt(sys.DiskActivePct);
                    DetailGraph.SetData(Series(s, Metric.Disk), Col("DiskBrush"), 100);
                    AddStat("Active time", UiFormat.PercentInt(sys.DiskActivePct));
                    AddStat("Read speed", UiFormat.DiskRate(sys.DiskReadBps));
                    AddStat("Write speed", UiFormat.DiskRate(sys.DiskWriteBps));
                    break;
                case Metric.Net:
                    DetailTitle.Text = "Network";
                    DetailSub.Text = "Active adapters (total)";
                    DetailValue.Text = UiFormat.PercentInt(sys.NetUtilPct);
                    DetailGraph.SetData(Series(s, Metric.Net), Col("NetBrush"), 100);
                    AddStat("Utilization", UiFormat.PercentInt(sys.NetUtilPct));
                    AddStat("Send", UiFormat.NetRate(sys.NetSentBps));
                    AddStat("Receive", UiFormat.NetRate(sys.NetRecvBps));
                    break;
            }
        }

        private void AddStat(string label, string value)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 12, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)TryFindResource("TextSecondaryBrush")
            });
            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 15,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = (Brush)TryFindResource("TextPrimaryBrush")
            });
            StatsPanel.Children.Add(panel);
        }

        private static float[] Series(Snapshot s, Metric m)
        {
            var h = s.History;
            if (h == null || h.Length == 0) return new float[] { 0, 0 };
            var arr = new float[h.Length];
            for (int i = 0; i < h.Length; i++)
            {
                switch (m)
                {
                    case Metric.Cpu: arr[i] = (float)h[i].CpuPct; break;
                    case Metric.Mem: arr[i] = (float)h[i].MemUsedPct; break;
                    case Metric.Disk: arr[i] = (float)h[i].DiskActivePct; break;
                    default: arr[i] = (float)h[i].NetUtilPct; break;
                }
            }
            return arr;
        }

        private Color Col(string key)
        {
            var b = TryFindResource(key) as SolidColorBrush;
            return b?.Color ?? Color.FromRgb(0x4C, 0xA0, 0xFF);
        }
    }
}
