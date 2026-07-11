# Changelog

## 1.0.0

First release.

- Dark-theme reproduction of the Windows 10 Task Manager, built in C# and WPF on .NET Framework 4.8, running as a normal user with no runtime to install.
- All seven tabs: Processes, Performance, App history, Startup, Users, Details, and Services.
- Processes tab groups by application with instance counts and an expander for the child processes, amber heat-map cells for CPU, Memory, Disk, and Network, live system percentages in the column headers, click-to-sort, and an optional grouping into Apps, Background processes, and Windows processes.
- Performance tab with a live mini-graph rail and a large per-resource graph, reporting CPU utilization and effective clock, memory in use and committed, disk activity and transfer rates, and network utilization and throughput.
- Details tab with per-process end task, set priority, and open file location; Services tab with start, stop, and restart through the service control manager; Startup tab that enables and disables sign-in items by writing the StartupApproved record Windows honors.
- One background sampler shared by every tab, so switching tabs is instant and the readings never disagree.
- Runs without elevation, with a one-click relaunch as administrator for actions that need it, and a clear message when an action is denied.
- No-admin PowerShell installer and an Inno Setup installer that add a Start Menu and optional Desktop shortcut.
