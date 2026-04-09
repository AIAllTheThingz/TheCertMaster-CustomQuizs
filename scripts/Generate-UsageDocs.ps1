param(
    [string]$SourcePath = "D:\Quiz_Application\QuizAPI\Documentation\Application_Usage_Guide.md",
    [string]$OutputDirectory = "D:\Quiz_Application\QuizAPI\Documentation\Output"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Escape-Html {
    param([string]$Text)

    if ($null -eq $Text) { return "" }

    return ($Text `
        -replace '&', '&amp;' `
        -replace '<', '&lt;' `
        -replace '>', '&gt;' `
        -replace '"', '&quot;')
}

function Escape-Xml {
    param([string]$Text)

    if ($null -eq $Text) { return "" }

    return ($Text `
        -replace '&', '&amp;' `
        -replace '<', '&lt;' `
        -replace '>', '&gt;' `
        -replace '"', '&quot;' `
        -replace "'", '&apos;')
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

function Get-MarkdownBlocks {
    param([string[]]$Lines)

    $blocks = New-Object System.Collections.Generic.List[object]
    $paragraphBuffer = New-Object System.Collections.Generic.List[string]
    $listBuffer = $null

    function Flush-Paragraph {
        if ($paragraphBuffer.Count -gt 0) {
            $blocks.Add([pscustomobject]@{
                Type = "Paragraph"
                Text = ($paragraphBuffer -join " ").Trim()
            })
            $paragraphBuffer.Clear()
        }
    }

    function Flush-List {
        if ($null -ne $listBuffer -and $listBuffer.Items.Count -gt 0) {
            $blocks.Add($listBuffer)
            $script:listBuffer = $null
        }
    }

    foreach ($rawLine in $Lines) {
        $line = $rawLine.TrimEnd()
        $trimmed = $line.Trim()

        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            Flush-Paragraph
            Flush-List
            continue
        }

        if ($trimmed -match '^(#{1,6})\s+(.*)$') {
            Flush-Paragraph
            Flush-List
            $blocks.Add([pscustomobject]@{
                Type = "Heading"
                Level = $Matches[1].Length
                Text = $Matches[2].Trim()
            })
            continue
        }

        if ($trimmed -match '^- (.*)$') {
            Flush-Paragraph
            if ($null -eq $listBuffer -or $listBuffer.Kind -ne "Bullet") {
                Flush-List
                $listBuffer = [pscustomobject]@{
                    Type = "List"
                    Kind = "Bullet"
                    Items = New-Object System.Collections.Generic.List[string]
                }
            }
            $listBuffer.Items.Add($Matches[1].Trim())
            continue
        }

        if ($trimmed -match '^\d+\.\s+(.*)$') {
            Flush-Paragraph
            if ($null -eq $listBuffer -or $listBuffer.Kind -ne "Number") {
                Flush-List
                $listBuffer = [pscustomobject]@{
                    Type = "List"
                    Kind = "Number"
                    Items = New-Object System.Collections.Generic.List[string]
                }
            }
            $listBuffer.Items.Add($Matches[1].Trim())
            continue
        }

        Flush-List
        $paragraphBuffer.Add($trimmed)
    }

    Flush-Paragraph
    Flush-List

    return ,$blocks
}

function Convert-BlocksToHtml {
    param([object[]]$Blocks, [string]$Title)

    $body = New-Object System.Text.StringBuilder

    foreach ($block in $Blocks) {
        switch ($block.Type) {
            "Heading" {
                [void]$body.AppendLine("<h$($block.Level)>$(Escape-Html $block.Text)</h$($block.Level)>")
            }
            "Paragraph" {
                $parts = (Escape-Html $block.Text) -split '  '
                $htmlText = ($parts -join '<br />')
                [void]$body.AppendLine("<p>$htmlText</p>")
            }
            "List" {
                $tag = if ($block.Kind -eq "Number") { "ol" } else { "ul" }
                [void]$body.AppendLine("<$tag>")
                foreach ($item in $block.Items) {
                    [void]$body.AppendLine("<li>$(Escape-Html $item)</li>")
                }
                [void]$body.AppendLine("</$tag>")
            }
        }
    }

    return @"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>$(Escape-Html $Title)</title>
  <style>
    @page { margin: 0.7in; }
    body {
      font-family: "Segoe UI", Arial, sans-serif;
      color: #1c1c1c;
      line-height: 1.45;
      font-size: 11pt;
      max-width: 8.2in;
      margin: 0 auto;
      background: white;
    }
    h1 {
      color: #005d7a;
      font-size: 24pt;
      margin: 0 0 18px 0;
      border-bottom: 2px solid #c61f3a;
      padding-bottom: 8px;
    }
    h2 {
      color: #005d7a;
      font-size: 16pt;
      margin-top: 24px;
      margin-bottom: 8px;
    }
    h3 {
      color: #174f61;
      font-size: 13pt;
      margin-top: 18px;
      margin-bottom: 6px;
    }
    p {
      margin: 0 0 10px 0;
    }
    ul, ol {
      margin: 0 0 12px 22px;
      padding: 0;
    }
    li {
      margin: 0 0 5px 0;
    }
  </style>
</head>
<body>
$($body.ToString())
</body>
</html>
"@
}

function Convert-BlocksToDocumentXml {
    param([object[]]$Blocks)

    $sb = New-Object System.Text.StringBuilder

    function Add-Paragraph {
        param(
            [string]$Text,
            [string]$Style = "",
            [bool]$Bold = $false
        )

        $escaped = Escape-Xml $Text
        $styleXml = if ([string]::IsNullOrWhiteSpace($Style)) { "" } else { "<w:pStyle w:val=`"$Style`"/>" }
        $runProps = if ($Bold) { "<w:rPr><w:b/></w:rPr>" } else { "" }
        [void]$sb.AppendLine("<w:p><w:pPr>$styleXml</w:pPr><w:r>$runProps<w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
    }

    foreach ($block in $Blocks) {
        switch ($block.Type) {
            "Heading" {
                $style = switch ($block.Level) {
                    1 { "Title" }
                    2 { "Heading1" }
                    3 { "Heading2" }
                    default { "Heading2" }
                }
                Add-Paragraph -Text $block.Text -Style $style
            }
            "Paragraph" {
                $parts = $block.Text -split '  '
                foreach ($part in $parts) {
                    Add-Paragraph -Text $part
                }
            }
            "List" {
                $index = 1
                foreach ($item in $block.Items) {
                    $prefix = if ($block.Kind -eq "Number") { "$index. " } else { [char]0x2022 + " " }
                    Add-Paragraph -Text ($prefix + $item)
                    $index++
                }
            }
        }
    }

    return @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:wpc="http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas"
 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
 xmlns:o="urn:schemas-microsoft-com:office:office"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"
 xmlns:v="urn:schemas-microsoft-com:vml"
 xmlns:wp14="http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing"
 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
 xmlns:w10="urn:schemas-microsoft-com:office:word"
 xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
 xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml"
 xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml"
 xmlns:wpg="http://schemas.microsoft.com/office/word/2010/wordprocessingGroup"
 xmlns:wpi="http://schemas.microsoft.com/office/word/2010/wordprocessingInk"
 xmlns:wne="http://schemas.microsoft.com/office/2006/wordml"
 xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
 mc:Ignorable="w14 w15 wp14">
  <w:body>
$($sb.ToString())
    <w:sectPr>
      <w:pgSz w:w="12240" w:h="15840"/>
      <w:pgMar w:top="1008" w:right="1008" w:bottom="1008" w:left="1008" w:header="708" w:footer="708" w:gutter="0"/>
    </w:sectPr>
  </w:body>
</w:document>
"@
}

function New-DocxFile {
    param(
        [string]$Path,
        [string]$DocumentXml
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("quizapi-docx-" + [guid]::NewGuid().ToString("N"))
    $wordDir = Join-Path $tempRoot "word"
    $relsDir = Join-Path $tempRoot "_rels"
    $wordRelsDir = Join-Path $wordDir "_rels"

    New-Item -ItemType Directory -Path $wordDir -Force | Out-Null
    New-Item -ItemType Directory -Path $relsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $wordRelsDir -Force | Out-Null

    Write-Utf8File -Path (Join-Path $tempRoot "[Content_Types].xml") -Content @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>
"@

    Write-Utf8File -Path (Join-Path $relsDir ".rels") -Content @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
"@

    Write-Utf8File -Path (Join-Path $wordDir "document.xml") -Content $DocumentXml

    Write-Utf8File -Path (Join-Path $wordDir "styles.xml") -Content @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
    <w:qFormat/>
    <w:rPr>
      <w:rFonts w:ascii="Segoe UI" w:hAnsi="Segoe UI"/>
      <w:sz w:val="22"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Title">
    <w:name w:val="Title"/>
    <w:basedOn w:val="Normal"/>
    <w:qFormat/>
    <w:rPr>
      <w:b/>
      <w:color w:val="005D7A"/>
      <w:sz w:val="34"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="Heading 1"/>
    <w:basedOn w:val="Normal"/>
    <w:qFormat/>
    <w:rPr>
      <w:b/>
      <w:color w:val="005D7A"/>
      <w:sz w:val="28"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="Heading 2"/>
    <w:basedOn w:val="Normal"/>
    <w:qFormat/>
    <w:rPr>
      <w:b/>
      <w:color w:val="174F61"/>
      <w:sz w:val="24"/>
    </w:rPr>
  </w:style>
</w:styles>
"@

    Write-Utf8File -Path (Join-Path $wordRelsDir "document.xml.rels") -Content @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships" />
"@

    if (Test-Path $Path) {
        Remove-Item $Path -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempRoot, $Path)
    Remove-Item $tempRoot -Recurse -Force
}

if (-not (Test-Path $SourcePath)) {
    throw "Source file not found: $SourcePath"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$title = "QuizAPI Application Usage Guide"
$markdownLines = Get-Content -Path $SourcePath
$blocks = Get-MarkdownBlocks -Lines $markdownLines

$htmlPath = Join-Path $OutputDirectory "QuizAPI_Application_Usage_Guide.html"
$docxPath = Join-Path $OutputDirectory "QuizAPI_Application_Usage_Guide.docx"
$pdfPath = Join-Path $OutputDirectory "QuizAPI_Application_Usage_Guide.pdf"

$html = Convert-BlocksToHtml -Blocks $blocks -Title $title
Write-Utf8File -Path $htmlPath -Content $html

$documentXml = Convert-BlocksToDocumentXml -Blocks $blocks
New-DocxFile -Path $docxPath -DocumentXml $documentXml

$edgePath = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$chromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$browserPath = if (Test-Path $edgePath) { $edgePath } elseif (Test-Path $chromePath) { $chromePath } else { $null }

if ($null -ne $browserPath) {
    $htmlUri = [System.Uri]::new($htmlPath).AbsoluteUri
    $arguments = @(
        "--headless=new"
        "--disable-gpu"
        "--print-to-pdf=$pdfPath"
        "--no-first-run"
        "--no-default-browser-check"
        $htmlUri
    )

    $process = Start-Process -FilePath $browserPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0 -or -not (Test-Path $pdfPath)) {
        throw "PDF generation failed using $browserPath"
    }
} else {
    throw "No supported local browser found for PDF generation."
}

Write-Host "Created:"
Write-Host " - $htmlPath"
Write-Host " - $docxPath"
Write-Host " - $pdfPath"
