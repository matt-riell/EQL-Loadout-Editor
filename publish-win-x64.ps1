# Publishes EQL Loadout Editor as a self-contained single-file Windows x64 executable.
# Everyday "dotnet build" and Visual Studio F5 are unchanged; this script opts in
# with -p:EqlSingleFilePublish=true.
#
# The SDK still emits EqlLoadouts.App.exe because the project assembly name is unchanged.
# This script renames that file to EqlLoadouts.exe after publish, so Visual Studio F5
# keeps debugging EqlLoadouts.App.exe. Shipped output is EqlLoadouts.exe, LICENSE,
# CONTRIBUTOR_AGREEMENT.md, hash.txt, and Verify.ps1. The license files stay beside the exe
# (excluded from the bundle) so the splash "View license" button can open LICENSE
# from AppContext.BaseDirectory. The .NET desktop runtime and native libraries are
# inside the exe.
#
# The default output folder is artifacts\<version>, where <version> is the Version
# property in src\EqlLoadouts.App\EqlLoadouts.App.csproj. Pass -Output to use another folder.
#
# hash.txt is written after the rename, from the exe and each license file just
# placed in the output folder. It is not a publish input, so it is not embedded
# in the exe. One file per line, uppercase hex, ASCII CRLF:
#   SHA256 EqlLoadouts.exe <hex>
#   SHA256 LICENSE <hex>
#   SHA256 CONTRIBUTOR_AGREEMENT.md <hex>
#
# Verify.ps1 is copied into that same folder from publish-verify.ps1. It hashes those
# three files and compares each hash with expected_hash.txt. A match prints the hash.
# A mismatch warns not to run the application. It is not listed in hash.txt.
#
# global.json pins .NET SDK 10.0.402 and disables roll-forward. This script stops
# when dotnet --version is not that SDK. Same sources and that SDK produce the same
# exe. A different SDK build will not. Two clean publishes with 10.0.402 had one SHA-256.
#
# After hash.txt is written, each generated hash is compared with expected_hash.txt
# in the repository root. A mismatch prints a warning and the publish still finishes.
# Replace expected_hash.txt when a new publish is the hash you want later builds to match.
#
# Tests run first, in Release. A failure stops the script before the previous publish
# folder is removed.
#
# Equivalent commands, from the repository root. This script runs the tests, then publish,
# renames the exe, then writes hash.txt beside it:
#   dotnet test tests\EqlLoadouts.Core.Tests\EqlLoadouts.Core.Tests.csproj -c Release
#   dotnet publish src\EqlLoadouts.App\EqlLoadouts.App.csproj -c Release -o <output> --artifacts-path <output>.artifacts -p:EqlSingleFilePublish=true

[CmdletBinding()]
param(
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$requiredSdk = '10.0.402'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\EqlLoadouts.App\EqlLoadouts.App.csproj'
$tests = Join-Path $root 'tests\EqlLoadouts.Core.Tests\EqlLoadouts.Core.Tests.csproj'

function Get-AppVersion([string] $projectPath) {
    [xml] $proj = Get-Content -LiteralPath $projectPath
    foreach ($group in @($proj.Project.PropertyGroup)) {
        if ($group.Version) {
            return "$($group.Version)".Trim()
        }
    }

    throw "Version was not found in $projectPath"
}

$version = Get-AppVersion $project
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root (Join-Path 'artifacts' $version)
} elseif (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path (Get-Location).Path $Output
}
$Output = [System.IO.Path]::GetFullPath($Output)
Set-Location $root

$outputParent = Split-Path -Parent $Output
$outputName = Split-Path -Leaf $Output
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    $outputParent = (Get-Location).Path
}

$artifacts = Join-Path $outputParent ($outputName + '.artifacts')

$sdk = (& dotnet --version)
if ($LASTEXITCODE -ne 0) {
    Write-Host "This publish requires .NET SDK $requiredSdk. Install that SDK and run the script from this repository."
    exit 1
}
$sdk = "$sdk".Trim()
if ($sdk -ne $requiredSdk) {
    Write-Host "This publish requires .NET SDK $requiredSdk. dotnet --version reported $sdk."
    exit 1
}
Write-Host "SDK: $sdk"
Write-Host "Version: $version"
Write-Host "Running tests..."
& dotnet test $tests -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

& dotnet publish $project -c Release -o $Output --artifacts-path $artifacts -p:EqlSingleFilePublish=true
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

# Assembly name stays EqlLoadouts.App so debug builds are unchanged. Only the shipped file is renamed.
$builtExeName = 'EqlLoadouts.App.exe'
$shippedExeName = 'EqlLoadouts.exe'
$licenseNames = @('LICENSE', 'CONTRIBUTOR_AGREEMENT.md')
$builtExe = Join-Path $Output $builtExeName
$shippedExe = Join-Path $Output $shippedExeName

if (-not (Test-Path -LiteralPath $builtExe)) {
    throw "Publish did not produce $builtExe"
}
if (Test-Path -LiteralPath $shippedExe) {
    Remove-Item -LiteralPath $shippedExe -Force
}
Move-Item -LiteralPath $builtExe -Destination $shippedExe

foreach ($licenseName in $licenseNames) {
    $licensePath = Join-Path $Output $licenseName
    if (-not (Test-Path -LiteralPath $licensePath)) {
        throw "Publish did not produce $licensePath"
    }
}

$hashLines = New-Object System.Collections.Generic.List[string]
$exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $shippedExe).Hash.ToUpperInvariant()
$hashLines.Add("SHA256 $shippedExeName $exeHash")
foreach ($licenseName in $licenseNames) {
    $licensePath = Join-Path $Output $licenseName
    $licenseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $licensePath).Hash.ToUpperInvariant()
    $hashLines.Add("SHA256 $licenseName $licenseHash")
}
$hashFile = Join-Path $Output 'hash.txt'
# ASCII, no BOM, CRLF. A trailing CRLF keeps the last line readable in Notepad.
# Written after publish, so hash.txt is not an input and is not embedded in the exe.
$hashText = ($hashLines -join "`r`n") + "`r`n"
[System.IO.File]::WriteAllText($hashFile, $hashText, [System.Text.Encoding]::ASCII)

$verifySource = Join-Path $root 'publish-verify.ps1'
$verifyScript = Join-Path $Output 'Verify.ps1'
if (-not (Test-Path -LiteralPath $verifySource)) {
    throw "Publish verify script was not found at $verifySource"
}
Copy-Item -LiteralPath $verifySource -Destination $verifyScript

$generatedHashes = @{}
foreach ($line in $hashLines) {
    $parts = $line.Split(' ', 3)
    $generatedHashes[$parts[1]] = $parts[2]
}

$expectedHashFile = Join-Path $root 'expected_hash.txt'
if (-not (Test-Path -LiteralPath $expectedHashFile)) {
    Write-Warning "Expected hashes were not found at $expectedHashFile. Generated hashes were not checked."
} else {
    $expectedHashes = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($expectedHashFile)) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }
        $parts = $trimmed.Split(' ', 3)
        if ($parts.Length -ne 3 -or $parts[0] -ne 'SHA256') {
            Write-Warning "Skipping unrecognized line in ${expectedHashFile}: $trimmed"
            continue
        }
        $expectedHashes[$parts[1]] = $parts[2].ToUpperInvariant()
    }

    $hashMismatch = $false
    foreach ($name in $generatedHashes.Keys) {
        if (-not $expectedHashes.ContainsKey($name)) {
            Write-Warning "No expected hash for $name. Generated $($generatedHashes[$name])."
            $hashMismatch = $true
            continue
        }
        if ($generatedHashes[$name] -ne $expectedHashes[$name]) {
            Write-Warning "$name hash $($generatedHashes[$name]) does not match expected $($expectedHashes[$name])."
            $hashMismatch = $true
        }
    }
    foreach ($name in $expectedHashes.Keys) {
        if (-not $generatedHashes.ContainsKey($name)) {
            Write-Warning "Expected $name was not produced. Expected hash $($expectedHashes[$name])."
            $hashMismatch = $true
        }
    }
    if (-not $hashMismatch) {
        Write-Host "Generated hashes match expected_hash.txt."
    }
}

Write-Host "Published to $Output"
foreach ($line in $hashLines) {
    Write-Host $line
}
Get-ChildItem -LiteralPath $Output | Select-Object Name, Length | Format-Table -AutoSize
