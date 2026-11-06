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

Configure `ConnectionStrings:DBConnection` for the intended environment before
running the application. Monitoring session tracking and Action Card saves can
write to the configured database; see the database guide for test isolation.

## Documentation

- [Application structure, workflow rules, and validation](Documentation/README.md)
- [Database behavior and performance](Documentation/DATABASE.md)
- [Report templates, image storage, deployment, and known gaps](Documentation/REPORTS.md)
- [JavaScript structure and conventions](Assets/js/README.md)

`TempData/` contains disposable generated output and is excluded from Git.
The application creates `TempData/GeneratedPdf/` when needed. Report audit
inventories default to the operating system's temporary directory.
