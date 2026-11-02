param([string]$Dotnet = 'dotnet')
# Compile the actual Form2 business routines as a database-free parity oracle.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-ActionSummary-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $checkDirectory 'LegacyOracle') -Force | Out-Null
$projectPath = [System.Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /><ProjectReference Include="LegacyOracle/LegacyOracle.vbproj" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><RootNamespace></RootNamespace><NuGetAudit>false</NuGetAudit></PropertyGroup>
</Project>
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'LegacyOracle/LegacyOracle.vbproj')
$legacy = [IO.File]::ReadAllText((Join-Path $repo 'Legacy/Form2.vb.txt'))
$methods = @('CheckIfSolve','GetDuplicateRowsByColumn','AreOtherRowsSame','GetFontColor','CheckifSevendays','LoadColor','LoadColor_TE','AreNextOneColumnValuesEqual','AreNextTwoColumnValuesEqual','AreNextThreeColumnValuesEqual','AreNextFourColumnValuesEqual','AreNextFiveColumnValuesEqual')
$oracle = @'
Imports System.Data
Imports System.Globalization
Imports System.Collections.Generic
Public Class LegacyOracle
    Private dtNewRowSame As DataTable
    Private dtNotEquals As DataTable
    Public Hits As New List(Of String)
    Public Sub Evaluate(table As DataTable, isTe As Boolean)
        If table.Rows.Count = 0 Then Return
        CheckIfSolve(table)
        dtNewRowSame.Merge(dtNotEquals)
        dtNewRowSame.DefaultView.Sort = "Mcno DESC,TimeofRequest DESC,BMCase ASC"
        Dim sorted = dtNewRowSame.DefaultView.ToTable()
        If isTe Then
            LoadColor_TE(sorted)
        Else
            LoadColor(sorted)
        End If
    End Sub
    Private Sub InputToLabel(process As String, location As String, mcno As String)
        Hits.Add(process & "|" & location & "|" & mcno)
    End Sub

'@
foreach ($method in $methods) {
    $match = [regex]::Match($legacy, "(?ms)^    (?:Private )?(?:Sub|Function) $method\(.*?^    End (?:Sub|Function)")
    if (!$match.Success) { throw "Missing legacy routine: $method" }
    $oracle += $match.Value + "`n"
}
$oracle += "End Class`n"
[IO.File]::WriteAllText((Join-Path $checkDirectory 'LegacyOracle/Oracle.vb'), $oracle)
@'
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var flags = BindingFlags.Static | BindingFlags.NonPublic;
object Call(string method, params object[] values) => typeof(AlertBoardRepository).GetMethod(method, flags)!.Invoke(null, values)!;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
AlertBoardRow Row(int age = 0, string mc = "MC-B", string bmCase = "CASE A", int content = 48, string action = "No", string priority = "QUALITY BM", int hours = 0) => new() {
    Process = "DC", Location = "LSI2-3F", Mcno = mc, BMCase = bmCase, BMContentId = content,
    HaveAction = action, Priority = priority,
    Timeofrequest = new DateTime(2026, 11, 1, 12, 0, 0).AddDays(-age).AddHours(-hours).ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
};
int checks = 0;
void Check(string label, bool te, params AlertBoardRow[] input) {
    var table = new DataTable();
    foreach (var column in new[] { "Process", "Location", "Mcno", "Timeofrequest", "BMCase", "HaveAction", "Priority", "FontColor", "Color" }) table.Columns.Add(column);
    table.Columns.Add("BMContentID", typeof(int));
    foreach (var row in input) table.Rows.Add(row.Process, row.Location, row.Mcno, row.Timeofrequest, row.BMCase, row.HaveAction, row.Priority, "None", "", row.BMContentId);
    var oracle = new LegacyOracle();
    oracle.Evaluate(table, te);
    var rows = (List<AlertBoardRow>)Call("ApplySolveLogic", input.ToList());
    var board = new AlertBoardData();
    Call(te ? "ApplyColorTE" : "ApplyColorNormal", rows, board);
    var expected = oracle.Hits.Where(hit => {
        var parts = hit.Split('|');
        return AlertBoardConfig.CellKeys.ContainsKey((parts[0], parts[1]));
    }).GroupBy(hit => { var p = hit.Split('|'); return AlertBoardConfig.CellKeys[(p[0], p[1])]; }).ToDictionary(group => group.Key, group => group.ToList());
    Require(board.Cells.Count == expected.Count, $"{label}: different populated cells");
    foreach (var (key, hits) in expected) {
        Require(board.Cells.TryGetValue(key, out var cell) && cell.Count == hits.Count, $"{label}/{key}: expected {hits.Count}, got {board.Cells.GetValueOrDefault(key)?.Count}");
        Require(cell.Machines.SequenceEqual(hits.Select(hit => hit.Split('|')[2]).Distinct()), $"{label}/{key}: machine list changed");
    }
    Call("BuildTotals", board);
    Require(board.GrandTotal == oracle.Hits.Count && board.LocationTotals.Values.Sum() == board.GrandTotal, $"{label}: totals disagree");
    checks++;
}
foreach (var te in new[] { false, true }) {
    Check("empty", te);
    Check("singleton", te, Row());
    Check("different content must not become a repeat", te, Row(), Row(1, content: 49, priority: "EQUIPMENT BM"));
    foreach (var count in Enumerable.Range(2, 14)) Check($"{count} equal timestamps", te, Enumerable.Range(0, count).Select(_ => Row()).ToArray());
    foreach (var hours in new[] { 0, 23, 24 }) Check($"seven days plus {hours} hours", te, Row(), Row(7, hours: hours));
    Check("action at group head", te, Row(action: "Yes"), Row(1));
    Check("action on older row", te, Row(), Row(1, action: "Yes"));
    Check("mixed cases and content", te, Row(bmCase: "CASE C"), Row(1, bmCase: "CASE B"), Row(2), Row(3));
    Check("separate machines", te, Row(mc: "MC-C"), Row(1), Row(2), Row(3, mc: "MC-A"));
    Check("middle singleton interrupts content group", te, Row(), Row(1, bmCase: "CASE B", content: 49), Row(2));
}
var random = new Random(1102);
foreach (var te in new[] { false, true })
foreach (var process in AlertBoardConfig.ProcessOrder) {
    var mapping = AlertBoardConfig.Cells.Values.First(cell => cell.Process == process);
    var rows = new[] { Row(), Row(1) };
    foreach (var row in rows) { row.Process = process; row.Location = mapping.Location; }
    Check($"{process} color mapping/{te}", te, rows);
}
foreach (var te in new[] { false, true })
for (int sample = 0; sample < 588; sample++) {
    var rows = Enumerable.Range(0, random.Next(1, 28)).Select(_ => Row(random.Next(16), "MC-" + random.Next(3), "CASE " + random.Next(3),
        new[] { 48, 49, 0 }[random.Next(3)], random.Next(4) == 0 ? "Yes" : "No", random.Next(3) == 0 ? "EQUIPMENT BM" : "QUALITY BM", random.Next(24)))
        .OrderByDescending(row => row.Mcno).ThenByDescending(row => DateTime.ParseExact(row.Timeofrequest, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)).ThenBy(row => row.BMCase).ToArray();
    var mapping = AlertBoardConfig.Cells.Values.ElementAt(sample % AlertBoardConfig.Cells.Count);
    foreach (var row in rows) { row.Process = mapping.Process; row.Location = mapping.Location; }
    Check($"random {te}/{sample}", te, rows);
}
var designer = File.ReadAllText(Path.Combine(args[0], "Legacy", "Form2.Designer.vb.txt"));
foreach (var process in AlertBoardConfig.ProcessOrder) {
    var columns = Regex.Matches(designer, $@"Me\.(lbl{process}_\d+)\.BackColor = System.Drawing.Color.White\b")
        .Select(match => Regex.Match(designer, $@"Me\.{match.Groups[1].Value}\.Location = New System.Drawing.Point\((\d+), (\d+)\)"))
        .Select(match => int.Parse(match.Groups[1].Value)).Where(x => x < 900)
        .Select(x => AlertBoardConfig.DisplayColumns[(x - 1) / 98]).ToHashSet();
    Require(columns.SetEquals(AlertBoardConfig.Cells.Values.Where(cell => cell.Process == process).Select(cell => cell.Column)), $"{process}: white cells differ from Form2 designer");
}
var mapped = new AlertBoardData();
foreach (var (key, cell) in AlertBoardConfig.Cells) {
    Call("AddToBoard", mapped, cell.Process, cell.Location, key);
    Require(mapped.Cells[key].Count == 1, $"{key}: wrong process/location mapping");
}
Call("BuildTotals", mapped);
foreach (var column in AlertBoardConfig.DisplayColumns)
    Require(mapped.LocationTotals[column] == AlertBoardConfig.Cells.Values.Count(cell => cell.Column == column), $"{column}: total is under the wrong column");
Require(mapped.GrandTotal == 54, "Designer should have 54 white cells");
var legacyLocations = Regex.Match(File.ReadAllText(Path.Combine(args[0], "Legacy", "Form2.vb.txt")), @"Dim strLocation\(\) As String = \{([^}]+)\}").Groups[1].Value;
var expectedLocations = Regex.Matches(legacyLocations, "\"([^\"]+)\"").Select(match => match.Groups[1].Value);
Require(((string[])typeof(AlertBoardRepository).GetField("Locations", flags)!.GetValue(null)!).SequenceEqual(expectedLocations), "Query location sequence differs from Form2");
Console.WriteLine($"PASS: {checks} fixtures match actual Form2 VB routines (normal/TE, group heads, mixed content, actions, ordering, whole-day boundaries); all 54 designer cells, floor totals and legacy query locations match.");
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet restore (Join-Path $checkDirectory 'Checks.csproj') --ignore-failed-sources --disable-parallel -m:1 --property:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Action summary parity runner restore failed.' }
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --no-restore --nologo -m:1 --property:UseSharedCompilation=false -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Action summary parity runner build failed.' }
& $Dotnet (Join-Path $checkDirectory 'bin/Debug/net8.0/Checks.dll') $repo
if ($LASTEXITCODE -ne 0) { throw 'Action summary legacy parity checks failed.' }
