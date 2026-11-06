param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB')
# Focused SQL regression: paging syntax and one read per process for all summary floors.
$ErrorActionPreference = 'Stop'
if ($LocalDbInstance -notmatch '^[A-Za-z0-9_-]+$') { throw 'Expected a LocalDB instance name.' }
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-Summary-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var database = "MachineKarte_SummaryTest_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder {
    DataSource = @"(localdb)\" + args[0], InitialCatalog = "master", IntegratedSecurity = true,
    Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = true, ConnectTimeout = 10
};
await using var admin = new SqlConnection(connection.ConnectionString);
await admin.OpenAsync();
async Task Admin(string sql) { await using var command = new SqlCommand(sql, admin); await command.ExecuteNonQueryAsync(); }
await Admin($"CREATE DATABASE [{database}]");
try {
    connection.InitialCatalog = database;
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:DBConnection"] = connection.ConnectionString
    }).Build();
    await using var fixture = new SqlConnection(connection.ConnectionString);
    await fixture.OpenAsync();
    async Task Execute(string sql) { await using var command = new SqlCommand(sql, fixture); await command.ExecuteNonQueryAsync(); }
    await Execute(@"
CREATE TABLE Process (ProcessID int, ProcessName varchar(20)); INSERT Process VALUES (1,'DC'),(2,'FT');
CREATE TABLE MCType (MCType varchar(50), ProcessID int, ActionLine int); INSERT MCType VALUES ('TYPE',1,4);
CREATE TABLE TesterType (TesterType varchar(50), ProcessID int, Location varchar(20), ActionLine int);
INSERT TesterType VALUES ('TYPE',2,'LSI1-2F',4),('TYPE',2,'LSI2-1F',4);
CREATE TABLE MachineKarte_MCD (DeviceName varchar(100), IsActive bit);
INSERT MachineKarte_MCD VALUES ('DEV ICE',1),('DEVICE',1);
CREATE TABLE MachineKarte_ActionCard (BMID int, MachineNo varchar(50));
CREATE TABLE MachineKarte_ActionCard_TE (BMID int, MachineNo varchar(50));
CREATE TABLE DCBMMaintenance (BMID int, MCNo varchar(50), TesterNo varchar(50), MCType varchar(50),
    Process varchar(20), Location varchar(20), TimeOfRequest datetime, PMCategoryID int,
    BMStatusID int, BMCaseID int, BMUnitID int, DeviceName varchar(100), Turnover varchar(10));
INSERT DCBMMaintenance VALUES
(1,'MC-A','TE-A','TYPE','DC','LSI1-1F','2026-11-01',1,6,1,1,'DEVICE','CC'),
(2,'MC-A','TE-A','TYPE','DC','LSI1-1F','2026-11-01',1,6,1,1,'DEVICE','CC'),
(3,'MC-B','TE-B','TYPE','DC','LSI2-1F','2026-11-01',1,6,1,1,'DEVICE','CC');
SELECT * INTO TEBMMaintenance FROM DCBMMaintenance;
UPDATE TEBMMaintenance SET Process='FT', Location='LSI1-2F' WHERE Location='LSI1-1F';
UPDATE TEBMMaintenance SET Process='FT', PMCategoryID=4 WHERE BMID=3;
");
    foreach (var process in MachineSummaryConfig.EeeProcessOrder.Where(p => p != "DC"))
        await Execute($"SELECT TOP (0) * INTO {process}BMMaintenance FROM DCBMMaintenance;");
    var monitoring = new MonitoringRepository(config);
    var summaries = new MachineSummaryRepository(config);
    foreach (var te in new[] { false, true }) {
        var process = te ? "FT" : "DC";
        var floor = te ? "LSI1-2F" : "LSI1-1F";
        var page = await monitoring.GetPagedMachineSummariesAsync(new MonitoringFilter {
            UserType=te ? "TE" : "PM", Process=process, Floor="All", Machine="All",
            DateFrom="11/01/2026 00:00:00", DateTo="11/01/2026 23:59:59"
        });
        Require(page.Machines.Count == 2 && page.Machines[0].BMCount == 2 && page.TotalPages == 1, "Paging syntax/count/order regression");
        var all = await summaries.GetMachinesByLocationAsync(process, new DateTime(2026,11,1), new DateTime(2026,11,1), te);
        foreach (var (location, machines) in all)
            Require(machines.SequenceEqual(await summaries.GetMachinesAsync(process, location, new DateTime(2026,11,1), new DateTime(2026,11,1), te)), "Batched floor results differ");
        Require(all[floor].Count == 2 && all["LSI2-1F"].Count == 1, "BM rows or floor isolation changed");
        var distinct = await new McdMonitoringRepository(config).GetMachinesAsync(process, floor, new DateTime(2026,11,1), new DateTime(2026,11,1), te);
        Require(distinct.Count == 1, "Shared MCD query lost DISTINCT semantics");
        var board = await new MachineSummaryService(summaries).BuildSummaryAsync(new MachineSummaryQuery {
            UserMode=te ? "TE" : "EEE", DateFrom="2026-11-01", DateTo="2026-11-01"
        });
        Require(board.GrandTotal == 3 && board.Rows.Single(r => r.Process == process).Cells[floor].Machines.Count == 1, "Grid counts or unique machine list changed");
    }
    Console.WriteLine("PASS: normal/TE SQL paging, batched floor parity, BM counts, MCD deduplication and grid totals.");
} finally {
    SqlConnection.ClearAllPools();
    Require(database.StartsWith("MachineKarte_SummaryTest_") && database.Length == "MachineKarte_SummaryTest_".Length + 32, "Invalid cleanup target");
    await Admin($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --no-launch-profile --verbosity quiet --property:NuGetAudit=false -- $LocalDbInstance
if ($LASTEXITCODE -ne 0) { throw 'Machine-summary SQL regression check failed.' }
