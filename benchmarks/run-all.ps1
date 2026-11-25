<#
.SYNOPSIS
  Runs every benchmark the report needs, on one machine, with one dataset and one script (US-10).

.DESCRIPTION
  - SQL baseline: mode A on the commit tagged/planned as v0.1-sql-baseline (no cache code at all).
  - Modes A, B, D, E, F on -Commit (the code that has cache + analytics; F needs async analytics, ee9fd90 does not have it).
  Each call writes benchmarks/results/<name>-<mode>-<sha7>.json; benchmarks/REPORT.md is filled from those.
  Takes about 25-30 minutes. Stop every other container first (the runner refuses otherwise).

.EXAMPLE
  ./benchmarks/run-all.ps1 -Commit HEAD
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Commit,
    [string]$BaselineCommit = "15683bb",
    [int]$Runs = 3,
    [switch]$SkipBaseline
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $PSScriptRoot "run-baseline.ps1"

if (-not $SkipBaseline) {
    & $runner -Commit $BaselineCommit -Mode A -Name baseline -Runs $Runs
}

foreach ($mode in "A", "B", "D", "E", "F") {
    & $runner -Commit $Commit -Mode $mode -Name bench -Runs $Runs
}

Write-Host "== All runs done. Results in benchmarks/results/"
