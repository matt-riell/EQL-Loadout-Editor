# Hashes the published files in this folder and compares each hash with
# expected_hash.txt. expected_hash.txt is read from this folder when present,
# otherwise from the nearest parent folder. The repository copy lives at the
# repository root. A match prints one line per file. A mismatch prints a
# warning not to run the application and exits with code 1.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$names = @('EqlLoadouts.exe', 'LICENSE', 'CONTRIBUTOR_AGREEMENT.md')

function Read-HashSet([string] $path) {
    $set = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        $parts = $trimmed.Split(' ', 3)
        if ($parts.Length -ne 3 -or $parts[0] -ne 'SHA256') {
            throw "Unrecognized line in ${path}: $trimmed"
        }

        $set[$parts[1]] = $parts[2].ToUpperInvariant()
    }

    return $set
}

function Find-ExpectedHashFile([string] $start) {
    $current = $start
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        $candidate = Join-Path $current 'expected_hash.txt'
        if (Test-Path -LiteralPath $candidate) {
            return [System.IO.Path]::GetFullPath($candidate)
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

$failed = $false
$expectedPath = Find-ExpectedHashFile $dir
if (-not $expectedPath) {
    Write-Warning "expected_hash.txt was not found. The file hashes could not be verified. Do not run the application."
    exit 1
}

$expected = Read-HashSet $expectedPath
foreach ($name in $names) {
    $path = Join-Path $dir $name
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "$name is missing. Its hash does not match the expected hash. Do not run the application."
        $failed = $true
        continue
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToUpperInvariant()
    if (-not $expected.ContainsKey($name) -or $expected[$name] -ne $actual) {
        $expectedHash = if ($expected.ContainsKey($name)) { $expected[$name] } else { "no expected hash" }
        Write-Warning "$name hash does not match the expected hash. Do not run the application."
        Write-Host "  file:     $actual"
        Write-Host "  expected: $expectedHash"
        $failed = $true
        continue
    }

    Write-Host "$name hash verified against expected hash. $actual"
}

if ($failed) {
    exit 1
}

exit 0
