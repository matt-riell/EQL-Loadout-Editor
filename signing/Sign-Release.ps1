<#
.SYNOPSIS
    Signs one executable with the configured Azure Artifact Signing profile.

.DESCRIPTION
    Uses SignTool, Azure.CodeSigning.Dlib.dll, SHA-256 digests, and the Microsoft
    Artifact Signing timestamp service. The script verifies the signature and
    publisher before it returns. It fails if verification fails.

.PARAMETER ExePath
    Unsigned executable to sign in place.

.PARAMETER CorrelationId
    Release correlation id sent in the SignTool metadata. One is generated when omitted.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Sign-Release.ps1 -ExePath .\releases\EqlLoadouts.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExePath,
    [string] $EvidenceDirectory,
    [string] $CorrelationId,
    [string] $ConfigPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

try {
    Invoke-ReleaseSign -ExePath $ExePath -EvidenceDirectory $EvidenceDirectory -CorrelationId $CorrelationId -ConfigPath $ConfigPath | Out-Null
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
