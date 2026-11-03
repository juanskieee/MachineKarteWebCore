param([Parameter(Mandatory=$true)][string]$Pdf, [Parameter(Mandatory=$true)][string]$OutputDirectory)
# Render the actual exported PDF with Windows' PDF engine, independently of GemBox.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$taskMethod=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
function Await-Result($operation,[type]$resultType) {
    $task=$taskMethod.MakeGenericMethod($resultType).Invoke($null,@($operation))
    $task.Wait(); return $task.Result
}
$file=Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync([IO.Path]::GetFullPath($Pdf))) ([Windows.Storage.StorageFile])
$document=Await-Result ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
for($index=0;$index -lt $document.PageCount;$index++) {
    $page=$document.GetPage($index)
    $stream=New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    try {
        $options=New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $options.DestinationWidth=1200
        $action=$page.RenderToStreamAsync($stream,$options)
        $actionMethod=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and !$_.IsGenericMethod -and $_.GetParameters().Count -eq 1} | Select-Object -First 1
        $task=$actionMethod.Invoke($null,@($action)); $task.Wait()
        $reader=[System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
        $reader.Position=0
        $output=Join-Path $OutputDirectory ('{0}-{1:D2}.png' -f [IO.Path]::GetFileNameWithoutExtension($Pdf),($index+1))
        $destination=[IO.File]::Create($output)
        try {$reader.CopyTo($destination)} finally {$destination.Dispose();$reader.Dispose()}
    } finally {$stream.Dispose();$page.Dispose()}
}
Write-Output ('Rendered {0} PDF pages to {1}' -f $document.PageCount,[IO.Path]::GetFullPath($OutputDirectory))
