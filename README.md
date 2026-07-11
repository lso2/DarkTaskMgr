# DarkTaskMgr

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6.svg?logo=windows&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-7.3-239120.svg?logo=csharp&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF-1f6feb.svg)
![Version](https://img.shields.io/badge/Version-1.0.0-success.svg)

A dark-theme Task Manager for Windows 10. It reproduces the layout and every tab of the built-in Task Manager, in a dark theme that Windows 10 never shipped, and adds a few small refinements along the way. It runs locally as a normal user, needs no runtime to install because .NET Framework 4.8 ships with Windows, and asks for administrator rights only when you take an action that genuinely needs them.

## Why a separate app rather than a skin

Windows Task Manager (`taskmgr.exe`) is a protected system process, so there is no supported way to recolor it from the outside. Reaching into it would mean injecting code into a system process, which is fragile, breaks on every servicing update, and looks exactly like malware to the security stack. The clean and durable approach is a standalone application that reads the same public performance counters, WMI classes, and process APIs that Task Manager itself reads, and presents them in a dark interface. That is what DarkTaskMgr is, so nothing is patched, hooked, or replaced on your system.

## The seven tabs

**Processes** groups the running processes by application the way Task Manager does, with an instance count next to each app and an expander that reveals the individual processes underneath. Each row shows CPU, Memory, Disk, and Network, and every resource cell carries the same amber heat shading as the original so the busiest work is visible at a glance. The column headers show the live system percentage over each label, clicking a header sorts by it, and a View option regroups the list into Apps, Background processes, and Windows processes.

**Performance** puts a rail of live mini-graphs on the left, one per resource, and a large graph with the selected resource's figures on the right. CPU reports utilization, effective clock, process, thread, and handle counts, and up time; Memory reports in use, available, and committed; Disk reports active time and read and write speed; Network reports utilization and send and receive rates.

**App history** accumulates CPU time per application for the current session. Windows builds its own history from a long-term usage database that is not available to a normal app, so this integrates each application's CPU share over time into an honest, self-contained figure that resets when DarkTaskMgr restarts.

**Startup** lists the programs that launch at sign-in from both Run keys and both Startup folders, shows each one's publisher and whether it is enabled, and lets you enable or disable it by writing the same StartupApproved record that Windows honors.

**Users** groups the process snapshot into the sessions signed in to the machine and shows each session's process count and rolled-up CPU, memory, and disk.

**Details** is the flat, per-process table with name, PID, status, user name, CPU, memory, and description. From the right-click menu you can end a task, open its file location, or set its priority.

**Services** lists Windows services with their PID, description, status, and load-order group, and lets you start, stop, or restart the selected service through the service control manager.

## How it reads the machine

DarkTaskMgr uses only documented, local sources and installs no driver of its own:

- **System metrics** come from the performance counters behind Task Manager's own numbers: `% Processor Time` and `% Processor Performance`, `Committed Bytes` and `Commit Limit`, `PhysicalDisk` activity and transfer rates, and the `Network Interface` byte and bandwidth counters, plus `GlobalMemoryStatusEx` for physical memory.
- **Per-process CPU** is the growth in each process's total processor time across the interval over wall time times the logical core count, which is the same basis the built-in tool uses.
- **Per-process disk** comes from `GetProcessIoCounters`, whose read and write transfer bytes are sampled each interval and differenced into a rate.
- **Process ownership** is resolved from each process's token and cached, so the Details and Users tabs can attribute work to an account.
- **Startup, services, and memory** read the registry, WMI `Win32_Service`, and the service control manager directly.

A single background sampler runs once a second and every tab reads from its snapshot, which keeps the tabs consistent and holds the cost to about a millisecond of work per second.

## Requirements

- **Windows 10 or Windows 11**, 64-bit
- **.NET Framework 4.8** runtime, which is preinstalled on current Windows
- To build from source: **Visual Studio 2019 or 2022** with the .NET Framework 4.8 targeting pack
- To build the installer: **[Inno Setup](https://jrsoftware.org/isinfo.php)** 6 or later

## Installation

### Option A: Run a release build

Run `DarkTaskMgrSetup.exe`. It installs per-user with no administrator rights, adds a Start Menu entry and an optional Desktop shortcut, and starts the app.

### Option B: Build from source

Open `DarkTaskMgr.sln` in Visual Studio, select the **Release | x64** configuration, and build. The executable is produced at `DarkTaskMgr\bin\x64\Release\DarkTaskMgr.exe`.

To install without building the setup, run `Install.ps1`, which copies the built executable into `%LOCALAPPDATA%\DarkTaskMgr`, creates the shortcuts, and launches it:

```
powershell -ExecutionPolicy Bypass -File .\Install.ps1
```

To build the single-file installer, build the app first so the executable exists, then open `Installer.iss` in Inno Setup and press Compile. It auto-detects the Release output, falling back to Debug, so a plain Visual Studio build still compiles the installer.

## Administrator rights

DarkTaskMgr launches without elevation so it opens instantly with no prompt, and viewing everything works as a normal user. Ending a process owned by another account, stopping a protected service, or disabling a machine-wide startup entry needs rights the standard token lacks, so the Options menu offers a one-click **Run as administrator** that relaunches the app elevated. When an action is denied, the app says so rather than failing silently.

## Refinements over the original

The window has a rounded, fully dark chrome, the graphs are redrawn cleanly, and the list selection dims when the window loses focus so the active row is never ambiguous. The single one-second sampler shared by every tab means switching tabs is instant and the readings never disagree between views.

## Known limits

- **Per-process Network** attribution needs Event Tracing for Windows, which requires a kernel session and administrator rights that this app deliberately avoids, so the Network column on the process tabs reads zero while the system-wide network graph on the Performance tab is fully live.
- **App history** covers the current monitoring session rather than weeks of history, because the long-term usage database Windows uses is not exposed to a normal application.

## Data and privacy

Everything stays on your computer. There are no accounts, no network requests, and no telemetry. The only file the app writes is a rolling diagnostic log at `%APPDATA%\DarkTaskMgr\debug.log`.

## Project structure

```
DarkTaskMgr/
├── DarkTaskMgr.sln
├── Installer.iss                 # Inno Setup installer script
├── Install.ps1                   # No-admin PowerShell installer
├── README.md
├── CHANGELOG.md
└── DarkTaskMgr/
    ├── DarkTaskMgr.csproj        # .NET Framework 4.8, x64, WPF
    ├── app.manifest              # Per-monitor DPI, runs without elevation
    ├── darktaskmgr.ico
    ├── App.xaml(.cs)             # Single-instance host and lifecycle
    ├── Themes/
    │   ├── Theme.Dark.xaml        # Dark palette
    │   ├── Controls.xaml          # Buttons, tabs, scrollbars
    │   └── Lists.xaml             # Table, headers, rows, menus
    ├── Services/
    │   ├── PerfSampler.cs         # System CPU, memory, disk, network counters
    │   ├── ProcessProvider.cs     # Per-process CPU, memory, disk, owner
    │   ├── NativeMethods.cs       # Memory, I/O counters, token owner
    │   ├── StartupProvider.cs     # Run keys and Startup folders
    │   ├── ServicesProvider.cs    # WMI services and the control manager
    │   ├── UsersProvider.cs       # Sessions from the process snapshot
    │   ├── Engine.cs              # The one-second sampling loop
    │   └── Log.cs                 # Plain file logger
    └── UI/
        ├── MainWindow.xaml(.cs)   # Chrome, menu, tabs, routing
        ├── ProcessesView          # Grouped, sortable, heat-map table
        ├── PerformanceView        # Rail plus large graph
        ├── AppHistoryView         # Session CPU-time accumulation
        ├── StartupView            # Enable and disable at sign-in
        ├── UsersView              # Per-session rollup
        ├── DetailsView            # Flat per-process table
        ├── ServicesView           # Start, stop, restart
        ├── PerfGraph.cs           # The code-drawn area graph
        ├── ProcessRowModel.cs     # Row view-model and tree controller
        ├── Converters.cs          # Heat and indent converters
        └── UiFormat.cs            # Shared number formatting
```

## Author

PlexPixel ([plexpixel.com](https://plexpixel.com))

## License

MIT.
