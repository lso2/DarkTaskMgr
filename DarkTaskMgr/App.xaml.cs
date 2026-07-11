using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DarkTaskMgr.Services;
using DarkTaskMgr.UI;

namespace DarkTaskMgr
{
    /// <summary>
    /// Application entry point. DarkTaskMgr is a windowed monitor, not a tray app, so it opens the
    /// dashboard on launch and exits when that window closes. One shared <see cref="Engine"/> samples
    /// the machine once a second and every tab reads from its snapshot, because a single timer keeps
    /// the tabs consistent and the cost low.
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceName = "DarkTaskMgr.SingleInstance.{C4E7A210}";
        private const string SignalName = "DarkTaskMgr.Activate.{C4E7A210}";

        private Mutex _mutex;
        private bool _ownsMutex;
        private EventWaitHandle _signal;
        private Thread _signalThread;
        private volatile bool _listening;

        public static Engine Engine { get; private set; }
        public static AppHistoryStore History { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Log.Init();
            Log.Info("DarkTaskMgr starting.");

            // A second launch should surface the running window rather than open a duplicate, which
            // matches how Windows treats its own Task Manager as effectively single-instance.
            _mutex = new Mutex(true, InstanceName, out _ownsMutex);
            if (!_ownsMutex)
            {
                try { EventWaitHandle.OpenExisting(SignalName).Set(); } catch { }
                Log.Info("Another instance is running; asked it to activate. Exiting.");
                Shutdown();
                return;
            }

            StartActivationListener();

            try
            {
                History = new AppHistoryStore();
                History.Load();

                Engine = new Engine();
                Engine.Start();
                // Accumulate history on the sampler thread for every sample, regardless of which tab
                // is open, so usage is recorded even when the App history tab is not showing.
                Engine.Updated += (s, snap) => History.Accumulate(snap);

                var win = new MainWindow(Engine);
                MainWindow = win;
                win.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Fatal during startup", ex);
                MessageBox.Show("DarkTaskMgr could not start: " + ex.Message, "DarkTaskMgr",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        // Own the signal handle on the first instance and block a background thread on it. A later
        // launch sets the handle, which wakes this thread so the existing window comes to the front.
        private void StartActivationListener()
        {
            try
            {
                _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
                _listening = true;
                _signalThread = new Thread(() =>
                {
                    while (_listening)
                    {
                        try
                        {
                            if (_signal.WaitOne(500) && _listening)
                                Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
                        }
                        catch { }
                    }
                }) { IsBackground = true, Name = "activate-listener" };
                _signalThread.Start();
            }
            catch (Exception ex) { Log.Error("activation listener", ex); }
        }

        private void ActivateMainWindow()
        {
            var w = MainWindow;
            if (w == null) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Show();
            w.Activate();
            w.Topmost = true;
            w.Topmost = false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _listening = false;
            try { _signal?.Set(); } catch { }
            try { Engine?.Stop(); } catch (Exception ex) { Log.Error("engine stop", ex); }
            try { History?.Save(); } catch (Exception ex) { Log.Error("history save on exit", ex); }
            try { _signal?.Dispose(); } catch { }
            try { if (_ownsMutex) _mutex.ReleaseMutex(); _mutex?.Dispose(); } catch { }
            Log.Info("DarkTaskMgr exited.");
            base.OnExit(e);
        }

        public static bool IsElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // Relaunch elevated so ending other users' processes and stopping services succeed. The
        // single-instance lock is released first so the elevated copy can claim it; if the user
        // dismisses the UAC prompt the current copy simply carries on unelevated.
        public static void RestartElevated()
        {
            try
            {
                string exe = Assembly.GetEntryAssembly().Location;
                var app = (App)Current;
                try { app._listening = false; app._signal?.Set(); } catch { }
                try { if (app._ownsMutex) { app._mutex.ReleaseMutex(); app._ownsMutex = false; } app._mutex?.Dispose(); app._mutex = null; } catch { }
                var psi = new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true, Verb = "runas" };
                System.Diagnostics.Process.Start(psi);
                Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("restart elevated (likely UAC dismissed)", ex);
                try { var app = (App)Current; app._mutex = new Mutex(true, InstanceName, out app._ownsMutex); app._listening = true; } catch { }
            }
        }
    }
}
