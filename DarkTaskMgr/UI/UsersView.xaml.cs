using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using DarkTaskMgr.Services;

namespace DarkTaskMgr.UI
{
    public sealed class UserRow : INotifyPropertyChanged
    {
        public int SessionId;
        private string _user, _status, _proc, _cpu = "0%", _mem, _disk;
        public string UserName { get { return _user; } set { Set(ref _user, value, "UserName"); } }
        public string StatusText { get { return _status; } set { Set(ref _status, value, "StatusText"); } }
        public string ProcText { get { return _proc; } set { Set(ref _proc, value, "ProcText"); } }
        public string CpuText { get { return _cpu; } set { Set(ref _cpu, value, "CpuText"); } }
        public string MemText { get { return _mem; } set { Set(ref _mem, value, "MemText"); } }
        public string DiskText { get { return _disk; } set { Set(ref _disk, value, "DiskText"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Set<T>(ref T f, T v, string n)
        {
            if (!EqualityComparer<T>.Default.Equals(f, v)) { f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        }
    }

    /// <summary>The Users tab: the process snapshot grouped into sign-in sessions, with each session's
    /// process count and rolled-up CPU, memory, and disk updated live.</summary>
    public partial class UsersView : UserControl, ITabView
    {
        private readonly Dictionary<int, UserRow> _bySession = new Dictionary<int, UserRow>();
        private readonly System.Collections.ObjectModel.ObservableCollection<UserRow> _rows =
            new System.Collections.ObjectModel.ObservableCollection<UserRow>();

        public UsersView(Engine engine)
        {
            InitializeComponent();
            List.ItemsSource = _rows;
        }

        public void OnActivated(Snapshot latest) { if (latest != null) OnSnapshot(latest); }
        public void OnDeactivated() { }

        public void OnSnapshot(Snapshot s)
        {
            if (s?.Processes == null) return;
            var sessions = UsersProvider.Build(s.Processes);

            var live = new HashSet<int>();
            var desired = new List<UserRow>(sessions.Count);
            foreach (var sess in sessions)
            {
                live.Add(sess.SessionId);
                if (!_bySession.TryGetValue(sess.SessionId, out var r))
                {
                    r = new UserRow { SessionId = sess.SessionId };
                    _bySession[sess.SessionId] = r;
                }
                r.UserName = sess.UserName;
                r.StatusText = sess.Status;
                r.ProcText = sess.ProcessCount.ToString();
                r.CpuText = UiFormat.Percent1(sess.CpuPct);
                r.MemText = UiFormat.Mb(sess.MemBytes);
                r.DiskText = UiFormat.DiskRate(sess.DiskBps);
                desired.Add(r);
            }
            foreach (var dead in new List<int>(_bySession.Keys))
                if (!live.Contains(dead)) _bySession.Remove(dead);

            Reconcile(desired);
        }

        private void Reconcile(List<UserRow> desired)
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
    }
}
