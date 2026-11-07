param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB', [switch]$Benchmark)
# Focused summary/analytics SQL parity in a disposable LocalDB database.
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
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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
        ["ConnectionStrings:MachineDBConnection"] = connection.ConnectionString
    }).Build();
    var connections = new DatabaseConnectionResolver(config, NullLogger<DatabaseConnectionResolver>.Instance);
    await using var fixture = new SqlConnection(connection.ConnectionString);
    await fixture.OpenAsync();
    async Task Execute(string sql) { await using var command = new SqlCommand(sql, fixture); await command.ExecuteNonQueryAsync(); }
    await Execute(@"
CREATE TABLE BMCase (BMCaseID int PRIMARY KEY, BMCase varchar(100)); INSERT BMCase VALUES (1,'CASE');
CREATE TABLE BMTEUnit (BMTEUnitID int PRIMARY KEY, BMTEUnit varchar(100)); INSERT BMTEUnit VALUES (1,'UNIT');
CREATE TABLE BMContent (BMContentID int, Process varchar(20), BMContent varchar(100));
CREATE TABLE MachineKarte_MCD (DeviceName varchar(100), IsActive bit);
INSERT MachineKarte_MCD VALUES ('DEV ICE',1),('DEVICE',1);
CREATE TABLE MachineKarte_ActionCard (Seqno int IDENTITY, BMID int, MachineNo varchar(50), MCStatus varchar(50));
CREATE TABLE MachineKarte_ActionCard_TE (Seqno int IDENTITY, BMID int, MachineNo varchar(50), MCStatus varchar(50));
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
ALTER TABLE DCBMMaintenance ADD BMContentID int NULL, AcredBMContentID int NULL;
ALTER TABLE TEBMMaintenance ADD BMContentID int NULL, AcredBMContentID int NULL;
");
    foreach (var process in MachineSummaryConfig.EeeProcessOrder.Where(p => p != "DC"))
        await Execute($"SELECT TOP (0) * INTO {process}BMMaintenance FROM DCBMMaintenance;");
    var summaries = new MachineSummaryRepository(connections);
    var from = new DateTime(2026,11,1); var to = from;
    foreach (var te in new[] { false, true }) {
        var process = te ? "FT" : "DC";
        var floor = te ? "LSI1-2F" : "LSI1-1F";
        var all = await summaries.GetMachinesByLocationAsync(process, new DateTime(2026,11,1), new DateTime(2026,11,1), te);
        foreach (var (location, machines) in all)
            Require(machines.SequenceEqual(await summaries.GetMachinesAsync(process, location, new DateTime(2026,11,1), new DateTime(2026,11,1), te)), "Batched floor results differ");
        Require(all[floor].Count == 2 && all["LSI2-1F"].Count == 1, "BM rows or floor isolation changed");
        var distinct = await new McdMonitoringRepository(connections).GetMachinesAsync(process, floor, new DateTime(2026,11,1), new DateTime(2026,11,1), te);
        Require(distinct.Count == 1, "Shared MCD query lost DISTINCT semantics");
        var board = await new MachineSummaryService(summaries).BuildSummaryAsync(new MachineSummaryQuery {
            UserMode=te ? "TE" : "EEE", DateFrom="2026-11-01", DateTo="2026-11-01"
        });
        Require(board.GrandTotal == 3 && board.Rows.Single(r => r.Process == process).Cells[floor].Machines.Count == 1, "Grid counts or unique machine list changed");
    }
    // Duplicate and wrong-machine actions; active/inactive/null devices; category,
    // status, date, missing-case and per-process floor boundaries.
    await Execute(@"
INSERT MachineKarte_ActionCard (BMID,MachineNo) VALUES (1,'MC-A'),(1,'MC-A'),(2,'OTHER'),(3,'MC-B');
INSERT MachineKarte_ActionCard_TE (BMID,MachineNo) VALUES (1,'TE-A'),(1,'TE-A'),(2,'OTHER'),(3,'TE-B');
INSERT DCBMMaintenance (BMID,MCNo,Process,Location,TimeOfRequest,PMCategoryID,BMStatusID,BMCaseID,DeviceName)
VALUES (4,'MC-C','DC','LSI1-1F','2026-11-01',1,6,999,'DEVICE'),
 (5,'MC-D','DC','LSI1-1F','2026-11-01',1,6,1,'INACTIVE'),
 (6,'MC-E','DC','LSI1-1F','2026-11-01',1,30,1,'DEVICE'),
 (7,'MC-F','DC','LSI1-1F','2026-11-01',4,6,1,'DEVICE'),
 (8,'MC-G','DC','LSI1-1F','2026-11-02',1,6,1,'DEVICE'),
 (9,'MC-H','DC','OTHER','2026-11-01',1,6,1,'DEVICE'),
 (10,'MC-I','DC','LSI1-1F','2026-11-01',1,6,1,NULL),
 (11,'MC-J','DC','LSI1-1F','2026-11-01',1,6,0,'DEVICE');
INSERT MachineKarte_ActionCard (BMID,MachineNo) SELECT BMID,MCNo FROM DCBMMaintenance WHERE BMID>=4;
INSERT MachineKarte_MCD VALUES ('INACTIVE',0);
INSERT TEBMMaintenance (BMID,TesterNo,Process,Location,TimeOfRequest,PMCategoryID,BMStatusID,BMUnitID,DeviceName)
VALUES (20,'TE-C','FL','LSI1-2F','2026-11-01',4,6,1,'DEVICE'),
 (21,'TE-D','FL','LSI1-2F','2026-11-01',1,7,1,'DEVICE'),
 (22,'TE-E','FL','LSI1-2F','2026-11-01',1,6,999,'DEVICE'),
 (23,'TE-F','EDS','LSI1-2F','2026-11-01',1,6,1,'DEVICE'),
 (24,'TE-G','FT','LSI2-1F','2026-11-01',1,6,1,'DEVICE');
INSERT MachineKarte_ActionCard_TE (BMID,MachineNo) SELECT BMID,TesterNo FROM TEBMMaintenance WHERE BMID>=20;
INSERT ATCBMMaintenance (BMID,MCNo,Process,Location,TimeOfRequest,PMCategoryID,BMStatusID,BMCaseID,DeviceName)
VALUES (30,'ATC-A','ATC','LSI3-1F','2026-11-01',1,6,1,'DEVICE');
INSERT MachineKarte_ActionCard (BMID,MachineNo) VALUES (30,'ATC-A');
");
    async Task<Dictionary<string, Dictionary<string, List<string>>>> PerProcess(bool te) {
        var result = new Dictionary<string, Dictionary<string, List<string>>>();
        foreach (var batch in MachineSummaryConfig.GetProcesses(te).Chunk(4)) {
            var rows = await Task.WhenAll(batch.Select(p => summaries.GetMachinesByLocationAsync(p,from,to,te)));
            for (var i=0;i<batch.Length;i++) result[batch[i]]=rows[i];
        }
        return result;
    }
    string MachineState(Dictionary<string, Dictionary<string, List<string>>> rows) => JsonSerializer.Serialize(
        rows.OrderBy(p=>p.Key).Select(p=>new { p.Key, Floors=p.Value.OrderBy(l=>l.Key).Select(l=>new { l.Key,l.Value }) }));
    foreach (var te in new[] { false,true })
        Require(MachineState(await summaries.GetAllMachinesAsync(from,to,te))==MachineState(await PerProcess(te)),
            "Full-grid Machine Summary differs from individual process reads");
    var analyticsRepo = new BmAnalyticsRepository(connections);
    var analytics = new BmAnalyticsService(analyticsRepo);
    var apply = typeof(BmAnalyticsService).GetMethod("ApplyNewLoadColor",BindingFlags.NonPublic|BindingFlags.Static)!;
    async Task<BmAnalyticsResult> PerFloor(string mode,string profile) {
        var result=new BmAnalyticsResult();
        var queries=BmAnalyticsMappings.GetProcesses(mode).SelectMany(p=>BmAnalyticsMappings.GetLocations(p,mode,profile).Select(l=>(Process:p,Location:l)));
        foreach (var batch in queries.Chunk(4)) {
            var rows=await Task.WhenAll(batch.Select(q=>analyticsRepo.GetRowsAsync(q.Process,q.Location,mode,from,to)));
            foreach(var data in rows) apply.Invoke(null,new object[]{data,result,profile});
        }
        return result;
    }
    string CellState(BmAnalyticsResult grid) => JsonSerializer.Serialize(grid.Cells.OrderBy(c=>c.Key).Select(c=>new { c.Key,c.Value.Count,c.Value.Mcnos }));
    foreach(var mode in new[]{"EEE","TE"}) foreach(var profile in new[]{"8","12"}) {
        var old=await PerFloor(mode,profile);
        var grid=await analytics.BuildGridAsync(new BmAnalyticsRequest{Mode=mode,LegacyForm=profile,DateFrom="2026-11-01",DateTo="2026-11-01"});
        Require(CellState(grid)==CellState(old),$"Analytics {mode}/{profile}: changed counts, order, floor profile or duplicate handling");
        Require(grid.GrandTotal==old.Cells.Values.Sum(c=>c.Count) && grid.GrandTotal==grid.LocationTotals.Values.Sum()
            && grid.GrandTotal==grid.ProcessTotals.Values.Sum(),"Analytics totals differ");
        if(mode=="EEE") Require(grid.GrandTotal==(profile=="12"?3:2),"Analytics qualification changed");
        if(profile=="12" && mode=="EEE") Require(grid.Cells["ATC_65"].Count==1,"Form12 ATC_65 mapping lost");
    }
    using (var cancelled=new CancellationTokenSource()) {
        cancelled.Cancel();
        try { await summaries.GetAllMachinesAsync(from,to,false,cancelled.Token); throw new Exception("Summary ignored cancellation"); }
        catch(OperationCanceledException) { }
        try { await analytics.BuildGridAsync(new BmAnalyticsRequest{DateFrom="2026-11-01",DateTo="2026-11-01"},cancelled.Token); throw new Exception("Analytics ignored cancellation"); }
        catch(OperationCanceledException) { }
    }
    Console.WriteLine("PASS: Machine Summary EEE/TE full-grid parity; Analytics Form8/Form12 EEE/TE cell counts, machine order, totals, duplicate actions/MCDs, filters, missing cases, ATC_65 and cancellation.");
    if(args.Contains("benchmark")) {
        await Execute(@"
;WITH Numbers AS (SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) n FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT MachineKarte_ActionCard (BMID,MachineNo) SELECT 100000+n,'UNRELATED' FROM Numbers;
INSERT MachineKarte_ActionCard_TE (BMID,MachineNo) SELECT BMID,MachineNo FROM MachineKarte_ActionCard WHERE BMID>100000;
");
        async Task<double> Time(Func<Task> read) { var watch=Stopwatch.StartNew(); await read(); return watch.Elapsed.TotalMilliseconds; }
        var summaryBefore=new List<double>(); var summaryAfter=new List<double>();
        var analyticsBefore=new List<double>(); var analyticsAfter=new List<double>();
        for(var run=0;run<3;run++) {
            async Task Before() { summaryBefore.Add(await Time(async()=>{await PerProcess(false);})); analyticsBefore.Add(await Time(async()=>{await PerFloor("EEE","12");})); }
            async Task After() { summaryAfter.Add(await Time(async()=>{await summaries.GetAllMachinesAsync(from,to,false);})); analyticsAfter.Add(await Time(async()=>{await analytics.BuildGridAsync(new BmAnalyticsRequest{LegacyForm="12",DateFrom="2026-11-01",DateTo="2026-11-01"});})); }
            if(run%2==0) {await Before();await After();} else {await After();await Before();}
        }
        double Median(List<double> values)=>values.Order().ElementAt(1);
        Console.WriteLine($"SYNTHETIC LocalDB medians (3 alternating runs, 50k unrelated actions): Machine Summary {Median(summaryBefore):F1} -> {Median(summaryAfter):F1} ms (12 -> 1 commands); Analytics {Median(analyticsBefore):F1} -> {Median(analyticsAfter):F1} ms (52 -> 12 commands). Not production timings.");
    }
} finally {
    SqlConnection.ClearAllPools();
    Require(database.StartsWith("MachineKarte_SummaryTest_") && database.Length == "MachineKarte_SummaryTest_".Length + 32, "Invalid cleanup target");
    await Admin($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
$runnerArgs = @($LocalDbInstance)
if ($Benchmark) { $runnerArgs += 'benchmark' }
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --no-launch-profile --verbosity quiet --property:NuGetAudit=false -- @runnerArgs
if ($LASTEXITCODE -ne 0) { throw 'Machine-summary SQL regression check failed.' }
