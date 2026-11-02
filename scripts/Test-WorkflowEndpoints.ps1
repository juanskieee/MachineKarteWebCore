param([string]$BaseUrl = 'http://127.0.0.1:5098')
# Real HTTP checks: no AJAX fixtures, SQL reads, saves, or report generation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repo = Split-Path $PSScriptRoot -Parent
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$createdFiles = @()
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
    $html = Get-Html '/ActionCards?dialog=1'
    $token = [Net.WebUtility]::HtmlDecode([regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
    Require ($token.Length -gt 0) 'Workflow did not issue an antiforgery token.'
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

    $id = [Guid]::NewGuid().ToString('N')
    $fileName = "Workflow-Endpoint-$id.xlsx"
    $teFileName = "TE_Evidence_$id.xlsx"
    $bytes = [Text.Encoding]::UTF8.GetBytes('Workflow download endpoint fixture')
    $output = Join-Path $repo 'TempData/GeneratedPdf'
    foreach ($name in @($fileName,$teFileName)) {
        $path = Join-Path $output $name
        [IO.File]::WriteAllBytes($path, $bytes)
        $createdFiles += $path
    }
    foreach ($check in @(
        @{path="/PmReport?handler=Download&fileName=$fileName";name=$fileName},
        @{path="/EvidenceMaking?handler=Download&fileName=$fileName";name=$fileName},
        @{path="/TeEvidenceMaking?handler=Download&bmId=$id";name=$teFileName}
    )) {
        $response = Get-Response $check.path
        try {
            $actual = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            Require ([Convert]::ToBase64String($actual) -eq [Convert]::ToBase64String($bytes)) "$($check.path) changed download bytes."
            Require ($response.Content.Headers.ContentType.MediaType -eq 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet') 'Download content type changed.'
            Require ($response.Content.Headers.ContentDisposition.ToString().Contains($check.name)) 'Download filename changed.'
        } finally { $response.Dispose() }
    }
    foreach ($name in @('PmReport','EvidenceMaking')) {
        $response = Get-Response "/${name}?handler=Download&fileName=..%2Finvalid.xlsx" 400
        $response.Dispose()
        $response = Get-Response "/${name}?handler=Download&fileName=Missing-$id.xlsx" 404
        $response.Dispose()
    }
    $response = Get-Response '/TeEvidenceMaking?handler=Download' 400
    $response.Dispose()
    Write-Output 'PASS: all three existing download routes stream exact fixture bytes, MIME types and filenames; missing/invalid file validation is retained.'
} finally {
    foreach ($path in $createdFiles) { Remove-Item -LiteralPath $path }
    $client.Dispose()
}
