using System;
using System.Collections.Generic;
using System.Threading;

namespace DarkTaskMgr.Services
{
    /// <summary>One second of the whole machine: the system reading, every process, the rolled-up
    /// totals shown on the Performance tab, and a short history for the graphs.</summary>
    public sealed class Snapshot
    {
        public SystemStats System;
        public List<ProcInfo> Processes;
        public int ProcessCount;
        public long ThreadTotal;
        public long HandleTotal;
        public SystemStats[] History;
        public int LogicalProcessors;
        public double BaseGhz;
    }

    /// <summary>
    /// The single sampling loop every tab reads from. It runs on its own background thread so the UI
    /// never stalls while several hundred processes are enumerated, and it raises <see cref="Updated"/>
    /// off that thread, so subscribers marshal to the dispatcher themselves. One loop keeps every tab
    /// consistent and holds the cost to about a millisecond of work per second.
    /// </summary>
    public sealed class Engine
    {
        public event EventHandler<Snapshot> Updated;

        private PerfSampler _perf;
        private ProcessProvider _procs;
        private Thread _thread;
        private volatile bool _running;
        private readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);

        private readonly object _histGate = new object();
        private readonly LinkedList<SystemStats> _history = new LinkedList<SystemStats>();
        private const int HistoryLen = 60;

        public int IntervalMs { get; set; }
        public bool Paused { get; set; }
        public Snapshot Latest { get; private set; }

        public Engine()
        {
            IntervalMs = 1000;
        }

        // The window must appear instantly, so the samplers are built on the background thread rather
        // than here. Creating the performance counters is slow the first time in a process (the
        // counter infrastructure initializes on first use), and doing it on the UI thread was what
        // made the app take seconds to open.
        public void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "sampler" };
            _thread.Start();
        }

        /// <summary>Force an immediate sample outside the normal cadence (used on tab activation).</summary>
        public void RequestRefresh() { _wake.Set(); }

        private void Loop()
        {
            try
            {
                _perf = new PerfSampler();
                _procs = new ProcessProvider();
            }
            catch (Exception ex) { Log.Error("sampler init", ex); return; }

            while (_running)
            {
                try
                {
                    if (!Paused)
                    {
                        var snap = SampleOnce();
                        Latest = snap;
                        Updated?.Invoke(this, snap);
                    }
                }
                catch (Exception ex) { Log.Error("sample loop", ex); }

                int wait = Math.Max(250, IntervalMs);
                _wake.Wait(wait);
                _wake.Reset();
            }
        }

        private Snapshot SampleOnce()
        {
            SystemStats sys = _perf.Read();
            List<ProcInfo> procs = _procs.Sample();

            long threads = 0, handles = 0;
            foreach (var p in procs) { threads += p.ThreadCount; handles += p.HandleCount; }

            SystemStats[] hist;
            lock (_histGate)
            {
                _history.AddLast(sys);
                while (_history.Count > HistoryLen) _history.RemoveFirst();
                hist = new SystemStats[_history.Count];
                _history.CopyTo(hist, 0);
            }

            return new Snapshot
            {
                System = sys,
                Processes = procs,
                ProcessCount = procs.Count,
                ThreadTotal = threads,
                HandleTotal = handles,
                History = hist,
                LogicalProcessors = Environment.ProcessorCount,
                BaseGhz = _perf.BaseGhz
            };
        }

        public void Stop()
        {
            _running = false;
            _wake.Set();
            try { _thread?.Join(1500); } catch { }
            try { _perf?.Dispose(); } catch { }
        }
    }
}
