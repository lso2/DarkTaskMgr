# DarkTaskMgr

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6.svg?logo=windows&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-7.3-239120.svg?logo=csharp&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF-1f6feb.svg)
![Version](https://img.shields.io/badge/Version-1.1.0-success.svg)
![License](https://img.shields.io/badge/License-GPLv3-blue.svg)

A dark-theme Task Manager for Windows 10. DarkTaskMgr reproduces the layout and every tab of the built-in Task Manager in the dark theme Windows 10 never shipped, and refines a few things along the way. It runs locally as a single app, needs no runtime to install because .NET Framework 4.8 ships with Windows, and asks for administrator rights only when an action genuinely needs them.

![DarkTaskMgr Processes tab in dark theme, showing per-application grouping with counts, amber heat-map cells, aggregate column percentages, and process icons](screens/darktaskmgr%20screen%201.jpg)

## Summary

DarkTaskMgr gives Windows 10 the dark Task Manager it is missing, with:

- **A true dark reproduction**: the same seven tabs and layout as the built-in tool, recolored end to end
- **Grouped, heat-mapped processes**: applications aggregate with an instance count, and CPU, Memory, Disk, and Network cells carry the same amber intensity shading
- **Live performance graphs**: a mini-graph rail and a large per-resource graph for CPU, memory, disk, and network
- **Persistent app history**: per-app CPU time and logged high-usage spikes that survive restarts, viewable by Today, this week, this month, or all time
- **Startup control**: enable or disable sign-in programs by writing the same record Windows honors
- **Users, Details, Services**: per-session rollups, a full per-process table with priority control, and start/stop/restart for services
- **An icon on every row**: image paths resolved from several sources so system and third-party service processes are not left blank
- **Local and lightweight**: no admin by default, no runtime install, and one background sampler for the whole app

## Features

### Processes

The Processes tab groups running processes by application, with an instance count next to each app and an expander that reveals the individual processes underneath. Each row shows CPU, Memory, Disk, and Network, and every resource cell carries an amber heat shade so the busiest work is visible at a glance. The column headers show the live system percentage over each label, clicking a header sorts by it, and a View option regroups the list into Apps, Background processes, and Windows processes. Right-click a row to end the task, open its file location, or search for it online.

### Performance

The Performance tab puts a rail of live mini-graphs on the left, one per resource, and a large graph with the selected resource's figures on the right. CPU reports utilization, effective clock, and the process, thread, and handle counts plus up time; Memory reports in use and committed; Disk reports active time and read and write speed; Network reports utilization and send and receive rates.

![The Performance tab with the CPU, Memory, Disk, and Network mini-graph rail on the left and the large CPU utilization graph with figures on the right](screens/darktaskmgr%20screen%202.jpg)

### App history

The App history tab keeps usage that lasts. Each application's CPU time is accumulated and every high-usage spike is logged with its time, and both are written to disk so they survive a restart. Time-range chips at the top switch between Today, this week, this month, and all time, with Today selected by default. The busiest applications are surfaced as cards on the left with their CPU time, peak CPU and when it occurred, first-seen and last-active times, and recent spikes, while the full set of applications sits in the table on the right. The divider between the two is draggable.

![The App history tab with Today, This week, This month, and All time chips, a grid of high-usage application cards on the left, and the full application table on the right](screens/darktaskmgr%20screen%203.jpg)

### Startup

The Startup tab lists the programs that launch at sign-in from both Run keys and both Startup folders, shows each one's publisher and whether it is enabled, and lets you enable or disable it by writing the same StartupApproved record that Windows honors.

### Users, Details, and Services

The Users tab groups the process snapshot into the sessions signed in to the machine and shows each session's process count and rolled-up CPU, memory, and disk. The Details tab is the flat, per-process table with name, PID, status, user name, CPU, memory, and description, and from its right-click menu you can end a task, open its file location, or set its priority. The Services tab lists Windows services with their PID, description, status, and load-order group, and starts, stops, or restarts the selected service through the service control manager.

### An icon on every row

Task Manager clones usually leave system processes without an icon, because a normal-user app cannot open a higher-integrity process to read its image path. DarkTaskMgr resolves the path from several sources in turn: the process directly, WMI's process list, the service configuration in the registry (which fills third-party service icons without elevation), and the System32 image for core Windows processes. Icons are loaded through the shell, and the few processes that have no executable on disk fall back to the Windows default-application icon rather than a blank square, so a row is never left without an icon.

### Refinements over the original

The window has a rounded, fully dark chrome, a centered loading indicator while the samplers warm up, smooth pixel scrolling, and list selection that dims when the window loses focus. One background sampler shared by every tab means switching tabs is instant and the readings never disagree between views.

## Requirements

- **Windows 10 or Windows 11**, 64-bit
- **.NET Framework 4.8** runtime, which is preinstalled on current Windows
- To build from source: **Visual Studio 2019 or 2022** with the .NET Framework 4.8 targeting pack
- To build the installer: **[Inno Setup](https://jrsoftware.org/isinfo.php)** 6 or later

## Installation

### Option A: Run a release build

1. Download the latest `DarkTaskMgrSetup.exe` from the Releases page (or build it yourself below)
2. Run the installer. It installs per-user with no administrator rights, adds a Start Menu entry and an optional Desktop shortcut, optionally adds a "Dark Task Manager" entry to the desktop right-click menu, and starts the app
3. The window opens, showing the Processes tab

### Option B: Build from source

```bash
git clone https://github.com/lso2/DarkTaskMgr.git
```

**Build the app**

1. Open `app/DarkTaskMgr.sln` in Visual Studio
2. Select the **Release | x64** configuration (x64 only)
3. Build the solution. The executable is produced at `app/DarkTaskMgr/bin/x64/Release/DarkTaskMgr.exe`
4. Run `DarkTaskMgr.exe`

**Build the installer (optional)**

1. Build the app first so `DarkTaskMgr.exe` exists at the path referenced in `[Files]`
2. Open `app/Installer.iss` in Inno Setup and press **Compile**
3. The single-file `DarkTaskMgrSetup.exe` is produced in the output folder

> The installer auto-detects your build: it uses the `Release\x64` output if present, otherwise the `Debug\x64` output, so a plain Visual Studio build (which defaults to Debug) still compiles the installer.

You can also install without building the setup by running `app/Install.ps1`, which copies the built executable into `%LOCALAPPDATA%\DarkTaskMgr`, creates the shortcuts and the desktop right-click entry, and launches it:

```
powershell -ExecutionPolicy Bypass -File .\app\Install.ps1
```

## Usage

### Reading the Processes tab

The column headers show the machine-wide percentage for CPU, Memory, Disk, and Network. Rows are grouped by application with an instance count; expand one to see its individual processes. Click a column header to sort by it, and use **View** to group by type or expand and collapse all applications. Right-click a row for End task, Open file location, and Search online.

### Administrator rights

DarkTaskMgr launches without elevation so it opens instantly with no prompt, and viewing everything works as a normal user. Ending a process owned by another account, stopping a protected service, or disabling a machine-wide startup entry needs rights the standard token lacks, so the **Options** menu offers a one-click **Run as administrator** that relaunches the app elevated. When an action is denied, the app says so rather than failing silently.

### App history ranges

Pick **Today**, **This week**, **This month**, or **All time**. Today shows clock times; the wider ranges include the date. The cards list the busiest applications with their peak and spike times, and the table lists every application in the range.

### Options

The **Options** menu toggles Always on top, the update speed (High, Normal, Low, or Paused), and Run as administrator. The **File** menu runs a new task or exits, and **View** refreshes now and controls grouping.

## Settings and data

All data is stored locally under your user profile:

```
%APPDATA%\DarkTaskMgr\
├── debug.log              # Diagnostic log (never the Windows Event Log)
└── history\
    ├── daily.dat          # Per-application CPU rollups, one record per day
    └── events.dat         # Logged high-usage spike events with their times
```

### Privacy

- **No cloud sync**: everything stays on your computer
- **No accounts**: no login or registration
- **No telemetry**: DarkTaskMgr makes no external network requests
- **Readable formats**: plain tab-separated history files you can open, back up, or delete

## Compatibility

| OS | Supported | Notes |
|----|-----------|-------|
| Windows 11 | ✅ | Full support |
| Windows 10 (incl. LTSC) | ✅ | Developed and tested here |
| Windows 8.1 | ⚠️ | Untested; some performance counters and WMI classes differ |
| Windows 7 | ⚠️ | Untested; counter availability differs |
| macOS / Linux | ❌ | Windows-only (WPF, performance counters, WMI) |

## Troubleshooting

### A process shows a generic icon

A few core processes such as System, Registry, and the memory-compression process have no executable on disk, so they show the Windows default-application icon, exactly as the built-in Task Manager does. Running DarkTaskMgr as administrator lets it read the image path of the remaining protected processes directly, so their real icons appear.

### End task or a service action is denied

Ending a process owned by another account or controlling a protected service needs administrator rights. Use **Run as administrator** from the Options menu and try again.

### Per-process Network always reads zero

Attributing network throughput to individual processes needs Event Tracing for Windows, which requires a kernel session and administrator rights that this app deliberately avoids. The per-process Network column reads zero, while the system-wide Network graph on the Performance tab is fully live.

### App history only covers time the app was open

Windows builds its own history from a long-term usage database that is not open to a normal app. DarkTaskMgr records usage while it is running and persists it, so This month and All time contain the days the app was actually open rather than system-wide history from before it existed.

### Build errors about C# syntax

The project uses the C# 7.3 language version that is the default for .NET Framework 4.8. Build with the .NET Framework 4.8 toolchain rather than retargeting the project to a newer framework.

## How it works

DarkTaskMgr is a WPF application that reads only documented performance counters, WMI, and process APIs, and installs no driver of its own. The core ideas:

- **One background sampler** runs about once a second on its own thread and every tab reads from its snapshot, which keeps the tabs consistent, holds the cost to roughly a millisecond of work per second, and lets the window open instantly while the counters initialize off the UI thread
- **System metrics** come from the counters behind Task Manager's own numbers: `% Processor Time` and `% Processor Performance`, `Committed Bytes` and `Commit Limit`, `PhysicalDisk` activity and transfer rates, and the `Network Interface` byte and bandwidth counters, plus `GlobalMemoryStatusEx` for physical memory
- **Per-process CPU** is the growth in each process's total processor time across the interval over wall time times the logical core count, and **per-process disk** comes from `GetProcessIoCounters`
- **Image paths** for icons are resolved with `QueryFullProcessImageName`, then WMI `Win32_Process` and `Win32_Service`, then the System32 image, so system and service processes are not left blank, and icons are loaded through the shell's `SHGetFileInfo`
- **App history** rolls per-application CPU up per day and logs each high-usage spike, persisted as plain files under `%APPDATA%` and aggregated by range on demand
- **Startup, services, and users** read the registry, WMI `Win32_Service` and the service control manager, and the process snapshot grouped by session

## Project structure

```
DarkTaskMgr/
├── README.md
├── screens/                          # Screenshots used in this README
└── app/
    ├── DarkTaskMgr.sln
    ├── Installer.iss                 # Inno Setup installer script
    ├── Install.ps1                   # No-admin PowerShell installer
    ├── CHANGELOG.md
    └── DarkTaskMgr/
        ├── DarkTaskMgr.csproj        # .NET Framework 4.8, x64, WPF
        ├── app.manifest              # Per-monitor DPI, runs without elevation
        ├── darktaskmgr.ico
        ├── App.xaml(.cs)             # Single-instance host, history, lifecycle
        ├── Themes/
        │   ├── Theme.Dark.xaml        # Dark palette
        │   ├── Controls.xaml          # Buttons, tabs, chips, scrollbars
        │   └── Lists.xaml             # Table, headers, rows, menus
        ├── Services/
        │   ├── PerfSampler.cs         # System CPU, memory, disk, network counters
        │   ├── ProcessProvider.cs     # Per-process CPU, memory, disk, owner, image path
        │   ├── NativeMethods.cs       # Memory, I/O counters, token owner, image path
        │   ├── StartupProvider.cs     # Run keys and Startup folders
        │   ├── ServicesProvider.cs    # WMI services and the control manager
        │   ├── UsersProvider.cs       # Sessions from the process snapshot
        │   ├── AppHistoryStore.cs     # Persistent per-app rollups and spike events
        │   ├── Engine.cs              # The one-second sampling loop
        │   └── Log.cs
        └── UI/
            ├── MainWindow.xaml(.cs)   # Chrome, menu, tabs, routing, loading overlay
            ├── ProcessesView          # Grouped, sortable, heat-map table
            ├── PerformanceView        # Rail plus large graph
            ├── AppHistoryView         # Range chips, cards, table
            ├── StartupView            # Enable and disable at sign-in
            ├── UsersView              # Per-session rollup
            ├── DetailsView            # Flat per-process table
            ├── ServicesView           # Start, stop, restart
            ├── PerfGraph.cs           # The code-drawn area graph
            ├── ProcessRowModel.cs     # Row view-model and tree controller
            ├── IconCache.cs           # Shell icon loading and caching
            ├── ScrollHelper.cs        # Smooth wheel scrolling
            ├── Converters.cs          # Heat and indent converters
            ├── RunDialog.cs           # The Run new task prompt
            └── UiFormat.cs            # Shared number formatting
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes (keep to documented counters and the existing C# 7.3 / .NET Framework 4.8 target)
4. Build and test on Windows 10 or 11
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## Author

PlexPixel ([plexpixel.com](https://plexpixel.com))

## License

Copyright (C) 2026 PlexPixel

DarkTaskMgr is licensed under the GNU General Public License version 3 (GPL-3.0-only). The full text is in [LICENSE](LICENSE). Sections 15 and 16 of the license disclaim any warranty and limit liability.

---

**⭐ If DarkTaskMgr gave Windows 10 the dark Task Manager it was missing, please star the repository!**

## ☕ Buy me a coffee

If DarkTaskMgr earns a place on your machine, consider supporting its development.

[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://plexpixel.com/donate)

**Why support?**

- ☕ Fuel continued development
- 🚀 New views and refinements
- 🐛 Faster fixes and updates
- 📚 Better documentation

---

*Made with ❤️ for people who keep Task Manager open.*
