param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB', [switch]$Benchmark, [switch]$BoardOnly, [switch]$MachineListOnly)
# Execute the real repository against a disposable LocalDB catalog, with original VB SQL as the oracle.
# Never loads appsettings or connects to an application database.
$ErrorActionPreference = 'Stop'
if ($LocalDbInstance -notmatch '^[A-Za-z0-9_-]+$') { throw 'Expected a LocalDB instance name.' }
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-MonitoringParity-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
foreach ($name in @('RepositorySupport', 'MonitoringRepository', 'MonitoringService')) {
    $folder = if ($name -eq 'MonitoringService') { 'Services' } else { 'Repositories' }
    $source = [IO.File]::ReadAllText((Join-Path $repo "$folder/$name.cs"))
    $source = $source.Replace('MachineKarte.Web.Repositories', 'MonitoringChecks.Repositories').Replace('namespace MachineKarte.Web.Services', 'namespace MonitoringChecks.Services').Replace('[eMaintenanceSystemDB].[dbo].', '[dbo].')
    if ($source -match 'eMaintenanceSystemDB') { throw "Unredirected database reference in $name" }
    [IO.File]::WriteAllText((Join-Path $checkDirectory "$name.cs"), $source)
}
@'
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using MachineKarte.Web.Services;
using Repository = MonitoringChecks.Repositories.MonitoringRepository;
using Service = MonitoringChecks.Services.MonitoringService;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
Require(args.Length >= 2 && Regex.IsMatch(args[1], @"^[A-Za-z0-9_-]+$"), "Repository and LocalDB instance required");
var database = "MachineKarte_MonitoringTest_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder {
    DataSource = @"(localdb)\" + args[1], InitialCatalog = "master", IntegratedSecurity = true,
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
    var repository = new Repository(connections);
    var service = new Service(repository);
    await using var fixture = new SqlConnection(connection.ConnectionString);
    await fixture.OpenAsync();
    async Task Execute(string sql) { await using var command = new SqlCommand(sql, fixture); await command.ExecuteNonQueryAsync(); }
    if (args.Contains("machine-list-only")) {
        await Execute(@"
CREATE TABLE Process (ProcessID int PRIMARY KEY, ProcessName varchar(10));
INSERT Process VALUES (1,'FT'),(2,'DC');
CREATE TABLE MCList (MCNo varchar(30), ProcessID int, MCLocation varchar(10));
INSERT MCList VALUES ('MC-Z',1,'LSI2-3F'),('MC-A',1,'LSI2-3F'),('MC-A',1,'LSI2-3F'),
    ('MC-OTHER',1,'LSI1-2F'),('DC-ONLY',2,'LSI2-3F');
CREATE TABLE TEBMMaintenance (TesterNo varchar(30), Process varchar(10), Location varchar(10));
INSERT TEBMMaintenance VALUES ('TE-Z','FT','LSI2-3F'),('TE-A','FT','LSI2-3F'),
    ('TE-A','FT','LSI2-3F'),('','FT','LSI2-3F'),('OTHER','FT','LSI1-2F');
CREATE TABLE ViewTEData (bmID int, MCNo varchar(30), TesterNo varchar(30), PMConfirmGLOPReqBy varchar(30), RepairedByEnd varchar(30));
INSERT ViewTEData VALUES (1,'MC-A','TE-A','REQUESTER','REPAIRER'),(2,'MC-A','TE-A','SAME','SAME');
");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cachedRepository = new Repository(connections, cache);
        var cachedService = new Service(cachedRepository);
        var clock = Stopwatch.StartNew();
        var first = await cachedService.GetMachineListAsync("PM", "FT", "LSI2-3F");
        var coldMs = clock.Elapsed.TotalMilliseconds;
        Require(first.SequenceEqual(new[] { "All", "MC-A", "MC-Z" }), "Machine list order/distinct/floor changed.");
        first.Add("LOCAL-EDIT");
        clock.Restart();
        var warm = await cachedService.GetMachineListAsync("PM", "FT", "LSI2-3F");
        var warmMs = clock.Elapsed.TotalMilliseconds;
        Require(warm.SequenceEqual(new[] { "All", "MC-A", "MC-Z" }), "Cached machine list was mutated or duplicated All.");
        Require((await cachedRepository.GetMachinesAsync("TE", "FT", "LSI2-3F")).SequenceEqual(new[] { "TE-A", "TE-Z" }), "TE list reused a PM cache entry.");
        Require((await cachedRepository.GetMachinesAsync("PM", "DC", "LSI2-3F")).SequenceEqual(new[] { "DC-ONLY" }), "Process cache isolation failed.");
        Require((await cachedRepository.GetMachinesAsync("PM", "FT", "LSI1-2F")).SequenceEqual(new[] { "MC-OTHER" }), "Floor cache isolation failed.");
        Require((await cachedRepository.GetMachinesAsync("PM", "FT", "All")).SequenceEqual(new[] { "MC-A", "MC-OTHER", "MC-Z" }), "Direct All-floor repository behavior changed.");
        await Execute("INSERT MCList VALUES ('MC-NEW',1,'LSI2-3F')");
        Require((await cachedRepository.GetMachinesAsync("CC", "FT", "LSI2-3F")).SequenceEqual(new[] { "MC-A", "MC-Z" }), "PM family did not share its short-lived cache.");
        cache.Compact(1);
        Require((await cachedRepository.GetMachinesAsync("PM", "FT", "LSI2-3F")).Contains("MC-NEW"), "Cache refill did not read current SQL data.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await cachedRepository.GetMachinesAsync("PM", "FT", "LSI2-3F", cancellation.Token); throw new Exception("Cache hit ignored cancellation."); }
        catch (OperationCanceledException) { }
        var te = new MachineKarte.Web.Repositories.TeEvidenceRepository(connections);
        foreach (var (id, expected) in new[] { ("1", "REQUESTER, REPAIRER"), ("2", "SAME") })
        {
            var header = await te.GetHeaderAsync(id);
            Require(header?.TEAssign == expected, "Reused TE header changed assignment formatting.");
        }
        Console.WriteLine($"PASS: MachineList SQL results, distinct/order/floor/process/family isolation, mutation safety, refill/cancellation and reused TE assignment. LocalDB first={coldMs:F2}ms; cached={warmMs:F2}ms (not production endpoint timing).");
        return;
    }
    await Execute(@"
;WITH Digits(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n))
SELECT TOP (50000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS value INTO FixtureNumbers
FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d CROSS JOIN Digits e;
CREATE TABLE Process (ProcessID int PRIMARY KEY, ProcessName varchar(20));
CREATE TABLE MCType (MCType varchar(50), ProcessID int, ActionLine int);
CREATE TABLE TesterType (TesterType varchar(50), ProcessID int, Location varchar(20), ActionLine int);
CREATE TABLE MCList (MCNo varchar(50), ProcessID int, MCLocation varchar(20));
CREATE TABLE BMCase (BMCaseID int PRIMARY KEY, BMCase varchar(100)); INSERT BMCase VALUES (1,'CASE'),(2,'OTHER');
CREATE TABLE BMWhy (BMWhyID int PRIMARY KEY, BMWhy varchar(100)); INSERT BMWhy VALUES (1,'WHY'),(2,'OTHER WHY');
CREATE TABLE BMTEUnit (BMTEUnitID int PRIMARY KEY, BMTEUnit varchar(100)); INSERT BMTEUnit VALUES (1,'UNIT'),(2,'OTHER UNIT');
CREATE TABLE BMContent (BMContentID int PRIMARY KEY, Process varchar(10), BMContent varchar(100));
CREATE TABLE MachineKarte_MCD (DeviceName varchar(100), IsActive bit); INSERT MachineKarte_MCD VALUES ('DEVICE A',1),('INACTIVE',0);
CREATE TABLE gridBMOCAP (BMCaseID int, Process varchar(10));
CREATE TABLE Seed (BMID int PRIMARY KEY, MCNo varchar(50), TesterNo varchar(50), MCType varchar(50),
    Process varchar(20), Location varchar(20), TimeOfRequest datetime, PMCategoryID int, BMStatusID int,
    BMCaseID int, BMUnitID int, Why4 int, BMContentID int, AcredBMContentID int, DeviceName varchar(100),
    Priority varchar(30), Turnover varchar(10), OCAPData varchar(100));
INSERT Seed VALUES
(1,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-11-01T12:34:56',1,6,1,1,1,48,0,'DEV ICEA','QUALITY BM','CC',''),
(2,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-31T12:34:56',4,6,1,1,1,48,49,'DEVICEA','QUALITY BM','PE',''),
(3,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-30T12:34:56',1,6,1,1,1,48,NULL,'INACTIVE','QUALITY BM','PE',''),
(4,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-29T12:34:56',1,6,1,1,1,48,48,NULL,'OTHER','VIS',''),
(5,'MC-A','TE-A','TYPE','X','OTHER','2026-10-28T12:34:56',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(6,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-09-01',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(7,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-28',1,30,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(8,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-28',1,31,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(9,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-28',2,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(10,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-28',1,7,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(11,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-28',1,6,0,0,0,48,0,'DEVICEA','QUALITY BM','CC',''),
(12,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-10-01',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(13,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-11-02',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC',''),
(14,'MC-A','TE-A','TYPE','X','LSI2-3F','2026-11-02T00:00:01',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC','');
INSERT Seed SELECT 20+value,'MC-'+RIGHT('0'+CAST(value AS varchar(2)),2),'TE-'+RIGHT('0'+CAST(value AS varchar(2)),2),
 'TYPE','X','LSI2-3F','2026-10-15',1,6,2,2,2,48,0,'DEVICEA','QUALITY BM','CC',''
FROM FixtureNumbers WHERE value<=24;
");
    var processes = new[] { "DC", "DB", "WB", "MP", "PL", "FL", "ATC", "SING", "FT", "TP", "EDS", "MEMS" };
    var testers = new[] { "FL", "FT", "TP", "EDS", "MEMS" };
    foreach (var user in new[] {"PM","CC","EEE","VIS","TE"}) {
        var table = "MachineKarte_ActionCard" + (user == "PM" ? "" : "_" + user);
        await Execute($@"CREATE TABLE {table} (Seqno int PRIMARY KEY, BMID int, MachineNo varchar(50),
            Action varchar(100), Comment varchar(100), Comment2 varchar(100), Comment3 varchar(100), MCStatus varchar(100));
            INSERT {table} VALUES (1,1,'{(user=="TE"?"WRONG-TESTER":"MC-A")}','ACTION','SECTION','DEPARTMENT','DIVISION','RUNNING (LOT EVALUATION)');");
    }
    await Execute("SELECT TOP (0) * INTO TEBMMaintenance FROM Seed;");
    for (var i=0;i<processes.Length;i++) {
        var process = processes[i];
        await Execute($@"INSERT Process VALUES ({i+1},'{process}');
INSERT MCType VALUES ('TYPE',{i+1},NULL); INSERT MCList VALUES ('MC-A',{i+1},'LSI2-3F');
INSERT TesterType VALUES ('TYPE',{i+1},'LSI2-3F',NULL),('TYPE',{i+1},'OTHER',4),('TYPE',{i+1},'UNUSED',4);
INSERT BMContent VALUES ({48+i*2},'{process}','CONTENT'),({49+i*2},'{process}','ACCREDITED');
INSERT gridBMOCAP VALUES (1,'{process}'),(1,'{process}'),(2,'{process}');
SELECT * INTO {process}BMMaintenance FROM Seed;
UPDATE {process}BMMaintenance SET Process='{process}',BMContentID={48+i*2},AcredBMContentID=CASE WHEN AcredBMContentID=49 THEN {49+i*2} WHEN AcredBMContentID=48 THEN {48+i*2} ELSE AcredBMContentID END;");
        if (testers.Contains(process)) await Execute($"INSERT TEBMMaintenance SELECT * FROM {process}BMMaintenance;");
    }
    if(args.Contains("board-only")) {
        // Compare the changed board directly with the existing web page/card
        // queries; leave the separate desktop SQL audit out of this focused run.
        await Execute(@"
INSERT MachineKarte_MCD VALUES ('DEV ICEA',1);
INSERT MachineKarte_ActionCard_TE VALUES (2,1,'OTHER','NEW ACTION','','','','STOP (ONGOING INVESTIGATION)');
INSERT TesterType VALUES ('SECOND',9,'LSI2-3F',6),('TYPE',9,'LSI2-2F',4);
INSERT TEBMMaintenance VALUES
(200,'MC-A','TE-A','SECOND','FT','LSI2-3F','2026-10-20',4,6,1,1,1,64,0,'DEVICEA','QUALITY BM','CC',''),
(201,'MC-A','TE-A','TYPE','FT','LSI2-2F','2026-10-31 12:00:00',1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC','');
");
        int columns=0;
        foreach(var user in new[]{"PM","CC","EEE","VIS","TE"}) {
            var request=new MonitoringFilter{UserType=user,Process=user=="TE"?"FT":"DC",Floor="LSI2-3F",Machine="All",DateFrom="2026-10-01",DateTo="2026-11-02",Page=999};
            var grouped=await service.SearchDashboardAsync(request);
            var paged=new List<MachineSummary>();
            for(var page=1;page<=grouped.TotalPages;page++) {
                request.Page=page;
                paged.AddRange((await repository.GetPagedMachineSummariesAsync(request)).Machines);
            }
            Require(grouped.CurrentPage==grouped.TotalPages && grouped.MachineSummaries.Count<=10,"Grouped search lost page clamping");
            var displayedCounts=grouped.MachineCards.ToDictionary(c=>(c.MachineNo,c.MachineType,c.ActionLine),c=>c.BMCards.Count);
            var staircase=paged.OrderByDescending(c=>displayedCounts[(c.MachineNo,c.MachineType,c.ActionLine)]);
            Require(grouped.MachineCards.Select(c=>(c.MachineNo,c.MachineType,c.ActionLine))
                .SequenceEqual(staircase.Select(c=>(c.MachineNo,c.MachineType,c.ActionLine))),user+": columns are not descending by displayed cards with stable legacy ties");
            foreach(var column in grouped.MachineCards) {
                var single=await service.GetMachineCardAsync(request,column.MachineNo,column.MachineType,column.ActionLine);
                Require(JsonSerializer.Serialize(column)==JsonSerializer.Serialize(single),user+": grouped column changed rows, colors, counts or metadata for "+column.MachineNo);
                Require(column.BMCards.All(c=>c.Location==request.Floor && c.MachineType==column.MachineType),"Grouped rows crossed floors/types");
                columns++;
            }
        }
        using var boardCancellation=new CancellationTokenSource(); boardCancellation.Cancel();
        try { await service.SearchDashboardAsync(new MonitoringFilter{UserType="TE",Process="FT",Floor="LSI2-3F"},boardCancellation.Token); throw new Exception("Grouped search ignored cancellation"); }
        catch(OperationCanceledException) { }
        Console.WriteLine($"PASS: {columns} grouped columns match per-column reads in all five modes, ordered by displayed count with stable legacy ties, including duplicate MCD/actions, tester types, interleaved floors, counts/colors, page clamping and cancellation. Two SQL reads per search; no repeated per-column/page reads needed.");
        return;
    }
    // Use the checked-in uc_Test statements themselves, changing only VB string concatenation and catalog.
    var vb = File.ReadAllText(Path.Combine(args[0],"Legacy","uc_Test.vb.txt"));
    var legacyCards = Regex.Matches(vb, "strQuery = \"(.*?)\"\\r?\\n", RegexOptions.Singleline)
        .Select(match=>match.Groups[1].Value).Where(sql=>sql.Contains("a.OCAPData") && sql.Contains("where (a.timeofrequest between")).ToArray();
    Require(legacyCards.Length==3,"Could not identify the original PM/other/TE card queries");
    string UserFilter(string user)=>user switch {"CC"=>"(Turnover='CC' AND BMStatusID IN (6,7,8,9,11,20,19,14))",
        "EEE"=>"(Turnover='PE' AND BMStatusID IN (6,7,8,9,11,20,19,14))",
        "VIS"=>"(Turnover='VIS' AND BMStatusID IN (6,7,8,9,11,20,19,14))", _=>"a.BMStatusID NOT IN (30,31)"};
    string Legacy(string user,string process) {
        var action = "MachineKarte_ActionCard"+(user=="PM"?"":"_"+user);
        var sql = legacyCards[user=="TE"?2:user=="PM"?0:1]
            .Replace("\" & MCProcess & \"",process).Replace("\" & ActionQuery & \"",action).Replace("\" & AddQuery & \"",UserFilter(user))
            .Replace("[eMaintenanceSystemDB].[dbo].","[dbo].");
        Require(!sql.Contains("eMaintenanceSystemDB",StringComparison.OrdinalIgnoreCase) && !sql.Contains(" & "),"Legacy SQL was not safely redirected");
        return sql;
    }
    async Task<DataTable> Read(string sql,string machine,string type="TYPE",string floor="LSI2-3F",string process="FT") {
        await using var command = new SqlCommand(sql,fixture);
        command.Parameters.AddWithValue("@Range1",new DateTime(2026,10,1));
        // Compare identical endpoints; neither path adds a day to To.
        command.Parameters.AddWithValue("@Range2",new DateTime(2026,11,2));
        command.Parameters.AddWithValue("@Location",floor); command.Parameters.AddWithValue("@Mcno",machine);
        command.Parameters.AddWithValue("@MCType",type); command.Parameters.AddWithValue("@Process",process);
        await using var reader = await command.ExecuteReaderAsync(); var result=new DataTable(); result.Load(reader); return result;
    }
    string Value(DataRow row,string column)=>row[column].ToString()!;
    int checks=0;
    foreach (var user in new[]{"TE","PM","CC","EEE","VIS"})
    foreach (var process in user=="TE"?testers:processes) {
        var machine=user=="TE"?"TE-A":"MC-A";
        var original=await Read(Legacy(user,process),machine,process:process);
        var cards=await repository.GetBMCardsAsync(user,process,"LSI2-3F",machine,"10/01/2026","11/02/2026","TYPE");
        Require(original.Rows.Count>0 && cards.Count==original.Rows.Count,$"{user}/{process}: legacy has {original.Rows.Count} cards, web has {cards.Count}");
        for (var i=0;i<cards.Count;i++) {
            var row=original.Rows[i]; var card=cards[i];
            Require(card.BMId==(int)row["bmid"] && card.TimeOfRequest==Value(row,"Timeofrequest") &&
                card.DeviceName==Value(row,"Devicename") && card.IsMCD==Value(row,"isMCD") &&
                card.MachineNo==Value(row,user=="TE"?"testerNo":"Mcno") && card.BMCase==Value(row,"BMCase") &&
                card.BMCaseId==Value(row,"BMCASEID") && card.HaveAction==Value(row,"HaveAction") &&
                card.BMContentId==Value(row,"BMContentID") && card.OcapCount==(int)row["OCAPCount"] &&
                card.MCStatus==Value(row,"MCStatus") && card.ActionMarker==Value(row,"Accre") &&
                card.SectionMarker==Value(row,"Sect") && card.DepartmentMarker==Value(row,"Dept") && card.DivisionMarker==Value(row,"Div"),
                $"{user}/{process}/BM{card.BMId}: original card fields/order differ");
            Require(user=="TE"?card.Priority==Value(row,"Priority"):card.BMContent==Value(row,"BMContent"),"Mode-specific content differs");
        }
        checks++;
    }
    Console.WriteLine($"PASS: {checks} user/process variants match original uc_Test SQL, including TE/FT/LSI2-3F and date boundaries, filters, fields, order and BMID-only TE actions.");

    var form=File.ReadAllText(Path.Combine(args[0],"Legacy","Form1.vb.txt"));
    var legacySummaries=Regex.Matches(form,"strQuery2 = \"(.*?)\"\\r?\\n",RegexOptions.Singleline).Select(match=>match.Groups[1].Value).ToArray();
    Require(legacySummaries.Length==4,"Could not identify original All/specific PM/TE summary queries");
    int summaryChecks=0;
    foreach(var user in new[]{"PM","CC","EEE","VIS","TE"})
    foreach(var process in user=="TE"?testers:processes) {
        foreach(var specific in new[]{false,true}) {
            var values=new Dictionary<string,string>{{"MCProcess",process},{"AddQuery",UserFilter(user)},{"topNum2","1000000"},{"topNum1","0"}};
            var sql=Regex.Replace(legacySummaries[(user=="TE"?2:0)+(specific?1:0)],"\" & (\\w+) & \"",match=>values[match.Groups[1].Value])
                .Replace("[eMaintenanceSystemDB].[dbo].","[dbo].");
            Require(!sql.Contains("eMaintenanceSystemDB",StringComparison.OrdinalIgnoreCase) && !sql.Contains(" & "),"Original summary was not redirected");
            var machine=specific?(user=="TE"?"TE-A":"MC-A"):"All";
            var original=await Read(sql,machine,process:process);
            var request=new MonitoringFilter{UserType=user,Process=process,Floor="LSI2-3F",Machine=machine,DateFrom="10/01/2026",DateTo="11/02/2026"};
            var actual=new List<MachineSummary>(); int total=1;
            do { var result=await repository.GetPagedMachineSummariesAsync(request); actual.AddRange(result.Machines); total=result.TotalPages; request.Page++; } while(request.Page<=total);
            var expected=original.Rows.Cast<DataRow>().Select(row=>$"{Value(row,user=="TE"?"testerno":"MCNO")}|{Value(row,"MCType")}|{Value(row,"MACHINENAME")}|{Value(row,"actionline")}").Order().ToArray();
            Require(actual.Select(row=>$"{row.MachineNo}|{row.MachineType}|{row.BMCount}|{row.ActionLine}").Order().SequenceEqual(expected),$"{user}/{process}/{machine}: original Form1 summary differs");
            summaryChecks++;
        }
    }
    Console.WriteLine($"PASS: {summaryChecks} All/specific machine summary variants match original Form1 SQL across every page.");

    var filter=new MonitoringFilter {UserType="TE",Process="FT",Floor="LSI2-3F",Machine="All",DateFrom="2026-10-01",DateTo="2026-11-02"};
    var testerList=await repository.GetMachinesAsync("TE","FT",filter.Floor);
    Require(testerList.Count==25 && testerList.SequenceEqual(testerList.Order()) && testerList.Contains("TE-A"),"TE tester dropdown lost process/floor filters or distinct ordering");
    Require((await repository.GetMachinesAsync("PM","DC",filter.Floor)).SequenceEqual(new[]{"MC-A"}),"PM machine dropdown changed");
    var all=await repository.GetBMCardsAsync("TE","FT",filter.Floor,"All",filter.DateFrom,filter.DateTo);
    Require(all.Count==30 && all.All(row=>row.Process=="FT"),"TE/FT/LSI2-3F/All filters lost or added BM cards");
    var pages=new List<MachineSummary>();
    for(var page=1;page<=3;page++) {
        filter.Page=page; var result=await repository.GetPagedMachineSummariesAsync(filter);
        Require(result.TotalPages==3 && result.Machines.Count==(page<3?10:5),"Pagination count/total differs");
        pages.AddRange(result.Machines);
    }
    Require(pages.Select(row=>row.MachineNo).Distinct().Count()==25 && pages.Sum(row=>row.BMCount)==all.Count && pages[0].MachineNo=="TE-A" && pages.All(row=>row.ActionLine==4),"Pagination ties, BM counts or default action line differ");
    filter.Page=4; Require((await repository.GetPagedMachineSummariesAsync(filter)).Machines.Count==0,"Out-of-range page lost total contract");
    filter.Page=1; filter.Floor="All";
    var allFloorCounts=new List<MachineSummary>();
    for(var page=1;page<=3;page++) { filter.Page=page; allFloorCounts.AddRange((await repository.GetPagedMachineSummariesAsync(filter)).Machines); }
    Require(allFloorCounts.Sum(row=>row.BMCount)==31,"All floors multiplied TesterType matches");
    filter.Page=1;
    filter.Floor="LSI2-3F"; filter.Machine="TE-A";
    Require((await repository.GetPagedMachineSummariesAsync(filter)).Machines.Single().BMCount==6,"Specific tester filter differs");
    filter.Machine="MISSING";
    var empty=await repository.GetPagedMachineSummariesAsync(filter);
    Require(empty.Machines.Count==0 && empty.TotalPages==1,"Empty summary contract differs");
    Console.WriteLine("PASS: machine/tester dropdowns, All/specific/missing testers, three pages with ties, empty/out-of-range pages, All floors, and default action lines.");

    await Execute(@"
INSERT MachineKarte_MCD VALUES ('DEV ICEA',1);
INSERT MachineKarte_ActionCard_TE VALUES (2,1,'ANOTHER-TESTER','LATEST','NEW SECTION','NEW DEPARTMENT','NEW DIVISION','STOP (WAITING FOR QC INSTRUCTION)');
INSERT MachineKarte_ActionCard VALUES (2,1,'MC-A','LATEST','NEW SECTION','NEW DEPARTMENT','NEW DIVISION','STOP (WAITING FOR QC INSTRUCTION)');
");
    var duplicates=await repository.GetBMCardsAsync("TE","FT","LSI2-3F","TE-A",filter.DateFrom,filter.DateTo,"TYPE");
    Require(duplicates.Count==6 && duplicates.Single(row=>row.BMId==1).ActionMarker=="LATEST" && duplicates.Single(row=>row.BMId==1).ActionColor=="Aqua","MCD/action duplicates multiplied BMs or selected old action");
    var pmDuplicates=await repository.GetBMCardsAsync("PM","DC","LSI2-3F","MC-A",filter.DateFrom,filter.DateTo,"TYPE");
    Require(pmDuplicates.Select(row=>row.BMId).Distinct().Count()==pmDuplicates.Count && pmDuplicates.Single(row=>row.BMId==1).ActionMarker=="LATEST","PM duplicate handling differs");
    filter.Machine="All"; filter.Page=99;
    var dashboard=await service.SearchDashboardAsync(filter);
    Require(dashboard.CurrentPage==3 && dashboard.TotalPages==3 && dashboard.FloorSummaries.Single(row=>row.FloorCode=="LSI2-3F").BMCount==30,"Real dashboard lost cards, counters or page clamping");
    var machineCard=await service.GetMachineCardAsync(filter,"TE-A","TYPE",4);
    Require(machineCard.BMCards.Count==6 && machineCard.BMCards.Any(row=>row.NeedsAction),"Real machine-card flow lost cards or repeat detection");
    using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
    try { await repository.GetBMCardsAsync("TE","FT","LSI2-3F","All",filter.DateFrom,filter.DateTo,cancellationToken:cancelled.Token); throw new Exception("Cancelled read completed"); }
    catch(OperationCanceledException) { }
    Console.WriteLine("PASS: duplicate MCD/action safety, latest action, repeat colors, real dashboard/card service flows and cancellation.");

    // EXCEPT previously removed identical projected groups (for example NULL vs explicit default action lines).
    await Execute("INSERT TesterType VALUES ('TYPE',9,'LSI2-3F',4); INSERT MCType VALUES ('TYPE',1,4);");
    filter.Page=1; filter.Machine="TE-A";
    var metadataDuplicates=await repository.GetPagedMachineSummariesAsync(filter);
    Require(metadataDuplicates.Machines.Count==1 && metadataDuplicates.Machines[0].BMCount==6,"Duplicate projected tester groups changed legacy EXCEPT semantics");
    var pmSummary=await repository.GetPagedMachineSummariesAsync(new MonitoringFilter{UserType="PM",Process="DC",Floor="LSI2-3F",Machine="MC-A",DateFrom=filter.DateFrom,DateTo=filter.DateTo});
    Require(pmSummary.Machines.Count==1 && pmSummary.Machines[0].BMCount==6,"Duplicate projected machine groups changed legacy EXCEPT semantics");
    await Execute(@"
INSERT TEBMMaintenance SELECT 100+status,'MC-A','TE-A','TYPE','FT','LSI2-3F','2026-10-20',1,status,1,1,1,64,0,'DEVICEA','QUALITY BM','CC',''
FROM (VALUES(1),(2),(3),(7),(17),(30),(31)) excluded(status);
INSERT TEBMMaintenance VALUES (200,'MC-A','TE-A','SECOND','FT','LSI2-3F','2026-10-20',4,6,1,1,1,64,0,'DEVICEA','QUALITY BM','CC','');
INSERT TesterType VALUES ('SECOND',9,'LSI2-3F',6);
");
    Require((await repository.GetBMCardsAsync("TE","FT","LSI2-3F","TE-A",filter.DateFrom,filter.DateTo,"TYPE")).Count==6,"Excluded TE statuses or another tester type leaked into TYPE cards");
    Require((await repository.GetBMCardsAsync("TE","FT","LSI2-3F","TE-A",filter.DateFrom,filter.DateTo,"SECOND")).Single().BMId==200,"Tester type/category 4 filtering lost valid cards");
    Console.WriteLine("PASS: legacy EXCEPT distinctness for NULL/default metadata, all seven TE status exclusions, and separate tester types/category 4.");


    if(args.Contains("benchmark")) {
        // Synthetic workload only; both queries use the same database, data, indexes and connection.
        await Execute(@"
DELETE MachineKarte_MCD WHERE DeviceName='DEV ICEA';
DELETE MachineKarte_ActionCard_TE WHERE Seqno=2;
DELETE TEBMMaintenance WHERE BMID BETWEEN 100 AND 200;
INSERT TEBMMaintenance SELECT 1000+value,'MC-A','TE-A','TYPE','FT','LSI2-3F',DATEADD(second,-value,'2026-11-01'),1,6,1,1,1,48,0,'DEVICEA','QUALITY BM','CC','' FROM FixtureNumbers WHERE value<=10000;
INSERT MachineKarte_ActionCard_TE SELECT 1000+value,100000+value,'UNRELATED','','','','','' FROM FixtureNumbers;
");
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var improved=(string)typeof(Repository).GetMethod("BuildBMCardSQL",flags)!.Invoke(null,new object[]{"TE","FT"})!;
        var original=Legacy("TE","FT");
        await Read(original,"TE-A"); await Read(improved,"TE-A");
        var oldTimes=new List<double>(); var newTimes=new List<double>();
        for(var iteration=0;iteration<7;iteration++) {
            foreach(var old in iteration%2==0?new[]{true,false}:new[]{false,true}) {
                var watch=Stopwatch.StartNew(); var rows=await Read(old?original:improved,"TE-A"); watch.Stop();
                Require(rows.Rows.Count==10006,"Benchmark row count changed");
                (old?oldTimes:newTimes).Add(watch.Elapsed.TotalMilliseconds);
            }
        }
        var before=oldTimes.Order().ElementAt(3); var after=newTimes.Order().ElementAt(3);
        Console.WriteLine($"SYNTHETIC LocalDB warm median (7 alternating runs, 10,006 cards / 50,001 action rows): original {before:F1} ms; CTE {after:F1} ms; {before/after:F2}x. Includes SQL read/materialization, excludes C# card coloring. Not a production speedup claim.");
    }
} finally {
    SqlConnection.ClearAllPools();
    Require(database.StartsWith("MachineKarte_MonitoringTest_") && database.Length=="MachineKarte_MonitoringTest_".Length+32,"Invalid cleanup target");
    await Admin($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
# Keep the RID on the runner so project references build/copy the same portable app DLL.
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --nologo -v minimal --self-contained false --property:UseAppHost=false -m:1 --nodeReuse:false --property:NuGetAudit=false --property:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Monitoring parity fixture build failed.' }
$runArguments = @((Join-Path $checkDirectory 'bin/Debug/net8.0/win-x64/Checks.dll'), $repo, $LocalDbInstance)
if ($Benchmark) { $runArguments += 'benchmark' }
if ($BoardOnly) { $runArguments += 'board-only' }
if ($MachineListOnly) { $runArguments += 'machine-list-only' }
& $Dotnet @runArguments
if ($LASTEXITCODE -ne 0) { throw 'Monitoring parity regression checks failed.' }
