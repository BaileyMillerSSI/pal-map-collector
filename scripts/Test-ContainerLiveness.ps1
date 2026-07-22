[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Image,

    [Parameter(Mandatory)]
    [ValidateSet("linux/amd64", "linux/arm64")]
    [string] $Platform,

    [ValidateRange(1, 180)]
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$architecture = $Platform.Split('/')[1]
$containerName = "palmap-collector-smoke-$architecture-$PID"

$environment = @(
    "ASPNETCORE_ENVIRONMENT=Development",
    "PalworldApi__BaseUrl=http://127.0.0.1:9",
    "PalworldApi__Admin__Username=synthetic-ci-admin",
    "PalworldApi__Admin__Password=synthetic-ci-password",
    "PalmapIngest__Endpoint=http://127.0.0.1:5080/api/ingest/v1/snapshots",
    "PalmapIngest__ClientId=pmc_AAAAAAAAAAAAAAAAAAAA",
    "PalmapIngest__ClientSecret=BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
    "PalmapIngest__PrivacyKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
    "PalmapIngest__AllowInsecureHttp=true"
)

$arguments = @(
    "run",
    "--detach",
    "--platform", $Platform,
    "--name", $containerName
)

foreach ($entry in $environment) {
    $arguments += @("--env", $entry)
}

$arguments += $Image

try {
    $containerId = & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to start '$Image' for $Platform."
    }

    Write-Host "Started $containerId for $Platform; waiting for /health/live."
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        & docker exec $containerName wget -q -O /dev/null http://127.0.0.1:8080/health/live
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Liveness smoke passed for $Platform."
            return
        }

        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    & docker logs $containerName
    throw "Collector liveness did not succeed for $Platform within $TimeoutSeconds seconds."
}
finally {
    & docker rm --force $containerName 2>$null | Out-Null
}
