param(
    [string]$BaseUrl = 'http://127.0.0.1:5098',
    [switch]$ReportsOnly
)
# Real HTTP checks: no AJAX fixtures, SQL reads, saves, or report generation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repo = Split-Path $PSScriptRoot -Parent
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
function Require($condition, [string]$message) { if (!$condition) { throw $message } }
function Get-Response([string]$path, [int]$status = 200) {
    $response = $client.GetAsync($BaseUrl + $path).GetAwaiter().GetResult()
    Require ([int]$response.StatusCode -eq $status) "$path returned $([int]$response.StatusCode), expected $status."
    return $response
}
function Get-Html([string]$path) {
    $response = Get-Response $path
    try { return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
    finally { $response.Dispose() }
}
function Expect-Failure([string]$path, [string]$message) {
    $response = Get-Response $path
    try {
        Require ($response.Content.Headers.ContentType.MediaType -eq 'application/json') "$path did not return JSON."
        $data = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Require (!$data.success -and $data.message.Contains($message)) "$path lost its validation response."
    } finally { $response.Dispose() }
}
function Post-Json([string]$path, [string]$body, [string]$token = '') {
    $request = New-Object System.Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, ($BaseUrl + $path))
    $request.Content = New-Object System.Net.Http.StringContent($body, [Text.Encoding]::UTF8, 'application/json')
    if ($token) { $request.Headers.Add('RequestVerificationToken', $token) }
    try { return $client.SendAsync($request).GetAwaiter().GetResult() }
    finally { $request.Dispose() }
}
try {
    if (!$ReportsOnly) {
    $names = @('AlertBoard','MachineSummary','McdMonitoring','BmAnalytics','RawData','OcapViewer','PmReport','BmReport','EvidenceMaking','TeEvidenceMaking','ActionCards')
    foreach ($name in $names) {
        Require (!(Test-Path -LiteralPath (Join-Path $repo "Pages/$name.cshtml"))) "$name still has a standalone route page."
        Require (!(Test-Path -LiteralPath (Join-Path $repo "Pages/$name.cshtml.cs"))) "$name still has a PageModel."
        $response = Get-Response "/${name}?bmId=123&process=DC&workflow=ignored&dialog=0" 302
        try {
            $target = $response.Headers.Location.OriginalString
            Require ($target.StartsWith("/Monitoring?workflow=$name&") -and $target.Contains('bmId=123') -and $target.Contains('process=DC') -and !$target.Contains('dialog=') -and !$target.Contains('ignored')) "$name lost bookmark redirect parameters."
        } finally { $response.Dispose() }
        $html = Get-Html "/${name}?dialog=1&bmId=123&process=DC&legacyForm=12"
        Require ($html.Contains("data-workflow=`"$name`"") -and !$html.Contains('<!DOCTYPE')) "$name no longer returns a partial."
        $response = Get-Response "/${name}?dialog=1&handler=UnknownHandler" 404
        $response.Dispose()
    }
    Require ((Get-Html '/BmAnalytics?dialog=1&legacyForm=12').Contains('value="12"')) 'Form12 profile binding failed.'
    Require ((Get-Html '/BmAnalytics?dialog=1&legacyForm=invalid').Contains('value="8"')) 'Form8 fallback changed.'
    foreach ($name in @('PmReport','BmReport')) {
        $html = Get-Html "/${name}?dialog=1&bmId=456&process=FT"
        Require ($html.Contains('456') -and $html.Contains('FT')) "$name lost its clicked record."
    }
    $response = Get-Response '/BmReport?dialog=1&bmId=0' 400
    $response.Dispose()
    $html = Get-Html '/OcapViewer?dialog=1&bmId=456&bmProcess=SING&bmCaseId=789'
    Require ($html.Contains('value="456"') -and $html.Contains('value="SING"') -and $html.Contains('value="789"')) 'OCAP view-model binding failed.'
    $html = Get-Html '/RawData?dialog=1&process=DC&location=LSI2-3F&machine=MC%26TEST&dateFrom=2026-10-03&dateTo=2026-11-03'
    foreach ($value in @('DC','LSI2-3F','MC&amp;TEST','2026-10-03','2026-11-03')) { Require ($html.Contains("value=`"$value`"")) "Raw Data lost/failed to encode $value." }
    Write-Output 'PASS: all 11 controller partials/bookmarks, removed page pairs, unknown handlers, profile and clicked-record view models.'

    $response = Get-Response '/aCtIoNcArDs?HaNdLeR=lOaD&bmId=456&mcNo=MC-TEST&userType=TE&status=New&colorName=Red&headerCase=TEST%20CASE'
    try {
        $card = ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).data
        Require ($card.bmId -eq '456' -and $card.machineNo -eq 'MC-TEST' -and $card.userType -eq 'TE' -and $card.headerCase -eq 'TEST CASE' -and $card.mcStatus -eq 'STOP (ONGOING INVESTIGATION)') 'Action Card query binding/camelCase response changed.'
    } finally { $response.Dispose() }
    Expect-Failure '/OcapViewer?handler=LoadOcap&bmId=0&bmProcess=DC&bmCaseId=1' 'Required parameters missing'
    Expect-Failure '/RawData?handler=LoadData&process=DC' 'Required parameters missing'
    Expect-Failure '/McdMonitoring?handler=Dashboard&userRole=TE' 'Date range is required'
    Expect-Failure '/McdMonitoring?handler=MachineList&cellKey=INVALID' 'Unknown cell key'
    Expect-Failure '/PmReport?handler=Generate&process=DC&bmId=0' 'valid BM ID'
    Expect-Failure '/TeEvidenceMaking?handler=Generate' 'bmId is required'
    Expect-Failure '/TeEvidenceMaking?handler=Generate&bmId=123&process=INVALID' 'valid report process'
    $html = Get-Html '/ActionCards?dialog=1'
    $token = [Net.WebUtility]::HtmlDecode([regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
    Require ($token.Length -gt 0) 'Workflow did not issue an antiforgery token.'
    $floors = (Get-Html '/Monitoring?handler=FloorList' | ConvertFrom-Json).data
    Require ($floors.Count -eq 10 -and $floors -contains 'LSI2-3F' -and $floors -notcontains 'All') 'Location options still include an unrestricted selection.'
    foreach ($floor in @('All','ALL','all','','-Select-','UNKNOWN')) {
        $encoded = [Uri]::EscapeDataString($floor)
        Expect-Failure "/Monitoring?handler=MachineList&userType=PM&process=DC&floor=$encoded" 'specific location'
        Expect-Failure "/Monitoring?handler=MachineCard&userType=PM&process=DC&floor=$encoded&machineNo=TEST" 'specific location'
        $body = @{userType='PM';process='DC';floor=$floor;machine='All';dateFrom='2026-10-01';dateTo='2026-11-01'} | ConvertTo-Json -Compress
        $response = Post-Json '/Monitoring?handler=Search' $body $token
        try {
            $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Require (!$result.success -and $result.message.Contains('specific location')) 'Monitoring search accepted an unrestricted location.'
        } finally { $response.Dispose() }
    }
    Write-Output 'PASS: specific Location options and pre-database guards on machine lists, searches, and cards.'
    foreach ($path in @('/ActionCards?handler=Save','/AlertBoard?handler=LoadBoard','/BmAnalytics?handler=LoadGrid','/EvidenceMaking?handler=Generate','/BmReport?handler=DownloadReport')) {
        $response = Post-Json $path '{}'
        try { Require ([int]$response.StatusCode -eq 400) "$path lost antiforgery protection." }
        finally { $response.Dispose() }
    }
    foreach ($check in @(
        @{path='/ActionCards?handler=Save';body='{"card":null}';message='Invalid request.'},
        @{path='/ActionCards?handler=Save';body='{"card":{"bmId":"TEST","mcStatus":"-SELECT-"},"statusId":"New"}';message='Please select a MC Status.'},
        @{path='/EvidenceMaking?handler=Generate';body='{"process":"DC","bmId":0}';message='Please enter a valid BM ID.'}
    )) {
        $response = Post-Json $check.path $check.body $token
        try {
            Require ([int]$response.StatusCode -eq 200) "$($check.path) rejected a valid antiforgery token."
            $data = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            Require (!$data.success -and $data.message -eq $check.message) "$($check.path) JSON body binding changed."
        } finally { $response.Dispose() }
    }
    $response = $client.GetAsync($BaseUrl + '/ActionCards?handler=Save').GetAwaiter().GetResult()
    try { Require ([int]$response.StatusCode -in @(404,405)) 'GET can invoke the POST Save action.' }
    finally { $response.Dispose() }
    Write-Output 'PASS: real GET/JSON binding, case-insensitive handler selection, HTTP method restrictions, validation and all five POST antiforgery guards.'
    }

    foreach ($name in @('PmReport','BmReport','EvidenceMaking','TeEvidenceMaking')) {
        $html=Get-Html "/${name}?dialog=1&bmId=123&process=DC"
        Require (!$html.Contains('report-history')) "$name still renders server history."
        foreach ($removed in @('History','Download')) {
            $response=Get-Response "/${name}?handler=$removed&bmId=123&process=DC&fileName=DC_123.pdf" 404
            $response.Dispose()
        }
    }
    Expect-Failure '/PmReport?handler=Generate&process=DC&bmId=0' 'valid BM ID'
    Expect-Failure '/TeEvidenceMaking?handler=Generate' 'bmId is required'
    $html=Get-Html '/EvidenceMaking?dialog=1'
    $token=[Net.WebUtility]::HtmlDecode([regex]::Match($html,'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
    $response=Post-Json '/EvidenceMaking?handler=Generate' '{"process":"DC","bmId":0}' $token
    try {
        Require ([int]$response.StatusCode -eq 200) 'Evidence generation rejected the antiforgery token.'
        $failure=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Require (!$failure.success -and $failure.message.Contains('valid BM ID')) 'Evidence validation changed.'
    } finally { $response.Dispose() }
    Write-Output 'PASS: four report partials without history, removed storage routes, and generation validation/antiforgery; no database or output files used.'
} finally { $client.Dispose() }
