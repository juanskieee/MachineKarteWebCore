param(
    [string]$BaseUrl = 'http://127.0.0.1:5098',
    [int]$DevToolsPort = 9227,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP 'MachineKarte-Workflow-QA')
)
# Run the application and an installed Chromium browser with --remote-debugging-port=9227.
# Browser fixtures replace only AJAX data; HTML partials and assets come from the real app.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$httpHandler = New-Object System.Net.Http.HttpClientHandler
$httpHandler.UseProxy = $false
$httpHandler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($httpHandler)
$socket = New-Object System.Net.WebSockets.ClientWebSocket
$script:messageId = 0
function Invoke-Cdp([string]$method, $parameters = @{}) {
    $script:messageId++
    $requestId = $script:messageId
    $json = @{ id = $requestId; method = $method; params = $parameters } | ConvertTo-Json -Depth 30 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $timeout = New-Object System.Threading.CancellationTokenSource
    $timeout.CancelAfter(45000)
    try {
        $socket.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult() | Out-Null
        do {
            $stream = New-Object IO.MemoryStream
            do {
                $buffer = New-Object byte[] 65536
                $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
                $stream.Write($buffer, 0, $received.Count)
            } until ($received.EndOfMessage)
            $response = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            $stream.Dispose()
        } until ($response.id -eq $requestId)
        if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
        return $response.result
    } finally { $timeout.Dispose() }
}
function Invoke-JavaScript([string]$expression) {
    $result = Invoke-Cdp 'Runtime.evaluate' @{ expression = $expression; awaitPromise = $true; returnByValue = $true }
    if ($result.exceptionDetails) { throw ($result.exceptionDetails | ConvertTo-Json -Depth 10) }
    return $result.result.value
}
try {
    $monitoringHtml = $client.GetStringAsync("$BaseUrl/Monitoring").GetAwaiter().GetResult()
    $assetPaths = @([regex]::Matches($monitoringHtml, '<(?:link|script)\b[^>]*(?:href|src)="(/[^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if (!$assetPaths.Count) { throw 'Monitoring did not render any local assets.' }
    foreach ($assetPath in $assetPaths) {
        $asset = $client.GetAsync([Uri]::new([Uri]$BaseUrl, $assetPath)).GetAwaiter().GetResult()
        try {
            if (!$asset.IsSuccessStatusCode) { throw "Missing Monitoring asset: $assetPath ($([int]$asset.StatusCode))" }
        } finally { $asset.Dispose() }
    }
    Write-Output "PASS: all $($assetPaths.Count) local Monitoring stylesheets and scripts"
    foreach ($name in @('AlertBoard','MachineSummary','McdMonitoring','BmAnalytics','RawData','OcapViewer','PmReport','BmReport','EvidenceMaking','TeEvidenceMaking','ActionCards')) {
        $redirect = $client.GetAsync("$BaseUrl/$name").GetAwaiter().GetResult()
        $fragment = $client.GetAsync("$BaseUrl/${name}?dialog=1&bmId=123&legacyForm=12").GetAwaiter().GetResult()
        $html = $fragment.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([int]$redirect.StatusCode -ne 302 -or [int]$fragment.StatusCode -ne 200 -or !$html.Contains("data-workflow=`"$name`"") -or $html.Contains('<!DOCTYPE')) { throw "$name redirect/partial failed." }
        $redirect.Dispose()
        $fragment.Dispose()
    }
    Write-Output 'PASS: all 11 redirects and real Razor partials'
    $invalid = $client.GetStringAsync("$BaseUrl/OcapViewer?handler=LoadOcap").GetAwaiter().GetResult() | ConvertFrom-Json
    if ($invalid.success -or !$invalid.message) { throw 'OCAP input validation failed.' }
    $payload = New-Object System.Net.Http.StringContent('{}', [Text.Encoding]::UTF8, 'application/json')
    $rejected = $client.PostAsync("$BaseUrl/ActionCards?handler=Save", $payload).GetAwaiter().GetResult()
    if ([int]$rejected.StatusCode -ne 400) { throw 'Action Card antiforgery protection failed.' }
    $rejected.Dispose()
    $payload.Dispose()
    Write-Output 'PASS: real handler validation and antiforgery rejection'
    $targets = $client.GetStringAsync("http://127.0.0.1:$DevToolsPort/json/list").GetAwaiter().GetResult() | ConvertFrom-Json
    $target = $targets | Where-Object { $_.type -eq 'page' -and ($_.url.StartsWith($BaseUrl) -or $_.url -eq 'about:blank') } | Select-Object -First 1
    if (!$target) { throw 'Open about:blank in the browser debugging session before running this check.' }
    $socket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    Invoke-Cdp 'Page.enable' | Out-Null
    Invoke-Cdp 'Browser.setDownloadBehavior' @{ behavior = 'deny' } | Out-Null
    $fixtures = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorkflowBrowserChecks.js'))
    Invoke-Cdp 'Page.addScriptToEvaluateOnNewDocument' @{ source = $fixtures } | Out-Null
    Invoke-Cdp 'Emulation.setDeviceMetricsOverride' @{ width = 1440; height = 1000; deviceScaleFactor = 1; mobile = $false } | Out-Null
    Invoke-Cdp 'Page.navigate' @{ url = "$BaseUrl/Monitoring" } | Out-Null
    $ready = Invoke-JavaScript "new Promise((resolve, reject) => { let attempts = 0; const timer = setInterval(() => { if (window.Workflows && document.readyState === 'complete') { clearInterval(timer); resolve(true); } else if (++attempts > 150) { clearInterval(timer); reject(new Error('Monitoring failed to load')); } }, 100); })"
    Write-Output (Invoke-JavaScript 'window.runWorkflowChecks()')
    Invoke-JavaScript "document.getElementById('btnMCD').focus(); window.prepareWorkflowCapture('MachineSummary')" | Out-Null
    Invoke-Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='Tab';code='Tab';windowsVirtualKeyCode=9;modifiers=1} | Out-Null
    if (!(Invoke-JavaScript "document.getElementById('workflowDialog').contains(document.activeElement)")) { throw 'Dialog did not trap keyboard focus.' }
    Invoke-Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='Escape';code='Escape';windowsVirtualKeyCode=27} | Out-Null
    Invoke-JavaScript "new Promise(resolve => setTimeout(resolve, 50))" | Out-Null
    if (!(Invoke-JavaScript "!document.getElementById('workflowDialog').open && document.activeElement.id === 'btnMCD'")) { throw 'Escape dismissal/focus restoration failed.' }
    Write-Output 'PASS: native focus trap, Escape dismissal, and focus restoration'
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    foreach ($viewport in @(@{name='desktop';width=1440;height=1000}, @{name='mobile';width=390;height=844})) {
        Invoke-Cdp 'Emulation.setDeviceMetricsOverride' @{ width=$viewport.width; height=$viewport.height; deviceScaleFactor=1; mobile=($viewport.name -eq 'mobile') } | Out-Null
        foreach ($surface in @('Monitoring','ActionCards','MachineSummary','BmAnalytics','RawData','OcapViewer')) {
            Write-Output (Invoke-JavaScript ("window.prepareWorkflowCapture('" + $surface + "')"))
            $capture = Invoke-Cdp 'Page.captureScreenshot' @{ format='png'; captureBeyondViewport=$false }
            [IO.File]::WriteAllBytes((Join-Path $ArtifactDirectory ($viewport.name + '-' + $surface + '.png')), [Convert]::FromBase64String($capture.data))
        }
    }
    Write-Output "PASS: desktop/mobile captures in $ArtifactDirectory"
} finally { $socket.Dispose(); $client.Dispose() }
