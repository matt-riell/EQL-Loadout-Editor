<#
.SYNOPSIS
    Builds the single-file executable from one Git commit.

.DESCRIPTION
    Resolves the commit, checks it out in a clean detached worktree, and runs
    the repository's existing publish-win-x64.ps1. The unsigned executable and
    a build record are written to the output directory. An existing executable
    is never reused.

.PARAMETER Commit
    A commit SHA, tag, or branch.

.PARAMETER OutputDirectory
    Directory that will receive EqlLoadouts.exe, the existing license files, and evidence.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Build-Release.ps1 -Commit HEAD -OutputDirectory .\releases\build
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Commit,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',
    [string] $Repository
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

try {
    Invoke-ReleaseBuild -Commit $Commit -OutputDirectory $OutputDirectory -Configuration $Configuration -Repository $Repository | Out-Null
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
