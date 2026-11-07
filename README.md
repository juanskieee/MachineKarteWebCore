# MachineKarteWebCore

ASP.NET Core application for the E-Karte machine-maintenance workflows, converted
from the VB WinForms sources preserved in `Legacy/`.

Monitoring is the main screen. Summaries, action cards, OCAP, raw data, and
PM/TE reports open within it as modal workflows.

## Working rules

- At the start of every prompt, read this README and the relevant documentation
  linked below before investigating, editing, or running checks.
- Always check the active legacy behavior in the relevant `Legacy/` form,
  designer, and callers before making changes. Preserve business rules and
  follow documented, approved web interaction changes.
- DO NOT RUN EXCESS TESTS THAT IS NOT CONNECTED OR NEEDED FOR THE TASKS, CONSUME LESS TIME AS MUCH AS POSSIBLE AND DO MORE TASKS.
  Run required checks and the smallest focused validation for the affected
  behavior; avoid unrelated suites and repeated passing checks.

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

Web configuration no longer needs `VersionPath` or `AppVersion`.
Optional PC status tracking records the deployed web assembly version instead
of a manually configured version. Deploy a new web build to update the app;
the web app does not use the desktop updater share.

## Tests and database coverage

[`scripts/`](scripts/) contains a mix of automated validation and maintenance
tools, rather than a dedicated unit-test suite:

| Check type | What it validates |
| --- | --- |
| Offline logic/parity checks | Isolated rules, generated SQL, and legacy behavior without a database; some are unit-style checks |
| LocalDB integration checks | Real repository SQL and saves against disposable fixture data, without loading the app's connection strings |
| HTTP/browser checks | Web routes, HTML, assets, and UI behavior; browser AJAX responses are mocked |

The web app can use its configured remote SQL database. Passing the scripts does
not prove that database's schema, permissions, data, or performance. HTTP/browser
tools accept `-BaseUrl` for a reachable hosted app, but their current checks do
not validate its connected database. Real database acceptance requires separate
integration checks against a suitable test SQL Server instance; the existing
LocalDB scripts cannot target a remote database as written. Some application
queries name `eMaintenanceSystemDB` explicitly, so changing only the catalog
does not isolate them. See the [validation index](Documentation/README.md#validation)
for each script's scope and the [database guide](Documentation/DATABASE.md)
for test isolation.

## Documentation

- [Application structure, workflow rules, and validation](Documentation/README.md)
- [Database behavior and performance](Documentation/DATABASE.md)
- [Report templates, image storage, deployment, and known gaps](Documentation/REPORTS.md)
- [JavaScript structure and conventions](Assets/js/README.md)

Generated reports use the PM/TE server `TempData\Print Out\<PROCESS>` folders;
see the reporting guide for roots, permissions, and local test overrides.
Local `TempData/` is excluded from Git. Audit inventories and validation artifacts
default to the operating system's temporary directory.
