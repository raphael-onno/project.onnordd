<#
.SYNOPSIS
    Builds the registry export the application imports, from a dump of the EuropaCorp registry database.

.DESCRIPTION
    The dump folder holds one JSON file per document, by collection:
      <Dump>\titles\<titleId>.json         { name, folder, chunks, ... }
      <Dump>\catalog\<titleId>--<n>.json   { title, els: [ { i, c, p, g, f, s, tp, r, m: {...} } ] }
      <Dump>\decisions\<titleId>.json      { els: { <elementId>: { v } } }   (optional)
    Output: UTF-8 CSV, semicolon-separated, with the columns RegistryCsvReader expects.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\registry-to-csv.ps1 -Dump C:\temp\registry -Output C:\temp\registre.csv
#>
param(
    [Parameter(Mandatory)] [string] $Dump,
    [Parameter(Mandatory)] [string] $Output
)

$ErrorActionPreference = 'Stop'

# Registry categories stored as one folder (image sequences, DCP, documents, excluded).
$folderCategories = @(3, 6, 7, 8, 9)

function Read-Json([string] $path) {
    [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json
}

function Csv([object] $value) {
    $text = if ($null -eq $value) { '' } else { [string]$value }
    if ($text -match '[;"\r\n]') { '"' + $text.Replace('"', '""') + '"' } else { $text }
}

$titles = @{}
Get-ChildItem (Join-Path $Dump 'titles') -Filter *.json | ForEach-Object {
    $titles[$_.BaseName] = Read-Json $_.FullName
}

$decisions = @{}
$decisionDir = Join-Path $Dump 'decisions'
if (Test-Path $decisionDir) {
    Get-ChildItem $decisionDir -Filter *.json | ForEach-Object {
        $doc = Read-Json $_.FullName
        if ($doc.els) {
            foreach ($p in $doc.els.PSObject.Properties) { $decisions[$p.Name] = $p.Value.v }
        }
    }
}

$header = 'elementId;titleKey;titleName;relativePath;category;sizeGb;isFolder;fileCount;source;tapes;recommendation;decision;type;codec;language;ratio;fps;definition;audio;version;date'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add($header)

Get-ChildItem (Join-Path $Dump 'catalog') -Filter *.json | Sort-Object Name | ForEach-Object {
    $doc = Read-Json $_.FullName
    $titleKey = $doc.title
    $titleName = if ($titles[$titleKey]) { $titles[$titleKey].name } else { $titleKey }
    foreach ($e in $doc.els) {
        $m = $e.m
        $isFolder = if ($folderCategories -contains [int]$e.c) { 1 } else { 0 }
        $fields = @(
            $e.i, $titleKey, $titleName, $e.p, $e.c,
            ([double]$e.g).ToString([Globalization.CultureInfo]::InvariantCulture),
            $isFolder, $e.f, $e.s, (($e.tp | Where-Object { $_ }) -join '|'), $e.r, $decisions[$e.i],
            $m.t, $m.c, $m.l, $m.o, $m.f, $m.d, $m.a, $m.x, $m.dt
        )
        $lines.Add((($fields | ForEach-Object { Csv $_ }) -join ';'))
    }
}

[IO.File]::WriteAllLines($Output, $lines, (New-Object Text.UTF8Encoding $false))
"$($lines.Count - 1) elements written to $Output"
