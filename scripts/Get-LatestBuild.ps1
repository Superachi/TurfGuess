<#
.SYNOPSIS
  Downloads the zipped artifact of the most recent successful "Build & package" run on main.

.DESCRIPTION
  Uses the GitHub CLI (gh, must be logged in). Saves the artifact as a .zip
  (named after the artifact, e.g. TurfGuess-main-1a2b3c4-linux-x64.zip) in the
  Downloads folder by default.

.EXAMPLE
  ./scripts/Get-LatestBuild.ps1
  ./scripts/Get-LatestBuild.ps1 -Branch main -OutDir D:\Temp
#>
[CmdletBinding()]
param(
    [string]$Repo = 'Superachi/TurfGuess',
    [string]$Branch = 'main',
    [string]$Workflow = 'package.yml',
    [string]$OutDir = (Join-Path $env:USERPROFILE 'Downloads')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) not found. Install it from https://cli.github.com and run "gh auth login".'
}

$runJson = gh run list --repo $Repo --workflow $Workflow --branch $Branch --status success --limit 1 `
    --json databaseId,headSha,displayTitle,createdAt
if ($LASTEXITCODE -ne 0) { throw 'gh run list failed.' }

$run = ($runJson | ConvertFrom-Json) | Select-Object -First 1
if (-not $run) { throw "No successful '$Workflow' run found on branch '$Branch'." }

Write-Host "Run $($run.databaseId): $($run.displayTitle) ($($run.headSha.Substring(0, 7)), $($run.createdAt))"

$artifactsJson = gh api "repos/$Repo/actions/runs/$($run.databaseId)/artifacts"
if ($LASTEXITCODE -ne 0) { throw 'Fetching artifacts failed.' }

$artifact = ($artifactsJson | ConvertFrom-Json).artifacts | Where-Object { -not $_.expired } | Select-Object -First 1
if (-not $artifact) { throw 'This run has no non-expired artifact (retention is 30 days).' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$target = Join-Path $OutDir "$($artifact.name).zip"

# "gh run download" unpacks the artifact; re-zip it (piping binary through PS 5.1 would corrupt it).
$tmp = Join-Path ([IO.Path]::GetTempPath()) "turfguess-$($run.databaseId)"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
try {
    gh run download $run.databaseId --repo $Repo --name $artifact.name --dir $tmp
    if ($LASTEXITCODE -ne 0) { throw 'Artifact download failed.' }
    if (Test-Path $target) { Remove-Item -Force $target }
    Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $target
}
finally {
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
}

Write-Host "Saved $([math]::Round((Get-Item $target).Length / 1MB, 1)) MB to $target"
