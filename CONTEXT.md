# MachineKarteWebCore - Project Context

## Purpose

ASP.NET Core Razor Pages application porting legacy WinForms reports and screens to the web. The active conversions are Machine Summary and BM Maintenance Record.

## Stack

- .NET 8 / ASP.NET Core Razor Pages
- ClosedXML 0.105.1 for Excel generation
- Microsoft.Data.SqlClient for SQL Server access
- Repository + service pattern, scoped DI

## Repository Layout

- `Pages/` - Razor pages and page models
- `Services/` - business logic, Excel generation, legacy mapping
- `Repositories/` - SQL access
- `Models/` - view models and data models
- `Templates/` - Excel templates
- `Assets/js/` and `Assets/css/` - page assets
- `Legacy/` - decompiled legacy VB source used as the conversion reference

## Configuration

- Connection string: `ConnectionStrings:DBConnection` in `appsettings.json`
- Templates: `Templates/BmReport/EDS-CSPTemplate.xlsx` and `Templates/BmReport/TETemplate.xlsx`
- DI registrations are sequential in `Program.cs`; BM Report uses:
  - `BmReportRepository`
  - `BmReportService`

## Build and Run

```powershell
dotnet build MachineKarteWebCore.csproj --nologo -v minimal
dotnet run --project MachineKarteWebCore.csproj
```

There is no test project. `node` is not available in this environment.

## Completed Work

### Machine Summary

Conversion is complete and builds cleanly. The page and download flow were smoke-tested successfully.

### BM Report

Status: implementation is largely converted; remaining work is live SQL validation and visual workbook QA.

Key files:

- `Legacy/Form9.vb.txt` - source of truth
- `Services/BmReportService.cs` - Excel generation
- `Repositories/BmReportRepository.cs` - `ViewTEData_Test` data
- `Models/BmReportModels.cs` - `BmReportData`
- `Pages/BmReport.cshtml` and `Pages/BmReport.cshtml.cs`
- `Assets/js/BmReport.js` and `Assets/css/BmReport.css`
- `Templates/BmReport/*.xlsx`

Implemented/fixed:

- Template selection: `EDS` -> `EDS-CSPTemplate.xlsx`; otherwise `TETemplate.xlsx`
- `TeEvidenceRepository.GetHeaderAsync` uses `SELECT *` plus optional-column reads, removing invalid `WaferNo` / `Remarks` failures
- `BmReportRepository.S` is schema-tolerant
- `BmReportData.AcredBMCauseID` added and populated
- EDS-only fields: `Z21`, `AC73`, `M73/V73`, `M76/V76`, footer `F92`
- TE-only footer `F91` is not written
- TE DropIC: `K71/W71` always; `K72/W72` only when `QualityCheckData` exists
- TE Accreditor colours: row 72, `D/J/M/S/V`, only when `BMStatusID = "14"`
- QC EDS/TE short and 14/15-element mappings are handled separately
- Setup / OPPosition / MCStatus / TestFlow markers implemented as text because legacy images are unavailable
- BmReport page now uses `_Layout`, so its Styles/Scripts sections load
- BmReport JS now auto-initializes and performs real `fetch` download handling with JSON error messages
- `_Layout`/page smoke test returns HTTP 200 and includes the BmReport CSS/JS assets

Template facts:

- Sheet order: `TE Template`, `Work Details`, `Action Taken`, `External Accessories - Quality`
- ClosedXML `Worksheet(2)` and `Worksheet(4)` are correct equivalents of legacy `Worksheets(1)` and `Worksheets(3)`
- Many cells are merged. Writing a value into a non-top-left merged cell is ignored by ClosedXML.
- EDS `X17` is inside `S17:AJ17`; TestFlow text is appended to the `S17` label in EDS, while TE writes `X17`.

## Known Limitations

- Local SQL Server is unreachable (`error: 40 - Could not open a connection to SQL Server`). Live report generation has not been verified.
- Legacy checkmark/diagram images are external and unavailable. Text markers (`✓`, `□`, or values such as `A1`) replace them.
- Some template cells are merged; visual output should be checked in Excel.
- No automated tests or JS linting are available.

## Recommended Next Steps

1. Run the BmReport page against the SQL environment and verify both TE and EDS records.
2. Open generated workbooks and inspect merged-cell areas, especially headers and QC rows.
3. Add a test fixture or harness if the project gains a test project.
