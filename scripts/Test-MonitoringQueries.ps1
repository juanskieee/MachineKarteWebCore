param([string]$Dotnet = 'dotnet')
# Validate the generated SQL against the checked-in schema without a database or test framework.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-MonitoringQueries-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [System.Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
using System.Reflection;
using System.Text.RegularExpressions;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;
using System.Globalization;
using MachineKarte.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

var schema = File.ReadAllText(Path.Combine(args[0], "schema.sql"));
var tables = Regex.Matches(schema, @"CREATE TABLE \[dbo\]\.\[(\w+)\]\((.*?)(?:\r?\n\))", RegexOptions.Singleline)
    .ToDictionary(match => match.Groups[1].Value,
        match => Regex.Matches(match.Groups[2].Value, @"^\s*\[(\w+)\]", RegexOptions.Multiline)
            .Select(column => column.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
        StringComparer.OrdinalIgnoreCase);
var flags = BindingFlags.NonPublic | BindingFlags.Static;
string Sql(string method, params object[] parameters) =>
    (string)typeof(MonitoringRepository).GetMethod(method, flags)!.Invoke(null, parameters)!;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
// Verify the complete public workflow URL contract without executing SQL-backed actions.
var registrations = new ServiceCollection();
registrations.AddLogging();
registrations.AddControllers().AddApplicationPart(typeof(AlertBoardController).Assembly);
using var provider = registrations.BuildServiceProvider();
var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
    .OfType<ControllerActionDescriptor>().Where(action => typeof(WorkflowController).IsAssignableFrom(action.ControllerTypeInfo)).ToArray();
var contracts = new Dictionary<string, string[]> {
    ["ActionCards"] = ["GET:", "GET:Load", "POST:Save"],
    ["AlertBoard"] = ["GET:", "POST:LoadBoard", "GET:Drilldown"],
    ["BmAnalytics"] = ["GET:", "POST:LoadGrid", "GET:CellMachines"],
    ["BmReport"] = ["GET:", "POST:DownloadReport", "GET:CheckRecord"],
    ["EvidenceMaking"] = ["GET:", "POST:Generate", "GET:Download"],
    ["MachineSummary"] = ["GET:", "GET:Summary", "GET:MachineList"],
    ["McdMonitoring"] = ["GET:", "GET:Dashboard", "GET:MachineList"],
    ["OcapViewer"] = ["GET:", "GET:LoadOcap"],
    ["PmReport"] = ["GET:", "GET:Generate", "GET:Download"],
    ["RawData"] = ["GET:", "GET:LoadData"],
    ["TeEvidenceMaking"] = ["GET:", "GET:Generate", "GET:Download"]
};
Require(actions.Length == contracts.Values.Sum(entries => entries.Length), "Workflow action count changed or a helper became a public action");
foreach (var (controller, routes) in contracts)
foreach (var route in routes) {
    var parts = route.Split(':');
    var request = new DefaultHttpContext();
    request.Request.Method = parts[0];
    request.Request.QueryString = QueryString.Create("handler", parts[1]);
    var context = new RouteContext(request);
    var matches = actions.Where(action => action.ControllerName == controller &&
        action.AttributeRouteInfo?.Template == controller &&
        action.ActionConstraints!.OfType<HttpMethodActionConstraint>().Any(constraint => constraint.HttpMethods.Contains(parts[0])) &&
        action.ActionConstraints!.OfType<WorkflowHandlerAttribute>().Single().IsValidForRequest(context, action)).ToArray();
    Require(matches.Length == 1, $"{parts[0]} /{controller}?handler={parts[1]} has {matches.Length} matching actions");
    Require(matches[0].ActionName == (parts[1] == "" ? "Index" : parts[1]), $"/{controller} selected the wrong action for {route}");
}
Console.WriteLine($"PASS: all {actions.Length} MVC workflow actions retain their existing URL, handler and HTTP method contracts.");
void CheckColumns(string sql, string alias, string table)
{
    Require(tables.ContainsKey(table), $"Missing schema table: {table}");
    foreach (Match match in Regex.Matches(sql, $@"\b{alias}\.(\w+)"))
        Require(tables[table].Contains(match.Groups[1].Value), $"Invalid column {table}.{match.Groups[1].Value}");
}
var processes = new[] { "DC", "DB", "WB", "MP", "PL", "FL", "ATC", "SING", "FT", "TP", "EDS", "MEMS" };
foreach (var process in processes)
{
    var table = (string)typeof(OcapViewerService).GetMethod("GetTableName", flags)!.Invoke(null, new object[] { process })!;
    Require(table == process + "BMMaintenance" && tables.ContainsKey(table) && tables[table].Contains("OCAPData"), $"Invalid OCAP table for {process}: {table}");
}
Require((string)typeof(OcapViewerService).GetMethod("GetTableName", flags)!.Invoke(null, new object[] { "SING];DROP TABLE x--" })! == "", "OCAP accepts an unknown table identifier");
Console.WriteLine("PASS: all twelve OCAP process tables, including SING/ATC/MEMS, match the schema; unknown identifiers are rejected.");
foreach (var form in new[] { "11", "12" })
{
    var designer = File.ReadAllText(Path.Combine(args[0], "Legacy", $"Form{form}.Designer.vb.txt"));
    foreach (var process in processes)
    {
        var whiteLocations = Regex.Matches(designer, $@"Me\.(lbl{process}_\d+)\.BackColor = System.Drawing.Color.White\b")
            .Select(match => Regex.Match(designer, $@"Me\.{match.Groups[1].Value}\.Location = New System.Drawing.Point\((\d+), (\d+)\)"))
            .Select(match => int.Parse(match.Groups[1].Value)).Where(x => x < 900)
            .Select(x => MachineSummaryConfig.DisplayColumns[(x - 1) / 98]).ToHashSet();
        Require(whiteLocations.SetEquals(MachineSummaryConfig.DisplayLocations[process]), $"Form{form}/{process} white cells differ from the designer");
    }
}
Console.WriteLine("PASS: all twelve process white-cell layouts match both Form11 and Form12 designers.");
var checkedQueries = 0;
foreach (var user in new[] { "PM", "CC", "EEE", "VIS", "TE" })
foreach (var process in user == "TE" ? new[] { "FL", "FT", "TP", "EDS", "MEMS" } : processes)
{
    var sql = Sql("BuildBMCardSQL", user, process);
    var actionTable = "MachineKarte_ActionCard" + (user == "PM" ? "" : "_" + user);
    CheckColumns(sql, "a", user == "TE" ? "TEBMMaintenance" : process + "BMMaintenance");
    CheckColumns(sql, "b", user == "TE" ? "BMTEUnit" : user == "PM" ? "BMCase" : "BMWhy");
    CheckColumns(sql, "c", actionTable);
    CheckColumns(sql, "ac", actionTable);
    Require(!Regex.IsMatch(sql, @"ISNULL\s*\(\s*\(SELECT 'Yes'", RegexOptions.IgnoreCase), "Existence check can return multiple rows");
    if (user != "TE") Require(!Regex.IsMatch(sql, @"\bPriority\b"), $"{user}/{process} requests TE-only Priority");
    checkedQueries++;
}
foreach (var user in new[] { "PM", "CC", "EEE", "VIS", "TE" })
{
    var sql = user == "TE" ? Sql("BuildTEMachineSummarySQL", "1=1", 10, 20, false)
        : Sql("BuildMachineSummarySQL", "DC", "1=1", 10, 20, false);
    CheckColumns(sql, "a", user == "TE" ? "TEBMMaintenance" : "DCBMMaintenance");
    CheckColumns(sql, "c", user == "TE" ? "testertype" : "MCType");
    CheckColumns(sql, "d", "Process");
    Require(sql.Contains("SELECT COUNT(*) FROM ("), "Missing total for the paged query");
}
Console.WriteLine($"PASS: {checkedQueries} BM card query variants match schema columns; all five user modes include page totals.");
var summaryQueries = new List<(string Sql, string Process, bool Te, string ActionTable)>();
foreach (var user in new[] { "PM", "CC", "EEE", "VIS", "TE" })
foreach (var process in user == "TE" ? new[] { "FL", "FT", "TP", "EDS", "MEMS" } : processes) {
    object Alert(string method, params object[] values) => typeof(AlertBoardRepository).GetMethod(method, flags)!.Invoke(null, values)!;
    var actionTable = "MachineKarte_ActionCard" + (user == "PM" ? "" : "_" + user);
    var sql = (string)(user == "TE" ? Alert("BuildQueryTE", process)
        : Alert("BuildQueryNormal", process, Alert("BuildAddQuery", user), actionTable));
    summaryQueries.Add((sql, process, user == "TE", actionTable));
}
foreach (var te in new[] { false, true })
foreach (var process in te ? new[] { "FL", "FT", "TP", "EDS", "MEMS" } : processes) {
    var sql = string.Format((string)typeof(BmAnalyticsRepository).GetField(te ? "TeSql" : "EeeSql", flags)!.GetRawConstantValue()!, process);
    summaryQueries.Add((sql, process, te, "MachineKarte_ActionCard" + (te ? "_TE" : "")));
}
foreach (var (sql, process, te, actionTable) in summaryQueries) {
    CheckColumns(sql, "a", te ? "TEBMMaintenance" : process + "BMMaintenance");
    CheckColumns(sql, "b", te ? "BMTEUnit" : "BMCase");
    CheckColumns(sql, "ac", actionTable);
    CheckColumns(sql, "c", actionTable);
    Require(!Regex.IsMatch(sql, @"ISNULL\s*\(\s*\(\s*SELECT\s+'Yes'", RegexOptions.IgnoreCase), "Summary existence lookup can throw SQL error 512 for duplicates");
    Require(sql.Contains("OUTER APPLY (SELECT TOP (1)") && sql.Contains("ORDER BY ac.Seqno DESC"), "Summary action join can multiply BM rows or select an arbitrary status");
}
Console.WriteLine($"PASS: {summaryQueries.Count} AlertBoard/Analytics SQL variants match schema columns, use duplicate-safe existence lookups and select one latest action status per BM row.");
var repeatChecks = 0;
BMCardRow Row(int age = 0, string content = "48", string priority = "QUALITY BM", string action = "No") => new()
{
    MachineNo = "TEST-MC", MachineType = "TEST", Location = "LSI2-3F",
    BMCase = "TEST CASE", BMContentId = content, Priority = priority, HaveAction = action,
    TimeOfRequest = new DateTime(2026, 11, 1, 12, 0, 0).AddDays(-age).ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
};
void CheckRepeats(string label, int expected, string user, params BMCardRow[] rows)
{
    var cards = rows.ToList();
    typeof(MonitoringRepository).GetMethod("ApplyLegacyRepeatColors", flags)!.Invoke(null, new object[] { cards, user, "DC" });
    var actual = (int)typeof(MonitoringService).GetMethod("CountNeedActionGroups", flags)!.Invoke(null, new object[] { cards })!;
    Require(actual == expected, $"{label}: expected {expected} Need Action groups, got {actual}");
    repeatChecks++;
}
CheckRepeats("Single record", 0, "PM", Row());
foreach (var user in new[] { "PM", "CC", "EEE", "VIS" })
    CheckRepeats(user + " repeated quality category", 1, user, Row(), Row(3));
CheckRepeats("Seven-day boundary", 1, "PM", Row(), Row(7));
CheckRepeats("Outside seven days", 0, "PM", Row(), Row(8));
CheckRepeats("Two adjacent repeat windows", 2, "PM", Enumerable.Range(0, 8).Select(_ => Row()).ToArray());
CheckRepeats("Action at group head", 0, "PM", Row(action: "Yes"), Row(1));
CheckRepeats("Action on older record", 1, "PM", Row(), Row(1, action: "Yes"));
CheckRepeats("Non-actionable content", 0, "PM", Row(content: "0"), Row(1, content: "0"));
CheckRepeats("Different case", 0, "PM", Row(), new BMCardRow { MachineNo="TEST-MC", MachineType="TEST", Location="LSI2-3F", BMCase="OTHER", BMContentId="48", TimeOfRequest=Row(1).TimeOfRequest });
CheckRepeats("Different machine", 0, "PM", Row(), new BMCardRow { MachineNo="OTHER", MachineType="TEST", Location="LSI2-3F", BMCase="TEST CASE", BMContentId="48", TimeOfRequest=Row(1).TimeOfRequest });
CheckRepeats("Different content", 0, "PM", Row(), Row(1, content: "49"));
CheckRepeats("TE quality repeat", 1, "TE", Row(), Row(2));
CheckRepeats("TE equipment BM", 0, "TE", Row(priority: "EQUIPMENT BM"), Row(1, priority: "EQUIPMENT BM"));
CheckRepeats("TE separate windows", 2, "TE", Enumerable.Range(0, 7).Select(_ => Row()).ToArray());
Console.WriteLine($"PASS: {repeatChecks} Need Action cases cover all user modes, seven-day limits, separate windows, action cards, case/content matching, and TE priority.");
var mcdRows = new[] { Row(), Row(1), Row(2) };
foreach (var row in mcdRows) row.IsMCD = "Yes";
typeof(MonitoringRepository).GetMethod("ApplyLegacyRepeatColors", flags)!.Invoke(null, new object[] { mcdRows.ToList(), "PM", "DC" });
Require(mcdRows[0].NeedsAction && !mcdRows[0].McdNeedsAction && !mcdRows[1].McdNeedsAction && mcdRows[2].McdNeedsAction,
    "MCD blinking did not preserve the legacy group-head, colored-member, and final-row branches");
void ColorActions(params BMCardRow[] rows) =>
    typeof(MonitoringRepository).GetMethod("ApplyLegacyActionColors", flags)!.Invoke(null, new object[] { rows.ToList() });
var statuses = new Dictionary<string, string> {
    ["RUNNING (ONGOING INVESTIGATION/MONITORING)"] = "DarkGray",
    ["RUNNING (UNDER 100% INSPECTION)"] = "DarkOrange",
    ["STOP (ONGOING INVESTIGATION)"] = "Red",
    ["RUNNING (LOT EVALUATION)"] = "LightGreen",
    ["NO RE-OCCURRENCE AFTER 7 DAYS"] = "Green",
    ["STOP (WAITING FOR INSPECTION RESULTS)"] = "Violet",
    ["STOP (WAITING FOR QC INSTRUCTION)"] = "Aqua",
    [""] = "Red"
};
foreach (var (status, color) in statuses) {
    var row = Row(action: "Yes"); row.MCStatus = status;
    ColorActions(row);
    Require(row.ActionColor == color, $"Wrong action color for {status}: {row.ActionColor}");
}
var latestAction = Row(action: "Yes");
var sixDayAction = Row(6, action: "Yes");
var sevenDayAction = Row(7, action: "Yes");
var noAction = Row(8);
ColorActions(latestAction, sixDayAction, sevenDayAction, noAction);
Require(latestAction.ActionColor == "Red" && sixDayAction.ActionColor == "Red" &&
    sevenDayAction.ActionColor == "Green" && noAction.ActionColor == "Red", "Seven-day solved override failed");
var reference = Row(); reference.BMCase = "DIFFERENT CASE";
var solvedHead = Row(7, action: "Yes"); var solvedOlder = Row(8, action: "Yes");
ColorActions(reference, solvedHead, solvedOlder);
Require(solvedHead.ActionColor == "Green" && solvedOlder.ActionColor == "Green", "Solved case head was not green");
var isolated = Row(9, action: "Yes"); isolated.MachineNo = "OTHER";
ColorActions(reference, Row(1), isolated);
Require(isolated.ActionColor == "Red", "Other machine changed the action color");
var singleCase = Row(14, action: "Yes"); singleCase.BMCase = "SINGLE CASE";
ColorActions(reference, singleCase);
Require(singleCase.ActionColor == "Red", "Singleton case was marked solved");
Console.WriteLine("PASS: all seven legacy Action colors, default red, seven-day solved overrides, existing-action guards, and machine/case isolation.");

// Exercise the real shared scheduler without opening a SQL connection.
var batchRead = typeof(AlertBoardRepository).Assembly
    .GetType("MachineKarte.Web.Repositories.MonitoringQueryBatch")!
    .GetMethod("ReadAsync")!.MakeGenericMethod(typeof(int), typeof(int));
async Task<List<int>> ReadBatch(IEnumerable<int> items, Func<int, Task<int>> read)
{
    var results = new List<int>();
    var stream = (IAsyncEnumerable<(int Item, int Result)>)batchRead.Invoke(null, new object[] { items, read })!;
    await foreach (var (item, value) in stream)
    {
        Require(item == value, "Batch paired a result with the wrong input");
        results.Add(value);
    }
    return results;
}
int activeReads = 0, peakReads = 0, readCount = 0;
var sync = new object();
async Task<int> DelayedRead(int value)
{
    lock (sync) { activeReads++; peakReads = Math.Max(peakReads, activeReads); readCount++; }
    try { await Task.Delay(5 + (3 - value % 4) * 5); return value; }
    finally { lock (sync) activeReads--; }
}
var parallelResults = await Task.WhenAll(
    ReadBatch(Enumerable.Range(0, 11), DelayedRead),
    ReadBatch(Enumerable.Range(20, 17), DelayedRead));
Require(parallelResults[0].SequenceEqual(Enumerable.Range(0, 11)) &&
    parallelResults[1].SequenceEqual(Enumerable.Range(20, 17)), "Batch changed legacy iteration order");
Require(peakReads == 4 && activeReads == 0 && readCount == 28,
    $"Shared SQL limit/exactly-once failed: peak={peakReads}, active={activeReads}, count={readCount}");
try
{
    await ReadBatch(Enumerable.Range(0, 8), async value =>
    {
        await Task.Delay(5);
        if (value == 1) throw new InvalidOperationException("Fixture read failure");
        return value;
    });
    throw new Exception("Batch swallowed a read failure");
}
catch (InvalidOperationException ex) when (ex.Message == "Fixture read failure") { }
Require((await ReadBatch(Enumerable.Range(0, 8), DelayedRead)).Count == 8,
    "A failed read leaked a semaphore slot");
Require((await ReadBatch(Array.Empty<int>(), _ => throw new Exception("Unexpected read"))).Count == 0,
    "Empty batch produced rows");
Console.WriteLine("PASS: shared four-read limit, exact-once reads, original ordering across concurrent requests, empty results, failure propagation and retry.");

var analytics = new BmAnalyticsResult();
typeof(BmAnalyticsService).GetMethod("ApplyNewLoadColor", flags)!.Invoke(null, new object[] {
    new List<BmAnalyticsRow> {
        new() { Process="ATC", Location="LSI3-1F", Mcno="MC-B", IsMCD="Yes", HaveAction="Yes" },
        new() { Process="ATC", Location="LSI3-1F", Mcno="mc-b", IsMCD="Yes", HaveAction="Yes" },
        new() { Process="ATC", Location="LSI3-1F", Mcno="MC-A", IsMCD="Yes", HaveAction="Yes" },
        new() { Process="ATC", Location="LSI3-1F", Mcno="NO-MCD", IsMCD="No", HaveAction="Yes" },
        new() { Process="ATC", Location="LSI3-1F", Mcno="NO-ACTION", IsMCD="Yes", HaveAction="No" }
    }, analytics, "12" });
Require(analytics.Cells["ATC_65"].Count == 3 &&
    analytics.Cells["ATC_65"].Mcnos.SequenceEqual(new[] { "MC-B", "MC-A" }),
    "Form12 changed row counts, case-insensitive list uniqueness, first-seen order, or MCD/action filtering");
Console.WriteLine("PASS: Form12 ATC_65 row counts, machine-list uniqueness/order and MCD/action qualification.");

var clock = System.Diagnostics.Stopwatch.StartNew();
foreach (var value in Enumerable.Range(0, 16)) await Task.Delay(30);
var sequentialMs = clock.Elapsed.TotalMilliseconds;
clock.Restart();
await ReadBatch(Enumerable.Range(0, 16), async value => { await Task.Delay(30); return value; });
var parallelMs = clock.Elapsed.TotalMilliseconds;
Console.WriteLine($"SYNTHETIC ONLY: 16 independent 30ms reads: sequential={sequentialMs:F1}ms, bounded-parallel={parallelMs:F1}ms, speedup={sequentialMs/parallelMs:F2}x. This is not a SQL benchmark.");
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --property:NuGetAudit=false --property:UseSharedCompilation=false -- $repo
if ($LASTEXITCODE -ne 0) { throw 'Monitoring query regression checks failed.' }
