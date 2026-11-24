<#
.SYNOPSIS
    Builds one commit, signs the executable, writes release provenance, and zips the release folder.

.DESCRIPTION
    Runs the existing deterministic publish, signs with Azure Artifact Signing,
    verifies the signature, and seals the provenance in an Authenticode catalog
    signed by the same certificate profile. The catalog is the release attestation.
    Pass -CreateSignedTag only when git user.signingkey is already configured.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit HEAD

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit 83a296dac404aebe9c804ad3f4126d82dbcd8f73
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Commit,
    [string] $OutputRoot,
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',
    [switch] $CreateSignedTag,
    [string] $Repository
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

try {
    Invoke-SignedRelease -Commit $Commit -OutputRoot $OutputRoot -Configuration $Configuration -CreateSignedTag:$CreateSignedTag -Repository $Repository | Out-Null
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
