# MachineKarteWebCore

ASP.NET Core application for the E-Karte machine-maintenance workflows, converted
from the VB WinForms sources preserved in `Legacy/`.

Monitoring is the main screen. Summaries, action cards, OCAP, raw data, and
PM/TE reports open within it as modal workflows.

## Development

Use a .NET SDK that supports the project's `net8.0` target:

```powershell
dotnet restore MachineKarteWebCore.csproj
dotnet build MachineKarteWebCore.csproj --no-restore
dotnet run --project MachineKarteWebCore.csproj --no-build
```

Configure `ConnectionStrings:MachineDBConnection` and
`ConnectionStrings:IntranetDBConnection` for the intended environment before
running the application. Machine is primary; Intranet is the automatic fallback.
Old-only `DBConnection` configurations remain supported by the central resolver.
Monitoring session tracking and Action Card saves can write to the selected
database; see the [database guide](Documentation/DATABASE.md) for resolution,
timeouts, recovery and test isolation.

## Documentation

- [Application structure, workflow rules, and validation](Documentation/README.md)
- [Database behavior and performance](Documentation/DATABASE.md)
- [Report templates, image storage, deployment, and known gaps](Documentation/REPORTS.md)
- [JavaScript structure and conventions](Assets/js/README.md)

Generated reports use the PM/TE server `TempData\Print Out\<PROCESS>` folders;
see the reporting guide for roots, permissions, and local test overrides.
Local `TempData/` is excluded from Git. Audit inventories and validation artifacts
default to the operating system's temporary directory.
