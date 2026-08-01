param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$TemplatePath = "$env:USERPROFILE\Downloads\softwarex-osp-template.docx"
)

$ErrorActionPreference = "Stop"

$sourcePath = Join-Path $RepositoryRoot "docs\manuscript\SOFTWAREX_MANUSCRIPT_DRAFT.md"
$outputDirectory = Join-Path $RepositoryRoot "docs\manuscript\word"
$outputPath = Join-Path $outputDirectory "VLab-Orchestrator_SoftwareX_Manuscript_Review.docx"

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Manuscript source not found: $sourcePath"
}
if (-not (Test-Path -LiteralPath $TemplatePath)) {
    throw "SoftwareX template not found: $TemplatePath"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Copy-Item -LiteralPath $TemplatePath -Destination $outputPath -Force

function Convert-InlineMarkdown {
    param([string]$Text)

    $value = $Text
    $value = $value -replace '!\[[^\]]*\]\([^)]+\)', ''
    $value = $value -replace '\[([^\]]+)\]\(([^)]+)\)', '$1 ($2)'
    $value = $value -replace '\*\*([^*]+)\*\*', '$1'
    $value = $value -replace '`([^`]+)`', '$1'
    $value = $value -replace '\^', ''
    return $value.Trim()
}

function Add-Paragraph {
    param(
        $Document,
        [string]$Text,
        [string]$Style = "Normal",
        [int]$Alignment = 0,
        [switch]$Italic,
        [switch]$Bold
    )

    $range = $Document.Range($Document.Content.End - 1, $Document.Content.End - 1)
    $paragraph = $Document.Paragraphs.Add($range)
    $paragraph.Range.Text = $Text
    try { $paragraph.Range.Style = $Style } catch { $paragraph.Range.Style = "Normal" }
    $paragraph.Alignment = $Alignment
    $paragraph.Range.Font.Italic = [int]$Italic.IsPresent
    $paragraph.Range.Font.Bold = [int]$Bold.IsPresent
    $paragraph.Range.InsertParagraphAfter()
    return $paragraph
}

function Add-MarkdownTable {
    param(
        $Document,
        [string[]]$Lines
    )

    $rows = @()
    foreach ($line in $Lines) {
        $cells = @($line.Trim().Trim("|").Split("|") | ForEach-Object { (Convert-InlineMarkdown $_) })
        if ($cells.Count -gt 0 -and -not ($cells -join "" -match '^[\s:\-]+$')) {
            $rows += ,$cells
        }
    }
    if ($rows.Count -eq 0) { return }

    $columnCount = ($rows | ForEach-Object Count | Measure-Object -Maximum).Maximum
    $range = $Document.Range($Document.Content.End - 1, $Document.Content.End - 1)
    $table = $Document.Tables.Add($range, $rows.Count, $columnCount)
    $table.Style = "Table Grid"
    $table.AllowAutoFit = $true

    for ($rowIndex = 0; $rowIndex -lt $rows.Count; $rowIndex++) {
        for ($columnIndex = 0; $columnIndex -lt $rows[$rowIndex].Count; $columnIndex++) {
            $table.Cell($rowIndex + 1, $columnIndex + 1).Range.Text = $rows[$rowIndex][$columnIndex]
        }
    }
    $table.Rows.Item(1).Range.Font.Bold = 1
    $table.Rows.Item(1).Shading.BackgroundPatternColor = 14277081
    $table.Range.Font.Size = 8.5
    $table.Range.ParagraphFormat.SpaceAfter = 2
    $table.Range.InsertParagraphAfter()
}

$word = $null
$document = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $document = $word.Documents.Open($outputPath)
    $document.Content.Delete()

    $normal = $document.Styles.Item("Normal")
    $normal.Font.Name = "Arial"
    $normal.Font.Size = 9
    $normal.ParagraphFormat.SpaceAfter = 5
    $normal.ParagraphFormat.LineSpacingRule = 0

    $titleStyle = $document.Styles.Item("Title")
    $titleStyle.Font.Name = "Arial"
    $titleStyle.Font.Size = 18
    $titleStyle.Font.Bold = 1
    $titleStyle.Font.Color = 2434341
    $titleStyle.ParagraphFormat.SpaceAfter = 12

    $heading1 = $document.Styles.Item("Heading 1")
    $heading1.Font.Name = "Arial"
    $heading1.Font.Size = 12
    $heading1.Font.Bold = 1
    $heading1.Font.Color = 2434341
    $heading1.ParagraphFormat.SpaceBefore = 10
    $heading1.ParagraphFormat.SpaceAfter = 4
    $heading1.ParagraphFormat.KeepWithNext = -1

    $heading2 = $document.Styles.Item("Heading 2")
    $heading2.Font.Name = "Arial"
    $heading2.Font.Size = 10
    $heading2.Font.Bold = 1
    $heading2.Font.Color = 2434341
    $heading2.ParagraphFormat.SpaceBefore = 7
    $heading2.ParagraphFormat.SpaceAfter = 3
    $heading2.ParagraphFormat.KeepWithNext = -1

    $section = $document.Sections.Item(1)
    $header = $section.Headers.Item(1).Range
    $header.Text = "SoftwareX manuscript | VLab Orchestrator | Review copy"
    $header.Font.Name = "Arial"
    $header.Font.Size = 8
    $header.Font.Color = 8421504
    $header.ParagraphFormat.Alignment = 2

    $footer = $section.Footers.Item(1).Range
    $footer.Text = "VLab Orchestrator | Page "
    $pageFieldRange = $section.Footers.Item(1).Range.Duplicate
    $pageFieldRange.End = $pageFieldRange.End - 1
    $pageFieldRange.Collapse(0)
    $pageFieldRange.Fields.Add($pageFieldRange, 33) | Out-Null
    $section.Footers.Item(1).Range.Font.Name = "Arial"
    $section.Footers.Item(1).Range.Font.Size = 8
    $section.Footers.Item(1).Range.ParagraphFormat.Alignment = 1

    $lines = Get-Content -LiteralPath $sourcePath
    $tableBuffer = New-Object System.Collections.Generic.List[string]
    $frontMatter = $true
    $inReferences = $false

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]

        if ($line.StartsWith("|")) {
            $tableBuffer.Add($line)
            continue
        }
        if ($tableBuffer.Count -gt 0) {
            Add-MarkdownTable -Document $document -Lines $tableBuffer.ToArray() | Out-Null
            $tableBuffer.Clear()
        }

        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.StartsWith(">")) {
            # Draft instructions remain in Markdown but are omitted from the paper-form review copy.
            continue
        }
        if ($line -match '^!\[([^\]]*)\]\(([^)]+)\)$') {
            $relativePath = $Matches[2].Replace("/", "\")
            $imagePath = Join-Path (Split-Path $sourcePath -Parent) $relativePath
            if (Test-Path -LiteralPath $imagePath) {
                $range = $document.Range($document.Content.End - 1, $document.Content.End - 1)
                $shape = $document.InlineShapes.AddPicture($imagePath, $false, $true, $range)
                if ($shape.Width -gt 470) {
                    $scale = 470 / $shape.Width
                    $shape.Width = 470
                    $shape.Height = $shape.Height * $scale
                }
                $shape.Range.ParagraphFormat.Alignment = 1
                $afterImage = $document.Range($document.Content.End - 1, $document.Content.End - 1)
                $afterImage.InsertParagraphAfter()
            }
            continue
        }
        if ($line.StartsWith("# ")) {
            Add-Paragraph -Document $document -Text (Convert-InlineMarkdown $line.Substring(2)) -Style "Title" -Alignment 1 -Bold | Out-Null
            continue
        }
        if ($line.StartsWith("## ")) {
            $headingText = Convert-InlineMarkdown $line.Substring(3)
            if ($headingText -eq "Authors") {
                continue
            }
            if ($headingText -eq "Abstract") {
                $frontMatter = $false
            }
            if ($headingText -eq "References") {
                $inReferences = $true
            }
            Add-Paragraph -Document $document -Text $headingText -Style "Heading 1" -Bold | Out-Null
            continue
        }
        if ($line.StartsWith("### ")) {
            Add-Paragraph -Document $document -Text (Convert-InlineMarkdown $line.Substring(4)) -Style "Heading 2" -Bold | Out-Null
            continue
        }
        if ($line.StartsWith("- ")) {
            $paragraph = Add-Paragraph -Document $document -Text (Convert-InlineMarkdown $line.Substring(2))
            $paragraph.Range.ListFormat.ApplyBulletDefault()
            continue
        }

        $clean = Convert-InlineMarkdown $line
        if ($clean.StartsWith("Fig. ")) {
            $caption = Add-Paragraph -Document $document -Text $clean -Alignment 1 -Italic
            $caption.Range.Font.Size = 8
            $caption.Range.ParagraphFormat.SpaceAfter = 8
        } elseif ($line.StartsWith("**Keywords:**")) {
            Add-Paragraph -Document $document -Text $clean -Bold | Out-Null
        } elseif ($inReferences -and $clean -match '^\[\d+\]') {
            $reference = Add-Paragraph -Document $document -Text $clean
            $reference.Range.Font.Size = 8
            $reference.Range.ParagraphFormat.LeftIndent = 18
            $reference.Range.ParagraphFormat.FirstLineIndent = -18
            $reference.Range.ParagraphFormat.SpaceAfter = 3
        } elseif ($frontMatter) {
            $authorLine = Add-Paragraph -Document $document -Text $clean -Alignment 1
            $authorLine.Range.Font.Size = 10
        } else {
            Add-Paragraph -Document $document -Text $clean | Out-Null
        }
    }

    if ($tableBuffer.Count -gt 0) {
        Add-MarkdownTable -Document $document -Lines $tableBuffer.ToArray() | Out-Null
    }

    foreach ($paragraph in $document.Paragraphs) {
        if ($paragraph.Range.Text -match '\[[A-Z][A-Z0-9 ,/\-]+\]') {
            $paragraph.Range.HighlightColorIndex = 7
        }
    }

    try {
        $document.BuiltInDocumentProperties.Item("Title").Value =
            "VLab Orchestrator: A multi-backend orchestration and controlled evaluation framework for Unity virtual laboratories"
        $document.BuiltInDocumentProperties.Item("Author").Value = "Nikolaos Marmaras"
        $document.BuiltInDocumentProperties.Item("Subject").Value = "SoftwareX manuscript review copy"
    } catch {
        Write-Warning "Word document properties could not be updated; manuscript content is unaffected."
    }

    $document.Save()
    Write-Output $outputPath
}
finally {
    if ($document) { $document.Close($false) }
    if ($word) { $word.Quit() }
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($document) 2>$null | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) 2>$null | Out-Null
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
