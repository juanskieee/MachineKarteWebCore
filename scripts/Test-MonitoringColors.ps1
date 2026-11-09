param([string]$Dotnet = 'dotnet', [string]$ArtifactDirectory = (Join-Path $env:TEMP 'MachineKarte-MonitoringColors'))
# Run the actual uc_Test color/capacity/timer routines against in-memory WinForms controls.
# No application configuration, SQL connections, or desktop windows are opened.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-MonitoringColors-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $checkDirectory 'LegacyOracle') -Force | Out-Null
$projectPath = [Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><UseAppHost>false</UseAppHost></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /><ProjectReference Include="LegacyOracle/LegacyOracle.vbproj" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><RootNamespace></RootNamespace><NuGetAudit>false</NuGetAudit><NoWarn>$(NoWarn);42016;42017;42024</NoWarn></PropertyGroup>
</Project>
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'LegacyOracle/LegacyOracle.vbproj')
$legacy = [IO.File]::ReadAllText((Join-Path $repo 'Legacy/uc_Test.vb.txt'))
$oracle = @'
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Collections.Generic
Imports System.Windows.Forms
Public Module Form1
    Public cbProcess As New ProcessSelection
End Module
Public Class ProcessSelection
    Public Text As String
End Class
Public Class LegacyCardOracle
    Inherits UserControl
    Private NeedActionCount As Integer
    Private TimerNum As Integer
    Private dtNewRowSame As DataTable
    Public Function State(index As Integer, part As String, propertyName As String) As String
        Dim item = Controls.Find(part & index, True).First()
        Select Case propertyName
            Case "Text" : Return item.Text
            Case "BackColor" : Return item.BackColor.Name
            Case "ForeColorHex" : Return "#" & (item.ForeColor.ToArgb() And &HFFFFFF).ToString("X6")
            Case Else : Return item.ForeColor.Name
        End Select
    End Function
    Public Function Evaluate(dt1 As DataTable, process As String, te As Boolean, actionLine As Integer, headerCount As Integer) As Integer
        Form1.cbProcess.Text = process
        TimerNum = dt1.Rows.Count - 1
        For i = 0 To dt1.Rows.Count - 1
            For Each part In {"lblMC1_Case", "lblMC1_Header", "lblMC1_Date", "lblMC1_Color", "lblMC1_MCD", "lblMC1_HeaderRed", "lblMC1_HeaderBlue", "lblMC1_HeaderPurple", "lblMC1_HeaderGreen"}
                Dim item As New Label With {.Name = part & i, .ForeColor = Color.Black}
                item.BackColor = If(part = "lblMC1_Case", Color.MistyRose, Color.Salmon)
                If part = "lblMC1_Color" Then item.Text = "Label1"
                If part = "lblMC1_MCD" Then item.BackColor = Color.WhiteSmoke
                Controls.Add(item)
            Next
            Controls.Add(New Button With {.Name = "btn" & i, .BackColor = Color.Red, .ForeColor = Color.White})
'@
# Preserve the original content/accreditation and resolved-header text/color branch.
$headerStart = $legacy.IndexOf('                If eUser = "PM" Or eUser = "CC" Or eUser = "EEE" Or eUser = "VIS" Then')
$headerEnd = $legacy.IndexOf('                .Font = New Font("Microsoft Sans Serif", 7)', $headerStart)
if ($headerStart -lt 0 -or $headerEnd -lt 0) { throw 'Original card header initialization was not found.' }
$oracle += "`n            Dim eUser = If(te, ""TE"", ""PM"")`n            With CType(Controls.Find(""lblMC1_Header"" & i, True).First, Label)`n" + $legacy.Substring($headerStart, $headerEnd - $headerStart) + "            End With`n"
# Execute the original action-button/marker initialization block verbatim.
$actionStart = $legacy.IndexOf('            If (dt1.Rows(i).Item("HaveAction").ToString) <> "No" Then')
$actionEnd = $legacy.IndexOf('        Next', $actionStart)
if ($actionStart -lt 0 -or $actionEnd -lt 0) { throw 'Original action-button initialization was not found.' }
$oracle += "`n" + $legacy.Substring($actionStart, $actionEnd - $actionStart) + @'
        Next
        If dt1.Rows.Count > 0 Then
            LoadProcessCapa(actionLine, headerCount)
            CheckIfSolve(dt1)
            If te Then
                LoadColor_TE(dt1, process)
            Else
                LoadColor(dt1, process)
            End If
        End If
        Return NeedActionCount
    End Function
    Public Sub Tick()
        Timer1_Tick_1(Nothing, EventArgs.Empty)
    End Sub
'@
foreach ($method in @('CheckIfSolve','GetDuplicateRowsByColumn','AreOtherRowsSame','LoadProcessCapa','CheckifSevendays','LoadColor','LoadColor_TE','GetFontColor','AreNextOneColumnValuesEqual','AreNextTwoColumnValuesEqual','AreNextThreeColumnValuesEqual','AreNextFourColumnValuesEqual','AreNextFiveColumnValuesEqual','Timer1_Tick_1')) {
    $match = [regex]::Match($legacy, "(?ms)^    (?:Private )?(?:Sub|Function) $method\(.*?^    End (?:Sub|Function)")
    if (!$match.Success) { throw "Missing original routine: $method" }
    $oracle += "`n" + $match.Value.Replace(' Handles Timer1.Tick', '') + "`n"
}
$oracle += "End Class`n"
[IO.File]::WriteAllText((Join-Path $checkDirectory 'LegacyOracle/Oracle.vb'), $oracle)
@'
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var flags = BindingFlags.Static | BindingFlags.NonPublic;
void Apply(string name, params object[] values) => typeof(MonitoringRepository).GetMethod(name, flags)!.Invoke(null, values);
BMCardRow Row(double age = 0, string content = "48", string priority = "QUALITY BM", string action = "No", string bmCase = "CASE", string status = "", string type = "TYPE") => new() {
    BMId = 1, MachineNo = "MC", MachineType = type, Location = "LSI2-3F", Process = "DC",
    BMCase = bmCase, BMContentId = content, BMContent = "CONTENT", Priority = priority,
    HaveAction = action, MCStatus = status, IsMCD = "Yes",
    TimeOfRequest = new DateTime(2026,11,1,12,0,0).AddDays(-age).ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
};
var differences = new List<string>();
var fixtures = new List<object>();
int checks = 0;
void Check(string label, string process, bool te, int actionLine, int? headerCount, params BMCardRow[] input) {
    var rows = input.ToList();
    var table = new DataTable();
    foreach (var name in new[] {"Mcno","Timeofrequest","BMCase","BMContent","HaveAction","MCStatus","isMCD","Priority","ButtonName","Accre","Sect","Dept","Div","BMStatusID","AcreBMContent"}) table.Columns.Add(name);
    table.Columns.Add("BMContentID",typeof(int)); table.Columns.Add("BMID",typeof(int));
    for (var i=0;i<rows.Count;i++) {
        var r=rows[i]; r.BMId=i+1; r.Process=process;
        table.Rows.Add(r.MachineNo,r.TimeOfRequest,r.BMCase,r.BMContent,r.HaveAction,r.MCStatus,r.IsMCD,r.Priority,"btn"+i,"","","","",r.BMStatusId,r.DisplayContent,int.Parse(r.BMContentId),r.BMId);
    }
    using var original = new LegacyCardOracle();
    int count=original.Evaluate(table,process,te,actionLine,headerCount??rows.Count);
    var expected=rows.Select((r,i)=>new {
        fontColor=original.State(i,"lblMC1_Color","Text") is "Label1" ? "None" : original.State(i,"lblMC1_Color","Text"),
        actionColor=original.State(i,"btn","BackColor"),
        needsAction=original.State(i,"lblMC1_Header","BackColor")=="Red",
        mcdNeedsAction=r.IsMCD=="Yes" && original.State(i,"lblMC1_MCD","ForeColor")=="Blue",
        header=original.State(i,"lblMC1_Header","BackColor"),
        caseBackground=original.State(i,"lblMC1_Case","BackColor"),
        headerForeground=original.State(i,"lblMC1_Header","ForeColor"),
        caseForeground=original.State(i,"lblMC1_Case","ForeColorHex"),
        headerText=original.State(i,"lblMC1_Header","Text")
    }).ToArray();
    Apply("ApplyLegacyActionColors",rows);
    Apply("ApplyLegacyRepeatColors",rows,te?"TE":"PM",process);
    for(var i=0;i<rows.Count;i++) {
        var r=rows[i]; var e=expected[i];
        if(r.FontColor!=e.fontColor || r.ActionColor!=e.actionColor || r.NeedsAction!=e.needsAction || r.McdNeedsAction!=e.mcdNeedsAction)
            differences.Add($"{label}/row {i}: expected font/action/need/MCD {e.fontColor}/{e.actionColor}/{e.needsAction}/{e.mcdNeedsAction}, got {r.FontColor}/{r.ActionColor}/{r.NeedsAction}/{r.McdNeedsAction}");
    }
    if(count!=rows.Count(r=>r.NeedsAction)) differences.Add($"{label}: Need Action count {count} vs {rows.Count(r=>r.NeedsAction)}");
    original.Tick();
    var tick=rows.Select((r,i)=>new {
        header=original.State(i,"lblMC1_Header","BackColor"),
        caseForeground=original.State(i,"lblMC1_Case","ForeColorHex"),
        mcdBackground=original.State(i,"lblMC1_MCD","BackColor")
    }).ToArray();
    fixtures.Add(new {label,userType=te?"TE":"PM",machineNo="MC",machineType="TYPE",actionLine,bmCount=headerCount??rows.Count,needActionCount=count,bmCards=rows,expected,tick});
    checks++;
}
foreach(var te in new[]{false,true}) {
    Check("singleton/"+te,"DC",te,4,null,Row());
    for(var n=2;n<=14;n++) Check($"{n} repeats/{te}","DC",te,4,null,Enumerable.Range(0,n).Select(_=>Row()).ToArray());
    foreach(var age in new[]{7d,7.9,8}) Check("date boundary/"+age+"/"+te,"DC",te,4,null,Row(),Row(age));
    foreach(var status in new[]{"RUNNING (ONGOING INVESTIGATION/MONITORING)","RUNNING (UNDER 100% INSPECTION)","STOP (ONGOING INVESTIGATION)","RUNNING (LOT EVALUATION)","NO RE-OCCURRENCE AFTER 7 DAYS","STOP (WAITING FOR INSPECTION RESULTS)","STOP (WAITING FOR QC INSTRUCTION)"})
        Check(status+"/"+te,"DC",te,4,null,Row(action:"Yes",status:status),Row(1));
    Check("noncanonical status/"+te,"DC",te,4,null,Row(action:"Yes",status:"running (lot evaluation)"),Row(1,action:"Yes",status:" RUNNING (LOT EVALUATION) "));
    Check("solved older action/"+te,"DC",te,4,null,Row(bmCase:"OTHER"),Row(7,action:"Yes"),Row(9,action:"Yes"));
    Check("mixed type/"+te,"DC",te,4,null,Row(),Row(1,type:"OLD"));
    Check("long gap/"+te,"DC",te,4,null,Row(),Row(1),Row(10),Row(11),Row(12),Row(13),Row(14),Row(20));
    for(var n=1;n<=6;n++) Check("equipment MCD/"+n+"/"+te,"DC",te,4,null,Enumerable.Range(0,n).Select(_=>Row(priority:"EQUIPMENT BM")).ToArray());
}
foreach(var (process,blue,red) in new[]{("DC",48,49),("DB",18,19),("WB",45,46),("MP",24,25),("PL",42,43),("FL",1,2),("SING",1,2),("ATC",1,2),("FT",31,33),("TP",9,30),("EDS",13,16),("MEMS",72,74)})
foreach(var content in new[]{blue,red,0}) Check(process+"/"+content,process,false,4,null,Row(content:content.ToString()),Row(1,content:content.ToString()));
for(var n=1;n<=14;n++) Check("capacity/"+n,"DC",false,4,null,Enumerable.Range(0,n).Select(i=>Row(i,bmCase:"CASE "+i)).ToArray());
Check("SP-33 header 8, history 9","SING",false,4,8,Enumerable.Range(0,9).Select(i=>Row(i,content:"1",bmCase:"CASE "+i)).ToArray());
var padded=Row(1); padded.MachineNo="mc "; padded.Location="lsi2-3f ";
Check("SQL-padded machine identity","DC",false,4,null,Row(),padded);
var resolved=Row(); resolved.BMStatusId="15";
Check("OCAP resolved header","DC",false,4,null,resolved);
var accredited=Row(); accredited.DisplayContent="Accredited content";
Check("accredited header","DC",false,4,null,accredited);
var random=new Random(1109);
foreach(var te in new[]{false,true})
for(var sample=0;sample<120;sample++) {
    var rows=Enumerable.Range(0,random.Next(1,15)).Select(_=>Row(random.Next(0,25)/2d,content:random.Next(3)==0?"49":"48",priority:random.Next(3)==0?"EQUIPMENT BM":"QUALITY BM",action:random.Next(4)==0?"Yes":"No",bmCase:"CASE "+random.Next(3))).OrderByDescending(r=>DateTime.ParseExact(r.TimeOfRequest,"MM/dd/yyyy HH:mm:ss",CultureInfo.InvariantCulture)).ToArray();
    Check($"mixed {sample}/{te}","DC",te,4,null,rows);
}
Directory.CreateDirectory(args[0]);
File.WriteAllText(Path.Combine(args[0],"cards.json"),JsonSerializer.Serialize(fixtures,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
foreach(var difference in differences.Take(18)) Console.WriteLine(difference);
if(differences.Count>0) throw new Exception($"{differences.Count} row/count differences against compiled uc_Test across {checks} focused cases.");
Console.WriteLine($"PASS: {checks} cases match compiled original uc_Test repeat/action colors, Need Action counts and MCD blink eligibility. Capacity/timer expectations: {args[0]}");
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --nologo -v minimal -m:1 --nodeReuse:false --property:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Monitoring color oracle build failed.' }
& $Dotnet (Join-Path $checkDirectory 'bin/Debug/net8.0-windows/Checks.dll') $ArtifactDirectory
if ($LASTEXITCODE -ne 0) { throw 'Monitoring colors differ from the original uc_Test routines.' }
