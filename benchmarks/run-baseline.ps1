<#
.SYNOPSIS
  Runs the redirect benchmark (US-04) and writes benchmarks/results/<Name>-<sha7>.json.

.DESCRIPTION
  1. Builds the API image from `git archive <Commit>` so the measured code is exactly that commit
     (or from the working tree with -WorkingTree, for trying the harness; the result is marked dirty).
  2. Starts SQL Server + API with fixed CPU/memory limits (docker-compose.yml + benchmarks/docker-compose.bench.yml).
  3. Creates the database, applies benchmarks/sql/migrate.sql and seeds 100k links (benchmarks/sql/seed.sql).
  4. For each run: k6 warm-up, reset the DB query counter, measured k6 run, read the counter.
  5. Writes per-run numbers, their median and the spread between runs, plus the environment.

  Needs Docker Desktop running and a .env file (copy .env.example).

.EXAMPLE
  ./benchmarks/run-baseline.ps1                       # HEAD, 3 runs
  ./benchmarks/run-baseline.ps1 -WorkingTree -Runs 1  # try uncommitted changes
#>
param(
    [string]$Commit = "HEAD",
    [int]$Runs = 3,
    [int]$Vus = 20,
    [string]$Duration = "60s",
    [string]$WarmUp = "20s",
    [string]$Name = "baseline",
    [switch]$WorkingTree,
    [switch]$Down
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Invoke-Native([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE" }
}

function Get-Median([double[]]$Values) {
    $sorted = $Values | Sort-Object
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

function Get-SpreadPercent([double[]]$Values) {
    $median = Get-Median $Values
    if ($median -eq 0) { return 0 }
    $max = ($Values | Measure-Object -Maximum).Maximum
    $min = ($Values | Measure-Object -Minimum).Minimum
    return [math]::Round(($max - $min) / $median * 100, 1)
}

if (-not (Test-Path .env)) { throw ".env not found. Copy .env.example to .env first." }
$saPassword = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''

$sha = (git rev-parse $Commit).Trim()
$sha7 = $sha.Substring(0, 7)
$dirtyFiles = git status --porcelain | Where-Object { $_ -notmatch 'benchmarks/results/' }
$dirty = [bool]$WorkingTree -or [bool]$dirtyFiles
$label = $sha7
if ($dirty) { $label = "$sha7-dirty" }

$image = "urlshortener-api:bench-$label"
$env:BENCH_API_IMAGE = $image
$compose = @("compose", "-f", "docker-compose.yml", "-f", "benchmarks/docker-compose.bench.yml")

Write-Host "== Building $image"
if ($WorkingTree) {
    Invoke-Native "docker build" { docker build -q -t $image . }
} else {
    # git archive gives exactly the committed files; nothing uncommitted can leak into the image.
    $context = Join-Path ([IO.Path]::GetTempPath()) "urlshortener-$sha7"
    $tar = "$context.tar"
    if (Test-Path $context) { Remove-Item -Recurse -Force $context }
    New-Item -ItemType Directory $context | Out-Null
    Invoke-Native "git archive" { git archive --format=tar -o $tar $sha }
    Invoke-Native "tar extract" { tar -xf $tar -C $context }
    Invoke-Native "docker build" { docker build -q -t $image $context }
    Remove-Item -Recurse -Force $context, $tar
}

Write-Host "== Starting SQL Server"
Invoke-Native "compose up sqlserver" { docker @compose up -d --wait sqlserver }

$sqlcmd = @("exec", "-T", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-b", "-S", "localhost", "-U", "sa", "-P", $saPassword)

Write-Host "== Creating database, migrating, seeding 100k links"
Invoke-Native "create db" { docker @compose @sqlcmd -Q "IF DB_ID('UrlShortener') IS NULL CREATE DATABASE UrlShortener" }
Invoke-Native "copy sql" { docker @compose cp benchmarks/sql/migrate.sql sqlserver:/tmp/migrate.sql }
Invoke-Native "copy sql" { docker @compose cp benchmarks/sql/seed.sql sqlserver:/tmp/seed.sql }
Invoke-Native "migrate" { docker @compose @sqlcmd -d UrlShortener -i /tmp/migrate.sql | Out-Null }
Invoke-Native "seed" { docker @compose @sqlcmd -d UrlShortener -i /tmp/seed.sql }
$sqlVersion = (docker @compose @sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(20)) + ' ' + CAST(SERVERPROPERTY('Edition') AS varchar(60))" | Select-Object -First 1).Trim()

Write-Host "== Starting API (force-recreate so it runs the image just built)"
Invoke-Native "compose up api" { docker @compose up -d --wait --force-recreate --no-build api }

$base = "http://localhost:8080"
$raw = "benchmarks/results/raw"
New-Item -ItemType Directory -Force $raw | Out-Null

$runResults = @()
for ($i = 1; $i -le $Runs; $i++) {
    Write-Host "== Run ${i}/${Runs}: warm-up $WarmUp"
    Invoke-Native "k6 warm-up" { docker @compose --profile bench run --rm k6 run --quiet -e VUS=$Vus -e DURATION=$WarmUp redirect.js | Out-Null }

    Invoke-RestMethod -Method Post "$base/_diag/db-queries/reset" | Out-Null

    Write-Host "== Run ${i}/${Runs}: measuring $Duration with $Vus VUs"
    $summary = "results/raw/$Name-$label-run$i.json"
    Invoke-Native "k6 run" { docker @compose --profile bench run --rm k6 run --quiet -e VUS=$Vus -e DURATION=$Duration --summary-export $summary redirect.js }

    $queries = (Invoke-RestMethod "$base/_diag/db-queries").count
    $k6 = Get-Content "benchmarks/$summary" -Raw | ConvertFrom-Json
    $d = $k6.metrics.http_req_duration
    $requests = [double]$k6.metrics.http_reqs.count

    $runResults += [pscustomobject]@{
        run              = $i
        requests         = $requests
        rps              = [math]::Round([double]$k6.metrics.http_reqs.rate, 1)
        p50Ms            = [math]::Round([double]$d.med, 2)
        p95Ms            = [math]::Round([double]$d.'p(95)', 2)
        p99Ms            = [math]::Round([double]$d.'p(99)', 2)
        avgMs            = [math]::Round([double]$d.avg, 2)
        maxMs            = [math]::Round([double]$d.max, 2)
        errorPercent     = [math]::Round([double]$k6.metrics.http_req_failed.value * 100, 3)
        dbQueries        = $queries
        dbQueriesPerReq  = [math]::Round($queries / $requests, 3)
    }
    $runResults[-1] | Format-List | Out-String | Write-Host
}

$median = [ordered]@{}
foreach ($metric in "rps", "p50Ms", "p95Ms", "p99Ms", "errorPercent", "dbQueriesPerReq") {
    $median[$metric] = Get-Median ($runResults | ForEach-Object { [double]$_.$metric })
}
$spread = [ordered]@{
    rps   = Get-SpreadPercent ($runResults | ForEach-Object { [double]$_.rps })
    p50Ms = Get-SpreadPercent ($runResults | ForEach-Object { [double]$_.p50Ms })
    p95Ms = Get-SpreadPercent ($runResults | ForEach-Object { [double]$_.p95Ms })
}

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$dockerInfo = docker info --format '{{json .}}' | ConvertFrom-Json
$k6Version = (docker run --rm grafana/k6:0.54.0 version | Select-Object -First 1).Trim()

$result = [ordered]@{
    name        = $Name
    commit      = $sha
    dirty       = $dirty
    measuredAt  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    environment = [ordered]@{
        cpu            = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) cores / $($cpu.NumberOfLogicalProcessors) threads)"
        ramGb          = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
        os             = "$($os.Caption) $($os.Version)"
        docker         = "Docker $($dockerInfo.ServerVersion), VM $($dockerInfo.NCPU) CPU / $([math]::Round($dockerInfo.MemTotal / 1GB, 1)) GB"
        limits         = "api 2 CPU / 1 GB, sqlserver 2 CPU / 2 GB (benchmarks/docker-compose.bench.yml)"
        sqlServer      = $sqlVersion
        redis          = "none (SQL-only baseline)"
        k6             = $k6Version
        loadGenerator  = "k6 in a container on the same machine and Docker network as the API; it competes for CPU (limitation)"
    }
    dataset     = [ordered]@{
        links   = 100000
        seed    = "deterministic: code(n) = Base62(n * 1000000007 mod 62^7), benchmarks/sql/seed.sql"
        indexes = "UX_short_links_code (unique, BIN2), IX_short_links_client_id_created_at"
    }
    workload    = [ordered]@{
        endpoint  = "GET /{code}, redirects not followed"
        mix       = "80% hot (20% of links), 15% cold, 5% non-existent (404)"
        vus       = $Vus
        duration  = $Duration
        warmUp    = "$WarmUp per run, excluded from numbers and from the DB query count"
        runs      = $Runs
        cacheTtl  = "n/a (no cache)"
        analytics = "off (not implemented yet)"
    }
    runs        = $runResults
    median      = $median
    spreadPercentBetweenRuns = $spread
}

$out = "benchmarks/results/$Name-$label.json"
$result | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $out
Write-Host "== Wrote $out"
$median | Format-List | Out-String | Write-Host

if ($Down) { docker @compose down | Out-Null }
