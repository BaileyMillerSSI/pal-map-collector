[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory
)

$ErrorActionPreference = "Stop"
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter "*.nupkg" -File)

if ($packages.Count -ne 1) {
    throw "Expected exactly one protocol package in '$PackageDirectory'; found $($packages.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)

try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/').ToLowerInvariant() })
    $requirements = @(
        @{ Name = "NuGet manifest"; Matches = { param($entry) $entry.EndsWith(".nuspec") } },
        @{ Name = "package README"; Matches = { param($entry) $entry -eq "readme.md" } },
        @{ Name = "compiled protocol assembly"; Matches = { param($entry) $entry -match '^lib/[^/]+/palmap\.protocol\.dll$' } },
        @{ Name = "v1 JSON Schema"; Matches = { param($entry) $entry.EndsWith("schema/palmap-snapshot-v1.schema.json") } },
        @{ Name = "synthetic v1 fixture"; Matches = { param($entry) $entry.EndsWith("fixtures/snapshot-v1.synthetic.json") } }
    )

    foreach ($requirement in $requirements) {
        $matches = $requirement["Matches"]
        $found = $false
        foreach ($entry in $entries) {
            if (& $matches $entry) {
                $found = $true
                break
            }
        }

        if (-not $found) {
            throw "Protocol package is missing $($requirement["Name"])."
        }
    }

    $forbidden = @($entries | Where-Object {
        $_ -match '(^|/)(\.env($|\.)|.*\.(pfx|p12|snk|key|pem))$'
    })

    if ($forbidden.Count -gt 0) {
        throw "Protocol package contains forbidden secret-bearing file types: $($forbidden -join ', ')"
    }

    $unexpectedAssemblies = @($entries | Where-Object {
        $_ -match '^lib/[^/]+/.*\.dll$' -and $_ -notmatch '^lib/[^/]+/palmap\.protocol\.dll$'
    })

    if ($unexpectedAssemblies.Count -gt 0) {
        throw "Protocol package contains non-protocol assemblies: $($unexpectedAssemblies -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Validated protocol package '$($packages[0].Name)'."
