[CmdletBinding()]
param(
    [switch]$History
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gitRootArguments = @('-c', "safe.directory=$repositoryRoot", '-C', $repositoryRoot)

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
    $revisions = git @gitRootArguments rev-list --all
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate Git history.' }
    foreach ($revision in $revisions) {
        $paths = git @gitRootArguments ls-tree -r --name-only $revision
        if ($LASTEXITCODE -ne 0) { throw "Unable to enumerate revision $revision." }
        foreach ($path in $paths) {
            $content = git @gitRootArguments show "${revision}:$path" 2>$null
            if ($LASTEXITCODE -eq 0) {
                Test-Content -Label "$($revision.Substring(0, 12)):$path" -Content $content
            }
            else { throw "Unable to inspect $($revision.Substring(0, 12)):$path." }
        }
    }
}
else {
    $paths = git @gitRootArguments ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files.' }
    foreach ($path in $paths) {
        $fullPath = Join-Path $repositoryRoot $path
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            Test-Content -Label $path -Content (Get-Content -LiteralPath $fullPath)
        }
    }
}

if ($hits.Count -gt 0) {
    Write-Error "Potential secret material found at:`n$($hits -join "`n")"
}

Write-Output "Secret scan completed without high-confidence matches."
