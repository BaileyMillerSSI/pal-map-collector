[CmdletBinding()]
param(
    [switch]$History
)

$ErrorActionPreference = 'Stop'

$patterns = @(
    '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    'AKIA[0-9A-Z]{16}',
    'ASIA[0-9A-Z]{16}',
    'github_pat_[A-Za-z0-9_]{30,}',
    'gh[pousr]_[A-Za-z0-9]{30,}',
    'xox[baprs]-[A-Za-z0-9-]{20,}',
    'https?://[^/:\s]+:[^/@\s]+@'
)
$combinedPattern = $patterns -join '|'
$hits = [System.Collections.Generic.List[string]]::new()

function Test-Content {
    param(
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]]$Content
    )

    for ($lineNumber = 0; $lineNumber -lt $Content.Count; $lineNumber++) {
        if ($Content[$lineNumber] -match $combinedPattern) {
            $hits.Add("${Label}:$($lineNumber + 1)")
        }
    }
}

if ($History) {
    foreach ($revision in (git rev-list --all)) {
        foreach ($path in (git ls-tree -r --name-only $revision)) {
            $content = git show "${revision}:$path" 2>$null
            if ($LASTEXITCODE -eq 0) {
                Test-Content -Label "$($revision.Substring(0, 12)):$path" -Content $content
            }
        }
    }
}
else {
    foreach ($path in (git ls-files)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Test-Content -Label $path -Content (Get-Content -LiteralPath $path)
        }
    }
}

if ($hits.Count -gt 0) {
    Write-Error "Potential secret material found at:`n$($hits -join "`n")"
}

Write-Output "Secret scan completed without high-confidence matches."
