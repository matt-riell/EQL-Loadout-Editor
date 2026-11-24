<#
.SYNOPSIS
    Verifies a signed release, and optionally rebuilds its recorded Git commit.

.DESCRIPTION
    Without -RebuildFromCommit, checks the signed executable hash, Authenticode
    signature, publisher, timestamp, and authenticated provenance when a catalog
    is present. With -RebuildFromCommit, also builds that commit and compares
    the unsigned SHA-256 with the recorded unsigned hash.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Verify-Release.ps1 -ReleaseDirectory .\releases\EqlLoadouts-1.0.0-83a296dac404

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Verify-Release.ps1 -ReleaseDirectory .\releases\EqlLoadouts-1.0.0-83a296dac404 -RebuildFromCommit
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ReleaseDirectory,
    [switch] $RebuildFromCommit,
    [string] $Repository
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

try {
    Invoke-ReleaseVerify -ReleaseDirectory $ReleaseDirectory -RebuildFromCommit:$RebuildFromCommit -Repository $Repository
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
