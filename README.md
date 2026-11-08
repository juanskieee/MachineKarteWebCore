# E-Karte Web System

E-Karte is a machine-maintenance monitoring and reporting application for
PM, TE, CC, EEE, and VIS workflows. It brings machine histories, action tracking,
process summaries, and maintenance reports into a single web interface.

## Capabilities

- **Machine monitoring** — Filter breakdown-maintenance histories by user,
  process, location, machine, and date range.
- **Action tracking** — Review recurring issues, important devices, action cards,
  and management comments.
- **Process summaries** — View need-action and action-with-MCD counts, with
  machine drilldowns by process and location.
- **Maintenance records** — Access OCAP information, historical data, PM reports,
  and TE maintenance evidence.
- **Report generation** — Generate Excel workbooks and PDF reports from the
  supplied templates, then download them directly to the browser.

Monitoring is the primary screen. Supporting workflows open in dialogs so users
can review records and return to the same monitoring context.

## Technology

| Layer | Implementation |
| --- | --- |
| Application | .NET 8, ASP.NET Core Razor Pages and MVC |
| Data | SQL Server with primary and fallback connection resolution |
| Interface | JavaScript, HTML, and CSS |
| Reporting | GemBox.Spreadsheet with compiled Visual Basic workbook mappings |

The repository retains the original VB WinForms sources in [Legacy](Legacy/)
as the reference for maintenance business rules and report mappings.

## Documentation

- [Application and development guide](Documentation/README.md)
- [Monitoring behavior and filters](Documentation/MONITORING.md)
- [Database configuration and behavior](Documentation/DATABASE.md)
- [Reporting, templates, and file access](Documentation/REPORTS.md)
- [JavaScript architecture and conventions](Assets/js/README.md)
