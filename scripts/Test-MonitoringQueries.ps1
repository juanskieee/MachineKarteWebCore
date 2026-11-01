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
void CheckColumns(string sql, string alias, string table)
{
    Require(tables.ContainsKey(table), $"Missing schema table: {table}");
    foreach (Match match in Regex.Matches(sql, $@"\b{alias}\.(\w+)"))
        Require(tables[table].Contains(match.Groups[1].Value), $"Invalid column {table}.{match.Groups[1].Value}");
}
var processes = new[] { "DC", "DB", "WB", "MP", "PL", "FL", "ATC", "SING", "FT", "TP", "EDS", "MEMS" };
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
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --property:NuGetAudit=false --property:UseSharedCompilation=false -- $repo
if ($LASTEXITCODE -ne 0) { throw 'Monitoring query regression checks failed.' }
