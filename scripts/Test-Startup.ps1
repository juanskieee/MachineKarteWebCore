param(
    [string]$Dotnet = 'dotnet',
    [switch]$MeasureOnly,
    [int]$MaximumPageMilliseconds = 3000,
    [string]$PublishDirectory = '',
    [string]$PathBase = ''
)
# Starts its own app with a loopback TCP sink, never the configured SQL server.
# The sink never speaks SQL, so no query or write can reach a database.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repo = Split-Path $PSScriptRoot -Parent
$workingDirectory = if ($PublishDirectory) { (Resolve-Path -LiteralPath $PublishDirectory).Path } else { $repo }
$assembly = if ($PublishDirectory) { Join-Path $workingDirectory 'MachineKarteWebCore.dll' } else { Join-Path $repo 'bin/Debug/net8.0/MachineKarteWebCore.dll' }
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the application before running this check.' }
$sink = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$reservation = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$sink.Start()
$reservation.Start()
$sqlPort = $sink.LocalEndpoint.Port
$httpPort = $reservation.LocalEndpoint.Port
$reservation.Stop()
$listenUrl = "http://127.0.0.1:$httpPort"
$PathBase = $PathBase.TrimEnd('/')
$baseUrl = $listenUrl + $PathBase
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command $Dotnet).Source
$start.Arguments = '"' + $assembly + '" --urls ' + $listenUrl
$start.WorkingDirectory = $workingDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
$start.EnvironmentVariables['Logging__EventLog__LogLevel__Default'] = 'None'
$start.EnvironmentVariables['Logging__LogLevel__Microsoft.Hosting.Lifetime'] = 'Information'
$start.EnvironmentVariables['ConnectionStrings__MachineDBConnection'] = "Server=tcp:127.0.0.1,$sqlPort;Database=StartupChecks;Integrated Security=true;Encrypt=false;Connect Timeout=8;Connect Retry Count=0;Max Pool Size=1"
$start.EnvironmentVariables['ConnectionStrings__IntranetDBConnection'] = $start.EnvironmentVariables['ConnectionStrings__MachineDBConnection']
$testApp = New-Object Diagnostics.Process
$testApp.StartInfo = $start
$handler = New-Object Net.Http.HttpClientHandler
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = New-Object Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
if ($PathBase) {
    # Exercise ASP.NET Core's IIS integration locally, without changing the app pipeline.
    $pairingToken = [guid]::NewGuid().ToString('N')
    $start.EnvironmentVariables['ASPNETCORE_PORT'] = [string]$httpPort
    $start.EnvironmentVariables['ASPNETCORE_APPL_PATH'] = $PathBase
    $start.EnvironmentVariables['ASPNETCORE_TOKEN'] = $pairingToken
    $client.DefaultRequestHeaders.Add('MS-ASPNETCORE-TOKEN', $pairingToken)
}
$cancelOcap = New-Object Threading.CancellationTokenSource
$started = $false
function Require($condition, [string]$message) { if (!$condition) { throw $message } }
function Page {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $response = $client.GetAsync("$baseUrl/Monitoring").GetAwaiter().GetResult()
    try {
        Require ([int]$response.StatusCode -eq 200) 'Monitoring failed to render.'
        $html = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Require ($html.Contains('id="btnSearch"')) 'Monitoring controls are missing.'
        return @{ Milliseconds = [Math]::Round($clock.Elapsed.TotalMilliseconds, 1); Html = $html }
    } finally { $response.Dispose() }
}
try {
    $launch = [Diagnostics.Stopwatch]::StartNew()
    $started = $testApp.Start()
    $stderr = $testApp.StandardError.ReadToEndAsync()
    $line = $testApp.StandardOutput.ReadLineAsync()
    $ready = $false
    while ($launch.Elapsed.TotalSeconds -lt 45 -and !$ready) {
        if ($testApp.HasExited) { throw ('Test app stopped: ' + $stderr.GetAwaiter().GetResult()) }
        if ($line.IsCompleted) {
            $value = $line.GetAwaiter().GetResult()
            $ready = $value -like "*Now listening on: $listenUrl*"
            if (!$ready) { $line = $testApp.StandardOutput.ReadLineAsync() }
        } else { Start-Sleep -Milliseconds 25 }
    }
    Require $ready 'Test app did not start within 45 seconds.'
    $hostMs = [Math]::Round($launch.Elapsed.TotalMilliseconds, 1)
    $first = Page
    Write-Output "MEASURE: host ready=${hostMs}ms; first Monitoring HTML=$($first.Milliseconds)ms; SQL sink timeout=8000ms. No SQL server was contacted."
    if ($MeasureOnly) { return }
    Require ($first.Milliseconds -lt $MaximumPageMilliseconds) 'First Monitoring page waited on unavailable SQL.'
    $redirect = $client.GetAsync("$baseUrl/").GetAwaiter().GetResult()
    try { Require ([int]$redirect.StatusCode -eq 302 -and $redirect.Headers.Location.OriginalString -eq "$PathBase/Monitoring") 'Home redirect escaped the application path.' }
    finally { $redirect.Dispose() }
    $redirect = $client.GetAsync("$baseUrl/RawData?machine=TEST%26MACHINE").GetAwaiter().GetResult()
    try { Require ([int]$redirect.StatusCode -eq 302 -and $redirect.Headers.Location.OriginalString.StartsWith("$PathBase/Monitoring?workflow=RawData&machine=TEST%26MACHINE")) 'Workflow bookmark lost its application path or parameters.' }
    finally { $redirect.Dispose() }
    Require ($first.Html.Contains("data-app-root=`"$PathBase/`"")) 'Browser application root is incorrect.'
    $assetPaths = @([regex]::Matches($first.Html, '<(?:link|script|img)\b[^>]*(?:href|src)="(/[^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $assetPaths += [regex]::Match($first.Html, 'data-cat-gifs="([^"]+)"').Groups[1].Value.Split(' ')
    Require ($assetPaths.Count -gt 0) 'Monitoring assets are missing.'
    foreach ($assetPath in $assetPaths) {
        Require ($assetPath.StartsWith("$PathBase/")) "Asset escaped the application path: $assetPath"
        $asset = $client.GetAsync([Uri]::new([Uri]$listenUrl, $assetPath)).GetAwaiter().GetResult()
        try { Require ([int]$asset.StatusCode -eq 200) "Published asset failed: $assetPath" }
        finally { $asset.Dispose() }
    }
    $report = $client.GetAsync("$baseUrl/BmReport?dialog=1&bmId=123&process=FT").GetAwaiter().GetResult()
    try { Require ([int]$report.StatusCode -eq 200 -and $report.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("action=`"$PathBase/BmReport?handler=DownloadReport`"")) 'Report form escaped the application path.' }
    finally { $report.Dispose() }
    Write-Output "PASS: home/bookmark redirects, browser application root, all $($assetPaths.Count) assets and report action under '$PathBase/'."
    $token = [Net.WebUtility]::HtmlDecode([regex]::Match($first.Html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
    Require ($token.Length -gt 0) 'Monitoring did not issue its antiforgery token.'
    $unprotected = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, "$baseUrl/Monitoring?handler=TrackSession")
    $rejected = $client.SendAsync($unprotected).GetAwaiter().GetResult()
    try { Require ([int]$rejected.StatusCode -eq 400) 'Tracking POST lost antiforgery protection.' }
    finally { $rejected.Dispose(); $unprotected.Dispose() }
    $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, "$baseUrl/Monitoring?handler=TrackSession")
    $request.Headers.Add('RequestVerificationToken', $token)
    $trackingClock = [Diagnostics.Stopwatch]::StartNew()
    $tracking = $client.SendAsync($request)
    Start-Sleep -Milliseconds 100
    $duringTracking = Page
    Require ($duringTracking.Milliseconds -lt $MaximumPageMilliseconds) 'PC tracking blocked another Monitoring page.'
    $tracked = $tracking.GetAwaiter().GetResult()
    $trackingClock.Stop()
    try { Require ([int]$tracked.StatusCode -eq 204) 'Best-effort tracking did not complete cleanly.' }
    finally { $tracked.Dispose(); $request.Dispose() }
    Require ($trackingClock.Elapsed.TotalMilliseconds -lt 5000) 'PC tracking exceeded its bounded wait.'
    $ocap = $client.GetAsync("$baseUrl/OcapViewer?handler=LoadOcap&bmId=1&bmProcess=DC&bmCaseId=1", $cancelOcap.Token)
    Start-Sleep -Milliseconds 100
    $duringOcap = Page
    Require ($duringOcap.Milliseconds -lt $MaximumPageMilliseconds) 'An unavailable OCAP connection blocked Monitoring.'
    $cancelOcap.Cancel()
    try { $response = $ocap.GetAwaiter().GetResult(); $response.Dispose() }
    catch { if (!$cancelOcap.IsCancellationRequested) { throw } }
    Write-Output "PASS: Tracking POST=$([Math]::Round($trackingClock.Elapsed.TotalMilliseconds,1))ms; Monitoring during tracking=$($duringTracking.Milliseconds)ms, during OCAP=$($duringOcap.Milliseconds)ms. Page rendering is independent of SQL availability."
} finally {
    $cancelOcap.Cancel()
    $cancelOcap.Dispose()
    $client.Dispose()
    if ($started -and !$testApp.HasExited) { $testApp.Kill(); [void]$testApp.WaitForExit(5000) }
    $testApp.Dispose()
    $sink.Stop()
}
