param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$sourceDirectory = Join-Path $RepositoryRoot "docs\submission"
$outputDirectory = Join-Path $sourceDirectory "word"
$documents = @(
    "COVER_LETTER",
    "HIGHLIGHTS",
    "CREDIT_AUTHOR_STATEMENT",
    "DECLARATION_OF_COMPETING_INTEREST",
    "DATA_AVAILABILITY"
)

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Convert-InlineMarkdown {
    param([string]$Text)

    return ($Text -replace '\*\*([^*]+)\*\*', '$1' -replace '`([^`]+)`', '$1').Trim()
}

$word = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3
    $word.Options.SaveNormalPrompt = $false

    foreach ($name in $documents) {
        $sourcePath = Join-Path $sourceDirectory "$name.md"
        $outputPath = Join-Path $outputDirectory "$name.docx"
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "Submission source not found: $sourcePath"
        }

        $document = $word.Documents.Add()
        try {
            $normal = $document.Styles.Item("Normal")
            $normal.Font.Name = "Arial"
            $normal.Font.Size = 11
            $normal.ParagraphFormat.SpaceAfter = 8

            $sourceText = [IO.File]::ReadAllText($sourcePath)
            $blocks = $sourceText -split "(?:\r?\n){2,}"
            foreach ($block in $blocks) {
                $lines = @($block -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                if ($lines.Count -eq 0) { continue }

                if ($lines[0].StartsWith("- ")) {
                    foreach ($line in $lines) {
                        $range = $document.Range($document.Content.End - 1, $document.Content.End - 1)
                        $paragraph = $document.Paragraphs.Add($range)
                        $paragraph.Range.Text = Convert-InlineMarkdown $line.Substring(2)
                        $paragraph.Range.ListFormat.ApplyBulletDefault()
                        $paragraph.Range.InsertParagraphAfter()
                    }
                    continue
                }

                $line = ($lines -join " ").Trim()
                $range = $document.Range($document.Content.End - 1, $document.Content.End - 1)
                $paragraph = $document.Paragraphs.Add($range)
                if ($line.StartsWith("# ")) {
                    $paragraph.Range.Text = Convert-InlineMarkdown $line.Substring(2)
                    $paragraph.Range.Style = "Title"
                } elseif ($line.StartsWith("## ")) {
                    $paragraph.Range.Text = Convert-InlineMarkdown $line.Substring(3)
                    $paragraph.Range.Style = "Heading 1"
                } else {
                    $paragraph.Range.Text = Convert-InlineMarkdown $line
                    $paragraph.Range.Style = "Normal"
                }
                $paragraph.Range.InsertParagraphAfter()
            }

            $document.SaveAs2([string]$outputPath, 16)
        } finally {
            $document.Close($false)
        }
    }
} finally {
    if ($word -ne $null) {
        $word.Quit()
        [Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

Write-Host "SoftwareX submission documents written to $outputDirectory"
