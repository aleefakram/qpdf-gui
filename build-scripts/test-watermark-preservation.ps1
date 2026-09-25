param(
    [Parameter(Mandatory)][string]$Qpdf,
    # Check existing output PDFs (wildcards allowed) instead of generating fixtures.
    [string[]]$Verify = @()
)
$ErrorActionPreference = 'Stop'
$Qpdf = (Resolve-Path -LiteralPath $Qpdf).Path
function Invoke-Qpdf([string[]]$Arguments) {
    $result = & $Qpdf @Arguments
    if ($LASTEXITCODE -ne 0) { throw "qpdf failed: $LASTEXITCODE ($Arguments)" }
    return $result
}
function Write-Pdf([string]$Path, [string]$PageAttributes, [string]$PagesAttributes) {
    $content = "1 0 0 rg 20 20 40 40 re f`n0 0 1 rg 210 310 30 30 re f`n"
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        ('<< /Type /Pages /Kids [3 0 R] /Count 1 ' + $PagesAttributes + ' >>'),
        ('<< /Type /Page /Parent 2 0 R /Resources << >> /Contents 4 0 R ' + $PageAttributes + ' >>'),
        ('<< /Length ' + $content.Length + " >>`nstream`n" + $content + 'endstream'))
    $data = "%PDF-1.7`n"
    $offsets = @()
    for ($i = 0; $i -lt $objects.Count; $i++) {
        $offsets += $data.Length
        $data += "$($i + 1) 0 obj`n$($objects[$i])`nendobj`n"
    }
    $xref = $data.Length
    $data += "xref`n0 5`n0000000000 65535 f `n"
    foreach ($offset in $offsets) { $data += $offset.ToString('D10') + " 00000 n `n" }
    $data += "trailer`n<< /Root 1 0 R /Size 5 >>`nstartxref`n$xref`n%%EOF`n"
    [IO.File]::WriteAllText($Path, $data, [Text.Encoding]::ASCII)
}
function Transform($m, $p) {
    return @(($m[0]*$p[0] + $m[2]*$p[1] + $m[4]),
             ($m[1]*$p[0] + $m[3]*$p[1] + $m[5]))
}
function Resolve-Value($objects, $value) {
    if ($value -is [string] -and $value -match '^\d+ \d+ R$') { return $objects['obj:' + $value].value }
    return $value
}
# Walks /Parent for inheritable attributes such as /MediaBox.
function Get-PageAttribute($objects, $node, [string]$Key) {
    for ($depth = 0; $depth -lt 64; $depth++) {
        if ($node.ContainsKey($Key)) { return Resolve-Value $objects $node[$Key] }
        if (-not $node.ContainsKey('/Parent')) { return $null }
        $node = Resolve-Value $objects $node['/Parent']
    }
    throw "Page tree too deep or cyclic while resolving $Key"
}
# Throws unless every page's original Form (/Fx0) keeps content in place and clips to MediaBox.
function Test-Preserved([string]$Output) {
    Invoke-Qpdf @('--check', $Output) | Out-Null
    $doc = (Invoke-Qpdf @('--json=2', '--json-stream-data=inline', $Output) | Out-String) |
        ConvertFrom-Json -AsHashtable
    $objects = $doc.qpdf[1]
    $checked = 0
    foreach ($page in $doc.pages) {
        $value = $objects['obj:' + $page.object].value
        $resources = Resolve-Value $objects $value['/Resources']
        $xobjects = if ($null -ne $resources) { Resolve-Value $objects $resources['/XObject'] }
        if ($null -eq $xobjects -or -not $xobjects.ContainsKey('/Fx0')) { continue } # page not overlaid
        $original = $objects['obj:' + $xobjects['/Fx0']].stream.dict
        $text = ($page.contents | ForEach-Object {
            [Text.Encoding]::ASCII.GetString([Convert]::FromBase64String($objects['obj:' + $_].stream.data))
        }) -join "`n"
        # Pinned qpdf regression, not a general PDF content parser.
        $match = [regex]::Match($text, '(?m)^([0-9.eE+\- ]+) cm\s*/Fx0 Do')
        if (-not $match.Success) { throw "page $($page.object): original Form invocation shape changed; inspect output" }
        $cm = @($match.Groups[1].Value.Trim() -split '\s+' | ForEach-Object {
            [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) })
        if ($cm.Count -ne 6) { throw "page $($page.object): invalid invocation matrix" }
        $fm = $original['/Matrix']
        if ($null -eq $fm) { $fm = @(1,0,0,1,0,0) }
        foreach ($point in @(@(0,0), @(600,0), @(0,800), @(210,310))) {
            $actual = Transform $cm (Transform $fm $point)
            # qpdf writes matrices rounded to 5 decimals (1/1.5 -> 0.66667), so exact inverses leave
            # ~0.004 pt error at 800 pt. Allow for that; real shifts from this bug are tens of points.
            $tolerance = 0.01 + 2e-5 * ([Math]::Abs($point[0]) + [Math]::Abs($point[1]))
            if ([Math]::Abs($actual[0]-$point[0]) -gt $tolerance -or
                [Math]::Abs($actual[1]-$point[1]) -gt $tolerance) { throw "page $($page.object): original content moved" }
        }
        $mediaBox = Get-PageAttribute $objects $value '/MediaBox'
        if ($null -eq $mediaBox -or ($original['/BBox'] -join ',') -ne ($mediaBox -join ',')) {
            throw "page $($page.object): original Form is not clipped to MediaBox"
        }
        $checked++
    }
    if ($checked -eq 0) { throw 'no overlaid page found' }
}

$failures = @()
if ($Verify.Count) {
    foreach ($output in @($Verify | ForEach-Object { (Resolve-Path -Path $_).Path })) {
        try { Test-Preserved $output; Write-Host "PASS $output" } catch { $failures += "${output}: $_" }
    }
} else {
    $work = Join-Path $env:TEMP ('qpdf-preservation-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    Write-Host "Fixtures: $work"
    # name = (page attributes, /Pages attributes)
    $cases = [ordered]@{
        offcenter = @('/MediaBox [0 0 600 800] /CropBox [0 0 300 400]', '')
        centered  = @('/MediaBox [0 0 600 800] /CropBox [150 200 450 600]', '')
        trim      = @('/MediaBox [0 0 600 800] /TrimBox [100 100 500 700]', '')
        rotate90  = @('/MediaBox [0 0 600 800] /CropBox [0 0 300 400] /Rotate 90', '')
        rotate180 = @('/MediaBox [0 0 600 800] /CropBox [0 0 300 400] /Rotate 180', '')
        rotate270 = @('/MediaBox [0 0 600 800] /CropBox [0 0 300 400] /Rotate 270', '')
        units     = @('/MediaBox [0 0 600 800] /CropBox [0 0 300 400] /Rotate 90 /UserUnit 2', '')
        origin    = @('/MediaBox [-100 -200 500 600] /CropBox [-50 -150 250 250] /Rotate 90', '')
        inherited = @('', '/MediaBox [0 0 600 800] /CropBox [0 0 300 400] /Rotate 90')
        outside   = @('/MediaBox [0 0 600 800] /CropBox [-100 0 400 900]', '')
    }
    $art = Join-Path $work 'art.pdf'
    Write-Pdf $art '/MediaBox [0 0 600 800]' ''
    foreach ($name in $cases.Keys) {
        $source = Join-Path $work "overlay-preserve-$name.pdf"
        Write-Pdf $source $cases[$name][0] $cases[$name][1]
        Invoke-Qpdf @('--check', $source) | Out-Null
        foreach ($mode in @('overlay', 'underlay')) {
            try {
                $output = Join-Path $work "overlay-preserve-$name-$mode-out.pdf"
                Invoke-Qpdf @('--static-id', $source, "--$mode", $art, '--', $output) | Out-Null
                Test-Preserved $output
                Write-Host "PASS $name $mode"
            } catch { $failures += "$name ${mode}: $_" }
        }
    }
}
$failures | ForEach-Object { Write-Host "FAIL $_" }
if ($failures.Count) { throw "$($failures.Count) preservation check(s) failed" }
