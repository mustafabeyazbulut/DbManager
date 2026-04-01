# DbManager

A Windows Forms app to manage SQL Server databases.

## Features

- **Server Info** — Version, edition, total/online/offline DB count, disk usage
- **Database List** — Status, recovery model, size, last backup date, read-only info
- **Table / Procedure / File Details** — Detailed view of a selected database
- **Backup** — Takes a full backup of a selected database
- **Shrink** — Makes a database or log file smaller
- **Index Management** — Fragmentation check; rebuild / reorganize operations
- **Active Connections** — Session, CPU, memory, wait type, blocking info
- **Long Running Queries** — Running queries and SQL text
- **Lock Info** — Locked resources and wait times
- **TempDB Usage** — TempDB usage per session
- **Users** — Database users and role memberships
- **SQL Agent Jobs** — Job list, last run time and status
- **Excel Export** — All grid views can be exported as xlsx
- **Logging** — Actions are saved to `Log/DatabaseLog/` folder

## Requirements

| Component | Version |
|---|---|
| .NET Framework | 4.8 |
| DevExpress WinForms | 21.2 |
| Microsoft SQL Server | 2012+ |

> A DevExpress license is needed. Libraries go in `bin/Debug/` but are not tracked by `.gitignore`.

## Setup

1. Clone the repo:
   ```
   git clone <repo-url>
   ```
2. Open `DbManager.sln` with Visual Studio 2019/2022.
3. Make sure DevExpress 21.2 is installed.
4. Set the connection string in `App.config` for your SQL Server.
5. Build and run: `F5` or `Ctrl+F5`.

## How to Use

Open the app. Enter your connection info. Connect to the server. Pick a category from the left panel. Data shows in the DevExpress grid. Use the toolbar buttons to start backup, shrink, or index rebuild.

## Project Structure

```
DbManager/
├── Form1.cs               # Main form and UI logic
├── Form1.Designer.cs      # Designer generated code
├── DatabaseHelper.cs      # SQL Server queries and database operations
├── GridHelper.cs          # DevExpress grid helpers and Excel export
├── Program.cs             # Entry point
├── App.config             # App configuration
└── Properties/            # AssemblyInfo, Resources, Settings
```
