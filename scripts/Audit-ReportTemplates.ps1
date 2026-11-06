param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'MachineKarte-ReportAudit/Inventory'))
# Offline inventory only. Never loads application settings or contacts SQL/shares.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Read-Part($zip, [string]$name) {
    $entry = $zip.GetEntry($name)
    if ($null -eq $entry) { return $null }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Cell-Position([string]$name) {
    if ($name -notmatch '^\$?([A-Z]+)\$?(\d+)$') { throw "Invalid cell: $name" }
    $column = 0
    foreach ($character in $Matches[1].ToCharArray()) { $column = $column * 26 + [int]$character - 64 }
    return @([int]$Matches[2], $column)
}
function Cell-Name([int]$row, [int]$column) {
    $letters = ''
    while ($column -gt 0) { $column--; $letters = [char](65 + $column % 26) + $letters; $column = [math]::Floor($column / 26) }
    return $letters + $row
}
$catalog = New-Object Collections.Generic.List[object]
$regions = New-Object Collections.Generic.List[object]
$pictures = New-Object Collections.Generic.List[object]
$bindings = New-Object Collections.Generic.List[object]
$sheetsByTemplate = @{}
$activeTemplates = @{}
$pmViews=@{}; $pmFiles=@{}; $pmFolders=@{}
$evidenceViews=@{}
$evidenceDefinitionSource=[IO.File]::ReadAllText((Join-Path $repo 'Models/EvidenceMakingModels.cs'))
foreach($match in [regex]::Matches($evidenceDefinitionSource,'\["(\w+)"\] = new\("[^"]+", "([^"]+)", "([^"]+)"\)')) {
    $evidenceViews[$match.Groups[1].Value]='gridEvidenceMaking_Ver3 + '+$match.Groups[2].Value+' + '+$match.Groups[3].Value
}
$definitionSource=[IO.File]::ReadAllText((Join-Path $repo 'Repositories/PmReportRepository.cs'))
foreach($match in [regex]::Matches($definitionSource,'\["(\w+)"\] = new\("\w+", "([^"]+)", "[^"]+", "([^"]+)", (?:true|false)(?:, "([^"]+)")?\)')) {
    $process=$match.Groups[1].Value; $pmFiles[$process]=$match.Groups[2].Value; $pmViews[$process]=$match.Groups[3].Value
    $pmFolders[$process]=if($match.Groups[4].Success){$match.Groups[4].Value}else{'PmReport'}
}
if($pmFiles.Count -ne 11){throw 'PM definition inventory is incomplete.'}
foreach ($process in $pmFiles.Keys) {
    $activeTemplates['Templates/'+$pmFolders[$process]+'/' + $pmFiles[$process]] = 'Pm' + $process
    $evidenceFile = if ($process -eq 'SING') {'EvidenceMaking_Sing.xlsx'} else {'EvidenceMaking_' + $process + '.xlsx'}
    $activeTemplates['Templates/EvidenceMaking/' + $evidenceFile] = 'PopulateEvidence:' + $process
}
$activeTemplates['Templates/BmReport/TETemplate.xlsx'] = 'TE'
$activeTemplates['Templates/BmReport/EDS-CSPTemplate.xlsx'] = 'TE_EDS'
$activeTemplates['Templates/EvidenceMaking/CWC.xlsx'] = 'PopulateTeEvidence'
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'Templates') -Recurse -Filter '*.xlsx' | Sort-Object FullName)) {
    $template = $file.FullName.Substring($repo.Length + 1).Replace('\','/')
    try {$zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)} catch [IO.InvalidDataException] {
        $catalog.Add([pscustomobject]@{Template=$template;Profile=$activeTemplates[$template];Status='Not an OOXML ZIP workbook; inspect using Excel/GemBox';SheetIndex='';Sheet='';Active='';Visibility='';Dimension='';MergedRegions='';PopulatedCells='';SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
        $sheetsByTemplate[$template]=@()
        continue
    }
    try {
        $book = Read-Part $zip 'xl/workbook.xml'
        $relationships = Read-Part $zip 'xl/_rels/workbook.xml.rels'
        $strings = Read-Part $zip 'xl/sharedStrings.xml'
        $shared = if($strings) {@($strings.SelectNodes('//*[local-name()="si"]') | ForEach-Object { ($_.SelectNodes('.//*[local-name()="t"]') | ForEach-Object {$_.InnerText}) -join '' })} else {@()}
        $active = $book.SelectSingleNode('//*[local-name()="workbookView"]')
        $activeIndex = if ($active -and $active.HasAttribute('activeTab')) {[int]$active.activeTab} else {0}
        $sheetIndex = 0
        $templateSheets = New-Object Collections.Generic.List[object]
        $drawingSheets=@{}
        foreach ($sheet in $book.SelectNodes('//*[local-name()="sheets"]/*')) {
            $id = $sheet.GetAttribute('id','http://schemas.openxmlformats.org/officeDocument/2006/relationships')
            $target = @($relationships.Relationships.Relationship | Where-Object {$_.Id -eq $id})[0].Target
            $part = if ($target.StartsWith('/')) {$target.TrimStart('/')} else {'xl/' + $target}
            $xml = Read-Part $zip $part
            $sheetRels=Read-Part $zip (([IO.Path]::GetDirectoryName($part).Replace('\','/'))+'/_rels/'+[IO.Path]::GetFileName($part)+'.rels')
            if($sheetRels) {
                foreach($relation in $sheetRels.Relationships.Relationship) {
                    if($relation.Type -match '/drawing$') {$drawingSheets['xl/'+$relation.Target.Replace('../','').TrimStart('/')]=$sheet.name}
                }
            }
            $cells = @{}
            foreach ($cell in $xml.SelectNodes('//*[local-name()="sheetData"]//*[local-name()="c"]')) {
                $value = $cell.SelectSingleNode('*[local-name()="v"]')
                $formula = $cell.SelectSingleNode('*[local-name()="f"]')
                $text = if ($value) {$value.InnerText} else {''}
                if ($cell.t -eq 's' -and $value) {$text = $shared[[int]$text]}
                if ($cell.t -eq 'inlineStr') {$text = ($cell.SelectNodes('.//*[local-name()="t"]') | ForEach-Object {$_.InnerText}) -join ''}
                if ($text -ne '' -or $formula) {
                    $cells[$cell.r] = $text
                    $regions.Add([pscustomobject]@{Template=$template;Sheet=$sheet.name;Kind='Cell';Address=$cell.r;Text=$text;Formula=$(if($formula){$formula.InnerText}else{''});Type=$cell.t;Style=$cell.s})
                }
            }
            $merges = @($xml.SelectNodes('//*[local-name()="mergeCell"]') | ForEach-Object {$_.ref})
            foreach ($merge in $merges) {
                $regions.Add([pscustomobject]@{Template=$template;Sheet=$sheet.name;Kind='Merged region';Address=$merge;Text=$cells[($merge -split ':')[0]];Formula='';Type='';Style=''})
            }
            $bounds = @($merges | ForEach-Object {
                $ends=$_ -split ':'; $first=Cell-Position $ends[0]; $last=Cell-Position $ends[-1]
                @{Address=$_;FirstRow=$first[0];LastRow=$last[0];FirstColumn=$first[1];LastColumn=$last[1];Text=$cells[$ends[0]]}
            })
            $templateSheets.Add(@{Name=$sheet.name;Cells=$cells;Merges=$merges;Bounds=$bounds;Active=($sheetIndex -eq $activeIndex)})
            $dimension = $xml.SelectSingleNode('//*[local-name()="dimension"]')
            $catalog.Add([pscustomobject]@{Template=$template;Profile=$activeTemplates[$template];Status=$(if($activeTemplates.ContainsKey($template)){'Active'}else{'Unreferenced by current services'});SheetIndex=$sheetIndex;Sheet=$sheet.name;Active=($sheetIndex -eq $activeIndex);Visibility=$sheet.state;Dimension=$dimension.ref;MergedRegions=$merges.Count;PopulatedCells=$cells.Count;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
            $sheetIndex++
        }
        foreach ($name in $book.SelectNodes('//*[local-name()="definedName"]')) {
            $regions.Add([pscustomobject]@{Template=$template;Sheet=$name.localSheetId;Kind='Defined name';Address=$name.name;Text=$name.InnerText;Formula='';Type='';Style=''})
        }
        foreach ($entry in $zip.Entries | Where-Object {$_.FullName -match '^xl/drawings/.*(xml|vml)$|^xl/ctrlProps/'}) {
            $drawing = Read-Part $zip $entry.FullName
            foreach ($anchor in $drawing.SelectNodes('//*[local-name()="twoCellAnchor" or local-name()="oneCellAnchor" or local-name()="absoluteAnchor"]')) {
                $from = $anchor.SelectSingleNode('*[local-name()="from"]')
                $to = $anchor.SelectSingleNode('*[local-name()="to"]')
                $label = ($anchor.SelectNodes('.//*[local-name()="t"]') | ForEach-Object {$_.InnerText}) -join ' '
                $properties = $anchor.SelectSingleNode('.//*[local-name()="cNvPr"]')
                $pictures.Add([pscustomobject]@{Template=$template;Sheet=$drawingSheets[$entry.FullName];Part=$entry.FullName;Kind=$anchor.LocalName;Name=$properties.name;From=$(if($from){Cell-Name ([int]$from.row+1) ([int]$from.col+1)}else{''});To=$(if($to){Cell-Name ([int]$to.row+1) ([int]$to.col+1)}else{''});Text=$label;Xml=$anchor.OuterXml})
            }
        }
        $sheetsByTemplate[$template] = $templateSheets
    } finally {$zip.Dispose()}
}
$catalog | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'Templates.csv')
$regions | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'Regions.csv')
$pictures | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'Drawings.csv')
$worksheetCount=@($catalog | Where-Object {$_.Sheet}).Count
Write-Output ('Inventory complete: {0} template files, {1} OOXML worksheets, {2} non-OOXML format records.' -f $sheetsByTemplate.Count,$worksheetCount,($catalog.Count-$worksheetCount))
$source = [IO.File]::ReadAllLines((Join-Path $repo 'Reporting/LegacyReportMappings.vb'))
$profile = ''; $sheetAlias = @{}; $conditions = New-Object Collections.Generic.List[string]; $variables = @{}
function Find-Fields([string]$expression, $seen) {
    foreach ($match in [regex]::Matches($expression,'\b(DR\w*|emDR\w*)\("([^\"]+)"\)')) {$match.Groups[1].Value + '.' + $match.Groups[2].Value}
    foreach ($match in [regex]::Matches($expression,'EvidenceImageGroups\.\w+\.\w+')) {$match.Value}
    foreach ($token in [regex]::Matches(([regex]::Replace($expression,'"[^"]*"','')),'\b[A-Za-z_]\w*\b')) {
        $name = $token.Value
        if ($variables.ContainsKey($name) -and $seen.Add($name)) { foreach ($value in $variables[$name]) { Find-Fields $value $seen } }
    }
}
for ($lineIndex=0; $lineIndex -lt $source.Length; $lineIndex++) {
    $line = $source[$lineIndex].Trim()
    if ($line -match '^(?:Private|Public) Sub (Pm\w+|TE_EDS|TE|PopulateEvidence|PopulateTeEvidence)\(') {
        $profile=$Matches[1]; $sheetAlias=@{ws='Active';ws_2='1';ws_3='2'}; $conditions.Clear(); $variables=@{}
        if($profile -eq 'PopulateEvidence') {
            $groups=@('ProblemPoint','ActionTaken','PartsReplaced','DisAssembly','QualityConfirm')
            for($groupIndex=0;$groupIndex -lt $groups.Count;$groupIndex++) {
                $suffix=if($groupIndex -eq 0){''}else{($groupIndex+1).ToString()}
                $variables['imagesData'+$suffix]=@('EvidenceImageGroups.'+$groups[$groupIndex]+'.ImageFile')
                $variables['remarksData'+$suffix]=@('EvidenceImageGroups.'+$groups[$groupIndex]+'.Description')
            }
        }
        continue
    }
    if ($line -eq 'End Sub') {$profile=''; continue}
    if (!$profile) {continue}
    if ($line -match '^(?:Dim )?(ws(?:_\d)?)\s*=.*wb.Worksheets\((\d)\)') {$sheetAlias[$Matches[1]]=$Matches[2]}
    if ($line -match '^(?:Dim )?(ws(?:_\d)?)\s*=.*(?:wb.Worksheets\("|sheet.Name = ")([^\"]+)"') {$sheetAlias[$Matches[1]]=$Matches[2]}
    if ($line -match '^If (.*) Then(?:\s*\x27.*)?$') {$conditions.Add($Matches[1])}
    elseif ($line -match '^ElseIf (.*) Then(?:\s*\x27.*)?$') {if($conditions.Count){$conditions[$conditions.Count-1]=$Matches[1]}}
    elseif ($line -eq 'Else') {if($conditions.Count){$conditions[$conditions.Count-1]='NOT ('+$conditions[$conditions.Count-1]+')'}}
    elseif ($line -eq 'End If') {if($conditions.Count){$conditions.RemoveAt($conditions.Count-1)}}
    if ($line -match '^(?:Dim )?([A-Za-z_]\w*)(?:\([^)]*\))?(?: As [\w()]+)?\s*=\s*(.+)$') {
        $variable=$Matches[1]; if (!$variables.ContainsKey($variable)) {$variables[$variable]=@()}; $variables[$variable]+=$Matches[2]
    }
    $kind=''; $cell=''; $alias=''; $expression=''
    if($line -match 'AddEvidencePicture.*arrTeleko\(intTeleko\)') {
        foreach($telekoCell in @('H70','J70','M70','O70')) {
            $bindings.Add([pscustomobject]@{Template='Templates/EvidenceMaking/CWC.xlsx';Profile=$profile;Sheet='FORMAT';Cell=$telekoCell;MergedRegion='';TemplateText='';Kind='Image';SourceFields='emDR_DataTeleko.TelekoEvidence';Dataset='TEBMEvidenceMaking: TOP 4 TELEKO EVIDENCE with nonempty TelekoEvidence';Expression=$line;Conditions='Reader order, four Teleko slots';GeneratedLine=$lineIndex+1;Status=''})
        }
        continue
    }
    if ($line -match '(ws(?:_\d)?)\.Cells\("([A-Z]+\d+)"\)(\.[\w.]+)\s*=\s*(.*)') {
        $alias=$Matches[1]; $cell=$Matches[2]; $kind=$Matches[3].TrimStart('.'); $expression=$Matches[4]
    } elseif ($line -match '(ws(?:_\d)?)\.Cells\((\d+),\s*(\d+)\)(\.[\w.]+)\s*=\s*(.*)') {
        $alias=$Matches[1]; $cell=Cell-Name ([int]$Matches[2]+1) ([int]$Matches[3]+1); $kind=$Matches[4].TrimStart('.'); $expression=$Matches[5]
    } elseif ($line -match 'Add(?:Evidence)?Picture\((ws(?:_\d)?),.*?, "([A-Z]+\d+)", ([\d.]+), ([\d.]+), LengthUnit\.(\w+)\)') {
        $alias=$Matches[1]; $cell=$Matches[2]; $kind='Image'; $expression=$line
    } elseif($line -match '(ws(?:_\d)?)\.(?:NamedRanges.SetPrintArea\()?Cells.GetSubrange\("([A-Z]+\d+)", "([A-Z]+\d+)"\)(.*)') {
        $alias=$Matches[1];$cell=$Matches[2];$kind='Range';$expression=$line
    }
    if (!$kind) {continue}
    $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $fields=@(Find-Fields ($expression+' '+($conditions -join ' ')) $seen | Sort-Object -Unique) -join '; '
    foreach ($template in @($activeTemplates.Keys | Sort-Object)) {
        if (($activeTemplates[$template] -split ':')[0] -ne $profile) {continue}
        if ($profile -eq 'PopulateEvidence') {
            $isSing=$activeTemplates[$template].EndsWith(':SING')
            if (($conditions -contains 'ProcessGroup = "SING"' -and !$isSing) -or ($conditions -contains 'NOT (ProcessGroup = "SING")' -and $isSing)) {continue}
        }
        $templateSheets=$sheetsByTemplate[$template]
        $sheetName=$sheetAlias[$alias]
        $index=if($sheetName -eq 'Active') {(@($templateSheets | Where-Object {$_.Active}))[0]} elseif ($sheetName -match '^\d+$' -and [int]$sheetName -lt $templateSheets.Count) {$templateSheets[[int]$sheetName]} else {(@($templateSheets | Where-Object {$_.Name -eq $sheetName}))[0]}
        $merge=''; $label=''; $status=''
        if ($null -eq $index) {$status='Required worksheet absent'} else {
            $label=$index.Cells[$cell]; $position=Cell-Position $cell
            foreach ($region in $index.Bounds) {
                if($position[0] -ge $region.FirstRow -and $position[0] -le $region.LastRow -and $position[1] -ge $region.FirstColumn -and $position[1] -le $region.LastColumn) {$merge=$region.Address; $label=$region.Text; break}
            }
        }
        $dataset=if($profile.StartsWith('Pm')) {$pmViews[$profile.Substring(2)]} elseif($profile -in @('TE','TE_EDS')) {'ViewTEData_Test + lookups + VRNumber'} elseif($profile -eq 'PopulateEvidence') {$evidenceViews[($activeTemplates[$template] -split ':')[1]]} else {'ViewTEData + TEBMEvidenceMaking + TEAssign'}
        $bindings.Add([pscustomobject]@{Template=$template;Profile=$activeTemplates[$template];Sheet=$(if($index){$index.Name}else{$sheetAlias[$alias]});Cell=$cell;MergedRegion=$merge;TemplateText=$label;Kind=$kind;SourceFields=$fields;Dataset=$dataset;Expression=$expression;Conditions=($conditions -join ' AND ');GeneratedLine=$lineIndex+1;Status=$status})
    }
}
$bindings | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'Bindings.csv')
$conflicts=@($bindings | Where-Object {$_.Kind -eq 'Value' -and !$_.Conditions -and $_.Expression -match '^DR\("'} |
    Group-Object { $_.Template+'|'+$_.Sheet+'|'+$(if($_.MergedRegion){$_.MergedRegion}else{$_.Cell}) } |
    Where-Object {@($_.Group.Expression | Sort-Object -Unique).Count -gt 1} |
    ForEach-Object {[pscustomobject]@{Destination=$_.Name;Sources=($_.Group.SourceFields -join '; ');Expressions=($_.Group.Expression -join '; ')}})
if($conflicts.Count){$conflicts | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'BindingConflicts.csv')}
else {'"Destination","Sources","Expressions"' | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'BindingConflicts.csv')}
$candidates=New-Object Collections.Generic.List[object]
foreach($process in ($pmFiles.Keys | Sort-Object)) {
    $writes=@($bindings | Where-Object {$_.Profile -eq ('Pm'+$process) -and $_.Kind -eq 'Value' -and $_.SourceFields -match '^DR\.'})
    foreach($template in ($sheetsByTemplate.Keys | Where-Object {[IO.Path]::GetFileName($_) -match ('^'+$process+'Template.*\.xlsx$') -or $_ -eq ('Templates/PmReport/'+$pmFiles[$process])} | Sort-Object)) {
        $sheet=@($sheetsByTemplate[$template] | Where-Object {$_.Active})[0]
        if(!$sheet){continue}
        $headers=New-Object Collections.Generic.List[string]
        $destinations=@{}
        foreach($write in $writes) {
            $cell=$write.Cell; $position=Cell-Position $cell; $label=$sheet.Cells[$cell]; $destination=$cell
            foreach($region in $sheet.Bounds) {
                if($position[0] -ge $region.FirstRow -and $position[0] -le $region.LastRow -and $position[1] -ge $region.FirstColumn -and $position[1] -le $region.LastColumn) {$destination=$region.Address; $label=$region.Text; break}
            }
            if($label -match '[A-Za-z]{3}' -and $label -notmatch '^[Yy]es\s*/\s*[Nn]o$') {$headers.Add($cell+'='+$label)}
            if(!$destinations.ContainsKey($destination)){$destinations[$destination]=@()}
            $destinations[$destination]+=$write.SourceFields
        }
        $collisions=@($destinations.Keys | Where-Object {@($destinations[$_] | Sort-Object -Unique).Count -gt 1})
        $candidates.Add([pscustomobject]@{Process=$process;Template=$template;Sheet=$sheet.Name;HeaderWrites=@($headers | Sort-Object -Unique).Count;CollidingRegions=$collisions.Count;Examples=(@($headers | Sort-Object -Unique | Select-Object -First 8) -join '; ')})
    }
}
$candidates | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'LayoutCandidates.csv')
Write-Output ('Inventoried {0} templates, {1} OOXML worksheets, {2} regions/cells/names, {3} drawings and {4} binding statements.' -f $sheetsByTemplate.Count,$worksheetCount,$regions.Count,$pictures.Count,$bindings.Count)
Write-Output ('Audit CSVs: ' + [IO.Path]::GetFullPath($OutputDirectory))
