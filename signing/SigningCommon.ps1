# Shared helpers for the Azure Artifact Signing release scripts.
# Dot-sourced by the scripts in this folder. This file does not sign, build, or
# change machine configuration on its own.

$script:RequiredSdk = '10.0.402'
$script:ExpectedPublisher = 'Generation X Technologies, Inc.'
$script:TimestampUrl = 'http://timestamp.acs.microsoft.com'
$script:ShippedExeName = 'EqlLoadouts.exe'
$script:ApplicationName = 'EQL Loadout Editor'
$script:SuggestedAccountName = 'genxtechsigning26'
$script:SuggestedProfileName = 'genx-public-signing'
$script:MinimumSignToolVersion = [version]'10.0.22621.755'
$script:SigningDirectory = $PSScriptRoot

$script:RegionEndpoints = @{
    'brazilsouth'      = 'https://brs.codesigning.azure.net'
    'centralus'        = 'https://cus.codesigning.azure.net'
    'eastus'           = 'https://eus.codesigning.azure.net'
    'japaneast'        = 'https://jpe.codesigning.azure.net'
    'koreacentral'     = 'https://krc.codesigning.azure.net'
    'northcentralus'   = 'https://ncus.codesigning.azure.net'
    'northeurope'      = 'https://neu.codesigning.azure.net'
    'polandcentral'    = 'https://plc.codesigning.azure.net'
    'southcentralus'   = 'https://scus.codesigning.azure.net'
    'switzerlandnorth' = 'https://swn.codesigning.azure.net'
    'westcentralus'    = 'https://wcus.codesigning.azure.net'
    'westeurope'       = 'https://weu.codesigning.azure.net'
    'westus'           = 'https://wus.codesigning.azure.net'
    'westus2'          = 'https://wus2.codesigning.azure.net'
    'westus3'          = 'https://wus3.codesigning.azure.net'
}

function Get-SigningDirectory {
    if ([string]::IsNullOrWhiteSpace($script:SigningDirectory)) {
        throw 'The signing helpers were loaded without a script directory.'
    }

    return $script:SigningDirectory
}

function Get-SigningLocalDirectory {
    $dir = Join-Path (Get-SigningDirectory) 'local'
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    return $dir
}

function Get-UtcTimestamp {
    return [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Text
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function ConvertTo-ReleaseJson {
    param(
        [Parameter(Mandatory = $true)] $Value
    )

    return ($Value | ConvertTo-Json -Depth 20)
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Value
    )

    $json = ConvertTo-ReleaseJson $Value
    if (-not $json.EndsWith("`n")) {
        $json += "`r`n"
    }

    Write-Utf8NoBom -Path $Path -Text $json
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "JSON file was not found: $Path"
    }

    $text = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "JSON file is empty: $Path"
    }

    return ($text | ConvertFrom-Json)
}

function ConvertTo-ObjectList {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return $Value
    }

    return @($Value)
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File was not found for hashing: $Path"
    }

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToUpperInvariant()
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Resolve-AzureCli {
    Update-SessionPath
    $command = Get-Command az -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Microsoft SDKs\Azure\CLI2\wbin\az.cmd'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SDKs\Azure\CLI2\wbin\az.cmd'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Azure CLI\az.cmd')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $directory = Split-Path -Parent $candidate
            if ($env:Path -notlike "*$directory*") {
                $env:Path = $directory + ';' + $env:Path
            }

            return $candidate
        }
    }

    return $null
}

function Get-RepositoryRoot {
    $start = Split-Path -Parent (Get-SigningDirectory)
    $output = & git -C $start rev-parse --show-toplevel 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git did not find a repository above $(Get-SigningDirectory). $output"
    }

    return "$output".Trim()
}

function Get-ToolPathsFile {
    return (Join-Path (Get-SigningLocalDirectory) 'tool-paths.json')
}

function Get-SigningConfigFile {
    return (Join-Path (Get-SigningLocalDirectory) 'artifact-signing.config.json')
}

function Save-ToolPaths {
    param($Tools)

    Write-JsonFile -Path (Get-ToolPathsFile) -Value $Tools
}

function Read-ToolPaths {
    $path = Get-ToolPathsFile
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }

    return Read-JsonFile $path
}

function Save-SigningConfig {
    param($Config)

    Write-JsonFile -Path (Get-SigningConfigFile) -Value $Config
}

function Read-SigningConfig {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = Get-SigningConfigFile
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Artifact Signing configuration was not found at $Path. Run signing\Configure-ArtifactSigning.ps1 first."
    }

    $config = Read-JsonFile $Path
    foreach ($name in @('accountName', 'certificateProfileName', 'endpoint', 'expectedPublisher')) {
        $value = $config.$name
        if ([string]::IsNullOrWhiteSpace("$value")) {
            throw "Artifact Signing configuration $Path is missing $name."
        }
    }

    if ("$($config.expectedPublisher)" -ne $script:ExpectedPublisher) {
        throw "Configured publisher '$($config.expectedPublisher)' is not $($script:ExpectedPublisher)."
    }

    if ("$($config.profileType)" -and "$($config.profileType)" -ne 'PublicTrust') {
        throw "Certificate profile type is $($config.profileType). Release signing requires PublicTrust."
    }

    $endpoint = "$($config.endpoint)".Trim().TrimEnd('/')
    if ($endpoint -notmatch '^https://[a-z0-9]+\.codesigning\.azure\.net$') {
        throw "Artifact Signing endpoint '$endpoint' is not a regional https://*.codesigning.azure.net URI."
    }

    $config.endpoint = $endpoint
    return $config
}

function Get-PeArchitecture {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x40) {
        throw "File is too small to be a PE image: $Path"
    }

    $peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $bytes.Length) {
        throw "PE header offset is invalid in $Path"
    }

    $machine = [int][System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
    switch ($machine) {
        0x8664 { return 'x64' }
        0x014C { return 'x86' }
        0xAA64 { return 'arm64' }
        default { return ('unknown-{0:X4}' -f $machine) }
    }
}

function Get-SignToolVersion {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    $info = (Get-Item -LiteralPath $Path).VersionInfo
    foreach ($prefix in @('Product', 'File')) {
        $major = $info.("${prefix}MajorPart")
        if ($major -gt 0) {
            $minor = $info.("${prefix}MinorPart")
            $build = $info.("${prefix}BuildPart")
            $private = $info.("${prefix}PrivatePart")
            return [version]("$major.$minor.$build.$private")
        }
    }

    $raw = "$($info.ProductVersion) $($info.FileVersion)"
    if ($raw -match '(\d+\.\d+\.\d+\.\d+)') {
        return [version]$Matches[1]
    }

    throw "Could not parse the SignTool file version '$($info.FileVersion)' from $Path."
}

function Test-SupportedSignTool {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    $version = Get-SignToolVersion $Path
    if ($version.Major -eq 10 -and $version.Minor -eq 0 -and $version.Build -eq 20348) {
        return $false
    }

    return ($version -ge $script:MinimumSignToolVersion)
}

function Get-CandidateFiles {
    param(
        [Parameter(Mandatory = $true)][string[]] $Roots,
        [Parameter(Mandatory = $true)][string] $Filter
    )

    $found = New-Object System.Collections.Generic.List[string]
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) {
            continue
        }

        Get-ChildItem -LiteralPath $root -Filter $Filter -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
            $found.Add($_.FullName)
        }
    }

    return $found
}

function Get-SigningSearchRoots {
    $programFilesX86 = ${env:ProgramFiles(x86)}
    $roots = @(
        (Join-Path $programFilesX86 'Windows Kits\10\bin'),
        (Join-Path $programFilesX86 'Microsoft\ArtifactSigningClientTools'),
        (Join-Path $env:ProgramFiles 'Microsoft\ArtifactSigningClientTools'),
        (Join-Path $programFilesX86 'Microsoft Artifact Signing Client Tools'),
        (Join-Path $env:ProgramFiles 'Microsoft Artifact Signing Client Tools'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\ArtifactSigningClientTools'),
        (Join-Path (Get-SigningDirectory) 'tools'),
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.artifactsigning.client'),
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.trusted.signing.client'),
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools')
    )

    return $roots
}

function Find-SigningToolPair {
    $roots = Get-SigningSearchRoots
    $signTools = @(Get-CandidateFiles -Roots $roots -Filter 'signtool.exe' | Where-Object { $_ -match '\\x64\\signtool\.exe$' -or $_ -match '\\X64\\signtool\.exe$' })
    if ($signTools.Count -eq 0) {
        $signTools = @(Get-CandidateFiles -Roots $roots -Filter 'signtool.exe')
    }

    $dlibs = @(Get-CandidateFiles -Roots $roots -Filter 'Azure.CodeSigning.Dlib.dll')
    $pairs = New-Object System.Collections.Generic.List[object]
    foreach ($signTool in $signTools) {
        if (-not (Test-SupportedSignTool $signTool)) {
            continue
        }

        $signArch = Get-PeArchitecture $signTool
        foreach ($dlib in $dlibs) {
            $dlibArch = Get-PeArchitecture $dlib
            if ($signArch -ne $dlibArch) {
                continue
            }

            $pairs.Add([pscustomobject]@{
                SignToolPath = $signTool
                SignToolVersion = (Get-SignToolVersion $signTool).ToString()
                SignToolArchitecture = $signArch
                DlibPath = $dlib
                DlibArchitecture = $dlibArch
            })
        }
    }

    $preferred = @($pairs | Where-Object { $_.SignToolArchitecture -eq 'x64' } | Sort-Object { [version]$_.SignToolVersion } -Descending)
    if ($preferred.Count -gt 0) {
        return $preferred[0]
    }

    $any = @($pairs | Sort-Object { [version]$_.SignToolVersion } -Descending)
    if ($any.Count -gt 0) {
        return $any[0]
    }

    return $null
}

function Resolve-SigningToolPair {
    $saved = Read-ToolPaths
    if ($saved -and $saved.signToolPath -and $saved.dlibPath) {
        if ((Test-Path -LiteralPath $saved.signToolPath) -and (Test-Path -LiteralPath $saved.dlibPath)) {
            $signArch = Get-PeArchitecture $saved.signToolPath
            $dlibArch = Get-PeArchitecture $saved.dlibPath
            if ($signArch -eq $dlibArch -and (Test-SupportedSignTool $saved.signToolPath)) {
                return [pscustomobject]@{
                    SignToolPath = "$($saved.signToolPath)"
                    SignToolVersion = (Get-SignToolVersion $saved.signToolPath).ToString()
                    SignToolArchitecture = $signArch
                    DlibPath = "$($saved.dlibPath)"
                    DlibArchitecture = $dlibArch
                }
            }
        }
    }

    $found = Find-SigningToolPair
    if (-not $found) {
        throw "A compatible SignTool and Azure.CodeSigning.Dlib.dll pair was not found. Run signing\Setup-SigningEnvironment.ps1."
    }

    return $found
}

function Resolve-SignToolPath {
    $saved = Read-ToolPaths
    if ($saved -and $saved.signToolPath -and (Test-Path -LiteralPath $saved.signToolPath) -and (Test-SupportedSignTool $saved.signToolPath)) {
        return "$($saved.signToolPath)"
    }

    $pair = Find-SigningToolPair
    if ($pair) {
        return $pair.SignToolPath
    }

    $roots = Get-SigningSearchRoots
    $signTools = @(Get-CandidateFiles -Roots $roots -Filter 'signtool.exe' | Where-Object { Test-SupportedSignTool $_ })
    $x64 = @($signTools | Where-Object { (Get-PeArchitecture $_) -eq 'x64' } | Sort-Object { Get-SignToolVersion $_ } -Descending)
    if ($x64.Count -gt 0) {
        return $x64[0]
    }

    if ($signTools.Count -gt 0) {
        return $signTools[0]
    }

    throw "SignTool 10.0.22621.755 or newer was not found. Run signing\Setup-SigningEnvironment.ps1."
}

function Get-DotNetCandidates {
    $candidates = New-Object System.Collections.Generic.List[string]
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        $candidates.Add($command.Source)
    }

    foreach ($path in @(
            (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
            (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
        )) {
        if (-not $candidates.Contains($path)) {
            $candidates.Add($path)
        }
    }

    $saved = Read-ToolPaths
    if ($saved -and $saved.dotnetPath -and -not $candidates.Contains("$($saved.dotnetPath)")) {
        $candidates.Add("$($saved.dotnetPath)")
    }

    return $candidates
}

function Resolve-DotNetSdk {
    foreach ($candidate in @(Get-DotNetCandidates)) {
        if (-not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $sdks = & $candidate --list-sdks 2>&1
        if ($LASTEXITCODE -ne 0) {
            continue
        }

        foreach ($line in @($sdks)) {
            if ("$line".StartsWith("$($script:RequiredSdk) ")) {
                return [pscustomobject]@{
                    DotNetPath = $candidate
                    SdkVersion = $script:RequiredSdk
                    Directory = Split-Path -Parent $candidate
                }
            }
        }
    }

    throw "The .NET SDK $($script:RequiredSdk) required by global.json was not found. Run signing\Setup-SigningEnvironment.ps1."
}

function Get-RuntimeVersions {
    param(
        [Parameter(Mandatory = $true)][string] $DotNetPath
    )

    $output = & $DotNetPath --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --list-runtimes failed using $DotNetPath. $output"
    }

    return @($output | ForEach-Object { "$_" })
}

function Test-RuntimePresent {
    param(
        [Parameter(Mandatory = $true)][string[]] $Runtimes,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Major
    )

    foreach ($line in $Runtimes) {
        if ($line.StartsWith("$Name $Major.")) {
            return $true
        }
    }

    return $false
}

function Test-VcRedistX64 {
    $key = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
    if (-not (Test-Path -LiteralPath $key)) {
        return $null
    }

    $value = Get-ItemProperty -LiteralPath $key
    if ($value.Installed -ne 1) {
        return $null
    }

    return [pscustomobject]@{
        Installed = $true
        Version = "$($value.Version)"
        Major = $value.Major
        Minor = $value.Minor
        Bld = $value.Bld
    }
}

function Resolve-GitCommit {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $Reference
    )

    if ([string]::IsNullOrWhiteSpace($Reference)) {
        throw 'A Git commit, tag, or branch reference is required.'
    }

    $sha = & git -C $Repository rev-parse --verify --end-of-options "$Reference^{commit}" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git reference '$Reference' does not resolve to a commit in $Repository. $sha"
    }

    $sha = "$sha".Trim()
    if ($sha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Git resolved '$Reference' to '$sha', which is not a full commit SHA."
    }

    $type = & git -C $Repository cat-file -t $sha 2>&1
    if ($LASTEXITCODE -ne 0 -or "$type".Trim() -ne 'commit') {
        throw "Commit $sha was not found in $Repository. $type"
    }

    return $sha.ToLowerInvariant()
}

function Get-GitCommitInfo {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $Reference
    )

    $sha = Resolve-GitCommit -Repository $Repository -Reference $Reference
    $format = '%H%x1f%cI%x1f%an%x1f%ae%x1f%s'
    $raw = & git -C $Repository log -1 --format=$format $sha 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read commit $sha. $raw"
    }

    $parts = "$raw".Split([char]0x1f)
    if ($parts.Count -lt 5) {
        throw "Unexpected git log output for $sha."
    }

    $remote = & git -C $Repository remote get-url origin 2>&1
    $repositoryId = if ($LASTEXITCODE -eq 0) { "$remote".Trim() } else { $Repository }
    $tags = @(& git -C $Repository tag --points-at $sha 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $tags = @()
    }

    $tagNames = @($tags | ForEach-Object { "$_".Trim() } | Where-Object { $_ -and $_ -notmatch '^(fatal|error):' })
    $releaseTag = $null
    if ($tagNames.Count -eq 1) {
        $releaseTag = $tagNames[0]
    }

    $autoCrlf = & git -C $Repository config --get core.autocrlf 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$autoCrlf")) {
        $autoCrlf = 'unset'
    }

    $eol = & git -C $Repository config --get core.eol 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$eol")) {
        $eol = 'unset'
    }

    return [pscustomobject]@{
        Sha = $sha
        ShortSha = $sha.Substring(0, 12)
        CommitTimestamp = $parts[1].Trim()
        AuthorName = $parts[2].Trim()
        AuthorEmail = $parts[3].Trim()
        Author = ("{0} <{1}>" -f $parts[2].Trim(), $parts[3].Trim())
        Subject = $parts[4].Trim()
        Repository = $repositoryId
        ReleaseTag = $releaseTag
        ReleaseTags = @($tagNames)
        CoreAutoCrlf = "$autoCrlf".Trim()
        CoreEol = "$eol".Trim()
    }
}

function Get-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)][string] $ProjectPath
    )

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project file was not found: $ProjectPath"
    }

    [xml] $project = Get-Content -LiteralPath $ProjectPath
    foreach ($group in @(ConvertTo-ObjectList $project.Project.PropertyGroup)) {
        if ($group.Version) {
            return "$($group.Version)".Trim()
        }
    }

    throw "Version was not found in $ProjectPath"
}

function Get-VersionAtCommit {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $Sha
    )

    $text = & git -C $Repository show "${Sha}:src/EqlLoadouts.App/EqlLoadouts.App.csproj" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the application project at $Sha. $text"
    }

    $joined = ($text | Out-String)
    if ($joined -notmatch '<Version>\s*([^<]+?)\s*</Version>') {
        throw "Version was not found in src/EqlLoadouts.App/EqlLoadouts.App.csproj at $Sha."
    }

    return $Matches[1].Trim()
}

function Get-InputHashSet {
    param(
        [Parameter(Mandatory = $true)][string] $Root
    )

    $relative = @(
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'publish-win-x64.ps1',
        'src\EqlLoadouts.App\EqlLoadouts.App.csproj',
        'src\EqlLoadouts.Core\EqlLoadouts.Core.csproj',
        'tests\EqlLoadouts.Core.Tests\EqlLoadouts.Core.Tests.csproj'
    )

    $hashes = [ordered]@{}
    foreach ($item in $relative) {
        $path = Join-Path $Root $item
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Build input was not found in the checkout: $path"
        }

        $hashes[$item.Replace('\', '/')] = Get-Sha256Hex $path
    }

    return ,$hashes
}

function Get-WindowsDescription {
    $current = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $display = "$($current.ProductName) $($current.DisplayVersion) build $($current.CurrentBuild)"
    return $display.Trim()
}

function New-ReleaseCorrelationId {
    param(
        [Parameter(Mandatory = $true)][string] $ShortSha
    )

    return "eql-loadout-$ShortSha-$([guid]::NewGuid().ToString('D'))"
}

function Get-RegionEndpoint {
    param(
        [Parameter(Mandatory = $true)][string] $Location
    )

    $key = ($Location -replace '\s', '').ToLowerInvariant()
    if ($script:RegionEndpoints.ContainsKey($key)) {
        return $script:RegionEndpoints[$key]
    }

    throw "No Artifact Signing endpoint is known for Azure region '$Location'. The account region must be one of: $($script:RegionEndpoints.Keys -join ', ')."
}

function Find-NestedString {
    param(
        $Value,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [int] $Depth = 0
    )

    if ($null -eq $Value -or $Depth -gt 8) {
        return $null
    }

    if ($Value -is [string]) {
        if ($Value -match $Pattern) {
            return $Value
        }

        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($entry in @($Value.Values)) {
            $found = Find-NestedString -Value $entry -Pattern $Pattern -Depth ($Depth + 1)
            if ($found) {
                return $found
            }
        }

        return $null
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        foreach ($entry in @($Value)) {
            $found = Find-NestedString -Value $entry -Pattern $Pattern -Depth ($Depth + 1)
            if ($found) {
                return $found
            }
        }

        return $null
    }

    foreach ($property in @($Value.PSObject.Properties)) {
        $found = Find-NestedString -Value $property.Value -Pattern $Pattern -Depth ($Depth + 1)
        if ($found) {
            return $found
        }
    }

    return $null
}

function Get-ProfileType {
    param($Profile)

    foreach ($name in @('profileType', 'profile_type')) {
        if ($Profile.PSObject.Properties.Name -contains $name -and $Profile.$name) {
            return "$($Profile.$name)"
        }

        if ($Profile.properties -and $Profile.properties.PSObject.Properties.Name -contains $name -and $Profile.properties.$name) {
            return "$($Profile.properties.$name)"
        }
    }

    return ''
}

function New-SigningMetadataObject {
    param(
        [Parameter(Mandatory = $true)] $Config,
        [Parameter(Mandatory = $true)][string] $CorrelationId
    )

    # Azure CLI is the identity Configure-ArtifactSigning.ps1 validated.
    # Interactive browser remains available when az login has not been completed.
    # No secrets are written. CorrelationId ties the request to this release.
    return ,[ordered]@{
        Endpoint = "$($Config.endpoint)"
        CodeSigningAccountName = "$($Config.accountName)"
        CertificateProfileName = "$($Config.certificateProfileName)"
        CorrelationId = $CorrelationId
        ExcludeCredentials = @(
            'EnvironmentCredential',
            'WorkloadIdentityCredential',
            'ManagedIdentityCredential',
            'SharedTokenCacheCredential',
            'VisualStudioCredential',
            'VisualStudioCodeCredential',
            'AzurePowerShellCredential',
            'AzureDeveloperCliCredential'
        )
    }
}

function Write-SigningMetadata {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Config,
        [Parameter(Mandatory = $true)][string] $CorrelationId
    )

    $metadata = New-SigningMetadataObject -Config $Config -CorrelationId $CorrelationId
    Write-JsonFile -Path $Path -Value $metadata
    return ,$metadata
}

function Invoke-SignToolCapture {
    param(
        [Parameter(Mandatory = $true)][string] $SignToolPath,
        [Parameter(Mandatory = $true)][string[]] $ArgumentList
    )

    # SignTool writes failures to stderr. With ErrorActionPreference Stop, that
    # stderr becomes a terminating error before the exit code can be checked.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $SignToolPath @ArgumentList 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $text = (($output | ForEach-Object { "$_" }) -join "`r`n")
    return [pscustomobject]@{
        ExitCode = $code
        Output = $text
    }
}

function Get-AuthenticodeReport {
    param(
        [Parameter(Mandatory = $true)][string] $Path
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $subject = $null
    $thumbprint = $null
    $timestamper = $null
    if ($signature.SignerCertificate) {
        $subject = $signature.SignerCertificate.Subject
        $thumbprint = $signature.SignerCertificate.Thumbprint
    }

    if ($signature.TimeStamperCertificate) {
        $timestamper = $signature.TimeStamperCertificate.Subject
    }

    return [pscustomobject]@{
        Status = "$($signature.Status)"
        StatusMessage = "$($signature.StatusMessage)"
        Subject = $subject
        Thumbprint = $thumbprint
        TimestamperSubject = $timestamper
        PublisherMatched = ($subject -and $subject.Contains($script:ExpectedPublisher))
        TimestampPresent = [bool]($signature.TimeStamperCertificate)
    }
}

function Assert-ReleaseSignature {
    param(
        [Parameter(Mandatory = $true)] $Report,
        [Parameter(Mandatory = $true)][string] $VerifyOutput,
        [Parameter(Mandatory = $true)][string] $FileLabel
    )

    if ($Report.Status -ne 'Valid') {
        throw "$FileLabel Authenticode status is $($Report.Status). $($Report.StatusMessage)"
    }

    if (-not $Report.PublisherMatched) {
        throw "$FileLabel signer '$($Report.Subject)' does not contain $($script:ExpectedPublisher)."
    }

    if (-not $Report.TimestampPresent) {
        throw "$FileLabel signature has no trusted timestamp. Artifact Signing certificates are short-lived, so an untimestamped signature is not a release signature."
    }

    if ($VerifyOutput -notmatch 'Successfully verified') {
        throw "SignTool did not report that $FileLabel was successfully verified."
    }

    if ($VerifyOutput -notmatch 'sha256') {
        throw "SignTool verification output for $FileLabel does not show a SHA-256 signature."
    }
}

function Invoke-ArtifactSigning {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $MetadataPath,
        [Parameter(Mandatory = $true)][string] $SignToolPath,
        [Parameter(Mandatory = $true)][string] $DlibPath,
        [string] $TimestampUrl = $script:TimestampUrl
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "File to sign was not found: $FilePath"
    }

    $signArch = Get-PeArchitecture $SignToolPath
    $dlibArch = Get-PeArchitecture $DlibPath
    if ($signArch -ne $dlibArch) {
        throw "SignTool architecture $signArch does not match Azure.CodeSigning.Dlib.dll architecture $dlibArch."
    }

    $unsignedHash = Get-Sha256Hex $FilePath
    Write-Host "Signing $FilePath"
    Write-Host "Unsigned SHA-256: $unsignedHash"
    $sign = Invoke-SignToolCapture -SignToolPath $SignToolPath -ArgumentList @(
        'sign',
        '/v',
        '/debug',
        '/fd', 'SHA256',
        '/tr', $TimestampUrl,
        '/td', 'SHA256',
        '/dlib', $DlibPath,
        '/dmdf', $MetadataPath,
        $FilePath
    )

    if ($sign.ExitCode -ne 0) {
        throw "SignTool sign failed with exit code $($sign.ExitCode).`r`n$($sign.Output)"
    }

    $signedHash = Get-Sha256Hex $FilePath
    if ($signedHash -eq $unsignedHash) {
        throw "Signing did not change the SHA-256 of $FilePath. The signature was not embedded."
    }

    $verify = Invoke-SignToolCapture -SignToolPath $SignToolPath -ArgumentList @(
        'verify',
        '/pa',
        '/all',
        '/v',
        $FilePath
    )
    if ($verify.ExitCode -ne 0) {
        throw "SignTool verify /pa /all /v failed with exit code $($verify.ExitCode).`r`n$($verify.Output)"
    }

    $report = Get-AuthenticodeReport $FilePath
    Assert-ReleaseSignature -Report $report -VerifyOutput $verify.Output -FileLabel $FilePath
    return [pscustomobject]@{
        UnsignedSha256 = $unsignedHash
        SignedSha256 = $signedHash
        SignedSize = (Get-Item -LiteralPath $FilePath).Length
        SignOutput = $sign.Output
        VerifyOutput = $verify.Output
        Report = $report
    }
}

function Get-AttestationRelativePaths {
    return @(
        'EqlLoadouts.exe',
        'LICENSE',
        'CONTRIBUTOR_AGREEMENT.md',
        'evidence\release-provenance.json',
        'evidence\signature-verification.txt',
        'evidence\build-log.txt',
        'evidence\build-record.json',
        'evidence\signing-record.json',
        'evidence\artifact-signing.metadata.json',
        'evidence\artifact-signing.catalog-metadata.json',
        'evidence\unsigned-hash.txt'
    )
}

function New-AttestationCatalog {
    param(
        [Parameter(Mandatory = $true)][string] $ReleaseDirectory
    )

    $catalogRelative = 'evidence\release-attestation.cat'
    $covers = @(Get-AttestationRelativePaths)
    Push-Location $ReleaseDirectory
    try {
        foreach ($relative in $covers) {
            if (-not (Test-Path -LiteralPath $relative)) {
                throw "Cannot catalog the release because $relative is missing."
            }
        }

        $catalogPath = Join-Path $ReleaseDirectory $catalogRelative
        if (Test-Path -LiteralPath $catalogPath) {
            Remove-Item -LiteralPath $catalogPath -Force
        }

        New-FileCatalog -Path $covers -CatalogFilePath $catalogRelative -CatalogVersion 2.0 | Out-Null
        $check = Test-FileCatalog -Path $covers -CatalogFilePath $catalogRelative -Detailed
        if ("$($check.Status)" -ne 'Valid') {
            throw "The new attestation catalog does not match the release files. Status: $($check.Status)"
        }
    }
    finally {
        Pop-Location
    }

    return [pscustomobject]@{
        RelativePath = $catalogRelative
        FullPath = (Join-Path $ReleaseDirectory $catalogRelative)
        Covers = $covers
    }
}

function Invoke-ExistingPublish {
    param(
        [Parameter(Mandatory = $true)][string] $PublishScript,
        [Parameter(Mandatory = $true)][string] $Output,
        [Parameter(Mandatory = $true)][string] $LogPath,
        [Parameter(Mandatory = $true)][string] $DotNetDirectory
    )

    $wrapper = Join-Path ([System.IO.Path]::GetTempPath()) ("eql-publish-" + [guid]::NewGuid().ToString('N') + '.ps1')
    $wrapperText = @'
param(
    [Parameter(Mandatory = $true)][string] $PublishScript,
    [Parameter(Mandatory = $true)][string] $Output,
    [Parameter(Mandatory = $true)][string] $LogPath,
    [string] $DotNetDirectory
)
$ErrorActionPreference = 'Continue'
if (-not [string]::IsNullOrWhiteSpace($DotNetDirectory)) {
    $env:Path = $DotNetDirectory.TrimEnd('\') + ';' + $env:Path
}
& $PublishScript -Output $Output *>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $LogPath
$code = $LASTEXITCODE
if ($null -eq $code) { $code = 0 }
exit $code
'@
    Write-Utf8NoBom -Path $wrapper -Text $wrapperText
    try {
        $hostPath = (Get-Process -Id $PID).Path
        & $hostPath -NoProfile -ExecutionPolicy Bypass -File $wrapper -PublishScript $PublishScript -Output $Output -LogPath $LogPath -DotNetDirectory $DotNetDirectory
        $code = $LASTEXITCODE
        if ($code -ne 0) {
            throw "publish-win-x64.ps1 failed with exit code $code. The build log is $LogPath."
        }
    }
    finally {
        Remove-Item -LiteralPath $wrapper -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-ReleaseBuild {
    param(
        [Parameter(Mandatory = $true)][string] $Commit,
        [Parameter(Mandatory = $true)][string] $OutputDirectory,
        [ValidateSet('Release')]
        [string] $Configuration = 'Release',
        [string] $Repository
    )

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        $Repository = Get-RepositoryRoot
    }

    $Repository = [System.IO.Path]::GetFullPath($Repository)
    if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path (Get-Location).Path $OutputDirectory
    }

    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if ($Configuration -ne 'Release') {
        throw 'The reproducible publish is Release only. Debug builds are not deterministic.'
    }

    if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
        throw "Output directory is not empty: $OutputDirectory. Choose a new directory so this build cannot reuse an existing executable."
    }

    $info = Get-GitCommitInfo -Repository $Repository -Reference $Commit
    $sdk = Resolve-DotNetSdk
    $versionCheck = & $sdk.DotNetPath --version
    if ($LASTEXITCODE -ne 0 -or "$versionCheck".Trim() -ne $script:RequiredSdk) {
        throw "dotnet --version must be $($script:RequiredSdk) before publish. Resolved $($sdk.DotNetPath) reported '$versionCheck'."
    }

    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'eql-loadout-build' ($info.Sha + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)))
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'eql-loadout-build' ($info.Sha + '-out-' + [guid]::NewGuid().ToString('N').Substring(0, 8)))
    $evidence = Join-Path $OutputDirectory 'evidence'
    $buildLog = Join-Path $evidence 'build-log.txt'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $worktreeAdded = $false
    try {
        & git -C $Repository worktree add --detach $worktree $info.Sha
        if ($LASTEXITCODE -ne 0) {
            throw "git worktree add failed for $($info.Sha)."
        }

        $worktreeAdded = $true
        $status = & git -C $worktree status --porcelain
        if ($LASTEXITCODE -ne 0) {
            throw "git status failed in the worktree."
        }

        if (-not [string]::IsNullOrWhiteSpace("$status")) {
            throw "The isolated checkout is not clean, so the build would include unrecorded inputs: $status"
        }

        $head = (& git -C $worktree rev-parse HEAD).Trim()
        if ($head -ne $info.Sha) {
            throw "Isolated checkout HEAD $head does not match requested commit $($info.Sha)."
        }

        $publishScript = Join-Path $worktree 'publish-win-x64.ps1'
        if (-not (Test-Path -LiteralPath $publishScript)) {
            throw "The commit does not contain publish-win-x64.ps1."
        }

        $started = Get-UtcTimestamp
        Write-Host "Building $($info.Sha) with .NET SDK $($sdk.SdkVersion)"
        Write-Host "Isolated checkout: $worktree"
        Invoke-ExistingPublish -PublishScript $publishScript -Output $staging -LogPath $buildLog -DotNetDirectory $sdk.Directory
        $builtExe = Join-Path $staging $script:ShippedExeName
        if (-not (Test-Path -LiteralPath $builtExe)) {
            throw "The publish script did not produce $builtExe."
        }

        $unsignedHash = Get-Sha256Hex $builtExe
        $hashFile = Join-Path $staging 'hash.txt'
        if (-not (Test-Path -LiteralPath $hashFile)) {
            throw "The publish script did not write hash.txt."
        }

        $hashText = [System.IO.File]::ReadAllText($hashFile)
        if ($hashText -notmatch [regex]::Escape($unsignedHash)) {
            throw "hash.txt from publish-win-x64.ps1 does not contain the SHA-256 calculated for $script:ShippedExeName."
        }

        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
        Copy-Item -LiteralPath $builtExe -Destination (Join-Path $OutputDirectory $script:ShippedExeName)
        foreach ($companion in @('LICENSE', 'CONTRIBUTOR_AGREEMENT.md')) {
            $source = Join-Path $staging $companion
            if (-not (Test-Path -LiteralPath $source)) {
                throw "The publish script did not produce $companion, which the existing release layout includes."
            }

            Copy-Item -LiteralPath $source -Destination (Join-Path $OutputDirectory $companion)
        }

        Copy-Item -LiteralPath $hashFile -Destination (Join-Path $evidence 'unsigned-hash.txt')
        $version = Get-ProjectVersion (Join-Path $worktree 'src\EqlLoadouts.App\EqlLoadouts.App.csproj')
        $inputHashes = Get-InputHashSet $worktree
        $lockFiles = [ordered]@{
            packagesLock = $null
            note = 'The application and library projects have no PackageReference and the repository has no packages.lock.json. NuGet lock files are not part of the executable. Test-only package versions are in the hashed test project file and do not ship inside the executable.'
        }
        $stagingParent = Split-Path -Parent $staging
        $stagingName = Split-Path -Leaf $staging
        $artifactsPath = Join-Path $stagingParent ($stagingName + '.artifacts')
        $buildCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$publishScript`" -Output `"$staging`""
        $record = [ordered]@{
            schema = 'eql-build-record/1.0'
            recordedAtUtc = Get-UtcTimestamp
            git = [ordered]@{
                commitSha = $info.Sha
                shortSha = $info.ShortSha
                repository = $info.Repository
                commitTimestamp = $info.CommitTimestamp
                author = $info.Author
                authorName = $info.AuthorName
                authorEmail = $info.AuthorEmail
                subject = $info.Subject
                releaseTag = $info.ReleaseTag
                releaseTags = @($info.ReleaseTags)
            }
            build = [ordered]@{
                applicationName = $script:ApplicationName
                version = $version
                sdkVersion = $sdk.SdkVersion
                dotnetPath = $sdk.DotNetPath
                configuration = 'Release'
                targetRuntime = 'win-x64'
                architecture = 'x64'
                selfContained = $true
                singleFile = $true
                buildCommand = $buildCommand
                innerCommands = @(
                    "dotnet test tests\EqlLoadouts.Core.Tests\EqlLoadouts.Core.Tests.csproj -c Release --nologo",
                    "dotnet publish src\EqlLoadouts.App\EqlLoadouts.App.csproj -c Release -o `"$staging`" --artifacts-path `"$artifactsPath`" -p:EqlSingleFilePublish=true"
                )
                buildTimestampUtc = $started
                dependencyHashes = $inputHashes
                lockFiles = $lockFiles
                deterministicBuild = [ordered]@{
                    status = 'configured'
                    verifiedByThisBuild = $false
                    publishScript = 'publish-win-x64.ps1'
                    property = 'EqlSingleFilePublish=true'
                    sdkPinned = $script:RequiredSdk
                    rollForward = 'disable'
                    explanation = 'Directory.Build.props enables Deterministic, ContinuousIntegrationBuild, and fixed source paths only when EqlSingleFilePublish is true. This build called the existing publish script and did not change those settings. A matching unsigned hash is established only by a later rebuild comparison.'
                }
                environment = [ordered]@{
                    windows = Get-WindowsDescription
                    powershell = $PSVersionTable.PSVersion.ToString()
                    gitVersion = "$((& git --version) | Out-String)".Trim()
                    coreAutocrlf = $info.CoreAutoCrlf
                    coreEol = $info.CoreEol
                }
            }
            executable = [ordered]@{
                fileName = $script:ShippedExeName
                unsignedSha256 = $unsignedHash
                unsignedSize = (Get-Item -LiteralPath $builtExe).Length
            }
            companionFiles = @(
                foreach ($companion in @('LICENSE', 'CONTRIBUTOR_AGREEMENT.md')) {
                    $path = Join-Path $OutputDirectory $companion
                    [ordered]@{
                        fileName = $companion
                        sha256 = (Get-Sha256Hex $path)
                        size = (Get-Item -LiteralPath $path).Length
                        role = $(if ($companion -eq 'LICENSE') {
                            'Existing publish output. The splash screen opens LICENSE from the executable directory. It is not added for signing.'
                        }
                        else {
                            'Existing publish output copied beside the executable by publish-win-x64.ps1. It is not added for signing.'
                        })
                    }
                }
            )
        }

        Write-JsonFile -Path (Join-Path $evidence 'build-record.json') -Value $record
        Write-Host "Unsigned $($script:ShippedExeName) SHA-256: $unsignedHash"
        Write-Host "Build output: $OutputDirectory"
        return ,$record
    }
    finally {
        if ($worktreeAdded) {
            & git -C $Repository worktree remove --force $worktree 2>&1 | Out-Null
            if (Test-Path -LiteralPath $worktree) {
                Remove-Item -LiteralPath $worktree -Recurse -Force -ErrorAction SilentlyContinue
            }

            & git -C $Repository worktree prune 2>&1 | Out-Null
        }

        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ReleaseSign {
    param(
        [Parameter(Mandatory = $true)][string] $ExePath,
        [string] $EvidenceDirectory,
        [string] $CorrelationId,
        [string] $ConfigPath
    )

    if (-not [System.IO.Path]::IsPathRooted($ExePath)) {
        $ExePath = Join-Path (Get-Location).Path $ExePath
    }

    $ExePath = [System.IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "Executable was not found: $ExePath"
    }

    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        $EvidenceDirectory = Join-Path (Split-Path -Parent $ExePath) 'evidence'
    }

    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    $config = Read-SigningConfig $ConfigPath
    $tools = Resolve-SigningToolPair
    if ([string]::IsNullOrWhiteSpace($CorrelationId)) {
        $CorrelationId = New-ReleaseCorrelationId 'manual'
    }

    $metadataPath = Join-Path $EvidenceDirectory 'artifact-signing.metadata.json'
    Write-SigningMetadata -Path $metadataPath -Config $config -CorrelationId $CorrelationId | Out-Null
    $parent = Split-Path -Parent $ExePath
    $beforeSign = @(Get-ChildItem -LiteralPath $parent -Force | Select-Object -ExpandProperty Name)
    $started = Get-UtcTimestamp
    $signed = Invoke-ArtifactSigning -FilePath $ExePath -MetadataPath $metadataPath -SignToolPath $tools.SignToolPath -DlibPath $tools.DlibPath -TimestampUrl $(if ($config.timestampUrl) { "$($config.timestampUrl)" } else { $script:TimestampUrl })
    $verifyPath = Join-Path $EvidenceDirectory 'signature-verification.txt'
    Write-Utf8NoBom -Path $verifyPath -Text ($signed.VerifyOutput + "`r`n")
    $record = [ordered]@{
        schema = 'eql-signing-record/1.0'
        recordedAtUtc = Get-UtcTimestamp
        fileName = [System.IO.Path]::GetFileName($ExePath)
        unsignedSha256 = $signed.UnsignedSha256
        signedSha256 = $signed.SignedSha256
        signedSize = $signed.SignedSize
        correlationId = $CorrelationId
        accountName = "$($config.accountName)"
        certificateProfileName = "$($config.certificateProfileName)"
        endpoint = "$($config.endpoint)"
        timestampUrl = $(if ($config.timestampUrl) { "$($config.timestampUrl)" } else { $script:TimestampUrl })
        signingTimestampUtc = $started
        signToolPath = $tools.SignToolPath
        signToolVersion = $tools.SignToolVersion
        signToolArchitecture = $tools.SignToolArchitecture
        dlibPath = $tools.DlibPath
        dlibArchitecture = $tools.DlibArchitecture
        authenticode = [ordered]@{
            result = 'verified'
            subject = $signed.Report.Subject
            thumbprint = $signed.Report.Thumbprint
            publisher = $script:ExpectedPublisher
            publisherMatched = [bool]$signed.Report.PublisherMatched
            status = $signed.Report.Status
        }
        timestamp = [ordered]@{
            result = 'verified'
            timestamperSubject = $signed.Report.TimestamperSubject
            timestampUrl = $(if ($config.timestampUrl) { "$($config.timestampUrl)" } else { $script:TimestampUrl })
        }
    }

    Write-JsonFile -Path (Join-Path $EvidenceDirectory 'signing-record.json') -Value $record
    $afterSign = @(Get-ChildItem -LiteralPath $parent -Force | Select-Object -ExpandProperty Name)
    foreach ($name in $afterSign) {
        if ($beforeSign -notcontains $name) {
            throw "Signing created an unexpected file beside the executable: $name. The release must remain a single executable. Evidence stays in the evidence folder."
        }
    }

    Write-Host "Signed SHA-256: $($signed.SignedSha256)"
    Write-Host "Publisher: $($signed.Report.Subject)"
    Write-Host "Timestamp: $($signed.Report.TimestamperSubject)"
    return ,$record
}

function New-ReleaseProvenance {
    param(
        [Parameter(Mandatory = $true)] $BuildRecord,
        [Parameter(Mandatory = $true)] $SigningRecord,
        [Parameter(Mandatory = $true)][string] $ReleaseCorrelationId,
        [Parameter(Mandatory = $true)][string] $CatalogCorrelationId,
        [Parameter(Mandatory = $true)][string[]] $Covers,
        [Parameter(Mandatory = $true)][string] $AuthenticationStatus,
        [string] $AuthenticationStatement
    )

    if ([string]::IsNullOrWhiteSpace($AuthenticationStatement)) {
        if ($AuthenticationStatus -eq 'authenticode-catalog') {
            $AuthenticationStatement = 'This status is a release-time claim. Verify-Release.ps1 establishes it by validating the Authenticode signature on evidence\release-attestation.cat and the catalog hashes of the covered files. SignTool does not Authenticode-sign the JSON file directly. The catalog is signed with the same Azure Artifact Signing public-trust profile as the executable.'
        }
        else {
            $AuthenticationStatement = 'This manifest is unauthenticated metadata. The hashes below are assertions written by the release scripts and can be edited. No trusted signature covers this file. Do not treat it as independently authenticated provenance.'
        }
    }

    return ,[ordered]@{
        schema = 'eql-release-provenance/1.0'
        generatedAtUtc = Get-UtcTimestamp
        description = 'Release evidence for EQL Loadout Editor. recorded contains assertions written by the release scripts. verified contains checks those scripts performed on the release machine; Verify-Release.ps1 repeats them. A hash in this file is not independent proof until authentication.status is validated.'
        factBoundary = [ordered]@{
            recordedAssertions = @(
                'recorded.git',
                'recorded.build',
                'recorded.executable.unsignedSha256',
                'recorded.azure'
            )
            releaseMachineChecks = @(
                'verified.authenticode',
                'verified.timestamp',
                'recorded.executable.signedSha256'
            )
            independentChecks = 'Verify-Release.ps1 recomputes the signed hash, reruns SignTool verification, validates the attestation catalog when present, and with -RebuildFromCommit rebuilds the unsigned executable from the recorded commit.'
        }
        recorded = [ordered]@{
            git = [ordered]@{
                commitSha = $BuildRecord.git.commitSha
                repository = $BuildRecord.git.repository
                commitTimestamp = $BuildRecord.git.commitTimestamp
                author = $BuildRecord.git.author
                subject = $BuildRecord.git.subject
                releaseTag = $BuildRecord.git.releaseTag
                releaseTags = @($BuildRecord.git.releaseTags)
            }
            build = [ordered]@{
                applicationName = $BuildRecord.build.applicationName
                version = $BuildRecord.build.version
                sdkVersion = $BuildRecord.build.sdkVersion
                configuration = $BuildRecord.build.configuration
                targetRuntime = $BuildRecord.build.targetRuntime
                architecture = $BuildRecord.build.architecture
                buildCommand = $BuildRecord.build.buildCommand
                innerCommands = @($BuildRecord.build.innerCommands)
                buildTimestampUtc = $BuildRecord.build.buildTimestampUtc
                dependencyHashes = $BuildRecord.build.dependencyHashes
                lockFiles = $BuildRecord.build.lockFiles
                deterministicBuild = $BuildRecord.build.deterministicBuild
                environment = $BuildRecord.build.environment
            }
            executable = [ordered]@{
                fileName = $script:ShippedExeName
                unsignedSha256 = $SigningRecord.unsignedSha256
                signedSha256 = $SigningRecord.signedSha256
                unsignedSize = $BuildRecord.executable.unsignedSize
                fileSize = $SigningRecord.signedSize
            }
            azure = [ordered]@{
                account = $SigningRecord.accountName
                certificateProfile = $SigningRecord.certificateProfileName
                endpoint = $SigningRecord.endpoint
                correlationId = $ReleaseCorrelationId
                exeCorrelationId = $SigningRecord.correlationId
                catalogCorrelationId = $CatalogCorrelationId
                signingTimestampUtc = $SigningRecord.signingTimestampUtc
                timestampUrl = $SigningRecord.timestampUrl
            }
        }
        verified = [ordered]@{
            checkedAtUtc = $SigningRecord.recordedAtUtc
            note = 'These results were produced on the release machine by SignTool verify /pa /all /v and Get-AuthenticodeSignature. They are release-machine checks, and Verify-Release.ps1 must repeat them.'
            authenticode = [ordered]@{
                result = $SigningRecord.authenticode.result
                publisher = $SigningRecord.authenticode.publisher
                subject = $SigningRecord.authenticode.subject
                thumbprint = $SigningRecord.authenticode.thumbprint
            }
            timestamp = [ordered]@{
                result = $SigningRecord.timestamp.result
                timestamperSubject = $SigningRecord.timestamp.timestamperSubject
                timestampUrl = $SigningRecord.timestamp.timestampUrl
            }
        }
        authentication = [ordered]@{
            status = $AuthenticationStatus
            method = $(if ($AuthenticationStatus -eq 'authenticode-catalog') { 'Authenticode catalog signed by Azure Artifact Signing' } else { $null })
            catalogFile = $(if ($AuthenticationStatus -eq 'authenticode-catalog') { 'evidence\release-attestation.cat' } else { $null })
            covers = @($Covers)
            statement = $AuthenticationStatement
        }
    }
}

function Write-ProvenanceFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $Provenance
    )

    $json = ConvertTo-ReleaseJson $Provenance
    if ($json -notmatch '"covers"\s*:\s*\[') {
        throw 'The provenance covers field was not written as a JSON array.'
    }

    if (-not $json.EndsWith("`n")) {
        $json += "`r`n"
    }

    Write-Utf8NoBom -Path $Path -Text $json
}

function Set-ProvenanceUnauthenticated {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Reason
    )

    $provenance = Read-JsonFile $Path
    $provenance.authentication.status = 'unauthenticated'
    $provenance.authentication.method = $null
    $provenance.authentication.catalogFile = $null
    $provenance.authentication.statement = "This manifest is unauthenticated metadata. $Reason The hashes in this file are editable assertions."
    Write-ProvenanceFile -Path $Path -Provenance $provenance
}

function New-ReleaseZip {
    param(
        [Parameter(Mandatory = $true)][string] $ReleaseDirectory
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $ReleaseDirectory = [System.IO.Path]::GetFullPath($ReleaseDirectory.TrimEnd('\', '/'))
    if (-not (Test-Path -LiteralPath $ReleaseDirectory -PathType Container)) {
        throw "Release directory was not found: $ReleaseDirectory"
    }

    $parent = Split-Path -Parent $ReleaseDirectory
    $name = Split-Path -Leaf $ReleaseDirectory
    $zipPath = Join-Path $parent ($name + '.zip')
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    # The zip file name is the release name. The archive holds the directory contents, with evidence beside the executable.
    [System.IO.Compression.ZipFile]::CreateFromDirectory($ReleaseDirectory, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw "The release zip was not created: $zipPath"
    }

    return $zipPath
}

function Invoke-SignedRelease {
    param(
        [Parameter(Mandatory = $true)][string] $Commit,
        [string] $OutputRoot,
        [ValidateSet('Release')]
        [string] $Configuration = 'Release',
        [switch] $CreateSignedTag,
        [string] $Repository
    )

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        $Repository = Get-RepositoryRoot
    }

    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $OutputRoot = Join-Path $Repository 'releases'
    }
    elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
        $OutputRoot = Join-Path (Get-Location).Path $OutputRoot
    }

    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
    $info = Get-GitCommitInfo -Repository $Repository -Reference $Commit
    $version = Get-VersionAtCommit -Repository $Repository -Sha $info.Sha
    $releaseDirectory = Join-Path $OutputRoot ("EqlLoadouts-{0}-{1}" -f $version, $info.ShortSha)
    Write-Host "Release directory: $releaseDirectory"
    $build = Invoke-ReleaseBuild -Commit $info.Sha -OutputDirectory $releaseDirectory -Configuration $Configuration -Repository $Repository
    $releaseCorrelationId = New-ReleaseCorrelationId $info.ShortSha
    $exeCorrelationId = "${releaseCorrelationId}:exe"
    $catalogCorrelationId = "${releaseCorrelationId}:catalog"
    $exePath = Join-Path $releaseDirectory $script:ShippedExeName
    $evidence = Join-Path $releaseDirectory 'evidence'
    $signing = Invoke-ReleaseSign -ExePath $exePath -EvidenceDirectory $evidence -CorrelationId $exeCorrelationId
    if ($signing.unsignedSha256 -ne $build.executable.unsignedSha256) {
        throw "The unsigned hash recorded at build time does not match the hash Sign-Release calculated before signing."
    }

    $config = Read-SigningConfig
    $covers = @(Get-AttestationRelativePaths)
    $provenance = New-ReleaseProvenance -BuildRecord $build -SigningRecord $signing -ReleaseCorrelationId $releaseCorrelationId -CatalogCorrelationId $catalogCorrelationId -Covers $covers -AuthenticationStatus 'authenticode-catalog'
    $provenancePath = Join-Path $evidence 'release-provenance.json'
    Write-ProvenanceFile -Path $provenancePath -Provenance $provenance
    Write-SigningMetadata -Path (Join-Path $evidence 'artifact-signing.catalog-metadata.json') -Config $config -CorrelationId $catalogCorrelationId | Out-Null
    $catalog = $null
    try {
        $catalog = New-AttestationCatalog -ReleaseDirectory $releaseDirectory
        $tools = Resolve-SigningToolPair
        $catalogSigned = Invoke-ArtifactSigning -FilePath $catalog.FullPath -MetadataPath (Join-Path $evidence 'artifact-signing.catalog-metadata.json') -SignToolPath $tools.SignToolPath -DlibPath $tools.DlibPath
        Write-Utf8NoBom -Path (Join-Path $evidence 'catalog-signature-verification.txt') -Text ($catalogSigned.VerifyOutput + "`r`n")
        $catalogCheck = Test-ReleaseCatalog -ReleaseDirectory $releaseDirectory -Provenance (Read-JsonFile $provenancePath)
        if (-not $catalogCheck.HashValid -or -not $catalogCheck.SignatureValid) {
            throw "The signed attestation catalog failed its own verification. $($catalogCheck.Message)"
        }
    }
    catch {
        $reason = $_.Exception.Message
        if ($catalog -and (Test-Path -LiteralPath $catalog.FullPath)) {
            Remove-Item -LiteralPath $catalog.FullPath -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $provenancePath) {
            Set-ProvenanceUnauthenticated -Path $provenancePath -Reason "Catalog signing did not finish. $reason"
        }

        throw "The executable was signed, but the provenance catalog was not authenticated. The manifest is marked unauthenticated. $reason"
    }

    $zipPath = New-ReleaseZip -ReleaseDirectory $releaseDirectory
    if ($CreateSignedTag) {
        New-SignedReleaseTag -Repository $Repository -CommitSha $info.Sha -Version $version -ShortSha $info.ShortSha -ProvenancePath $provenancePath -UnsignedSha256 $signing.unsignedSha256 -SignedSha256 $signing.signedSha256
    }

    Write-Host ""
    Write-Host "Signed release: $releaseDirectory"
    Write-Host "Release zip: $zipPath"
    Write-Host "Commit: $($info.Sha)"
    Write-Host "Unsigned SHA-256: $($signing.unsignedSha256)"
    Write-Host "Signed SHA-256: $($signing.signedSha256)"
    Write-Host "Provenance: $provenancePath"
    Write-Host "Attestation: $($catalog.FullPath)"
    return $releaseDirectory
}

function New-SignedReleaseTag {
    param(
        [Parameter(Mandatory = $true)][string] $Repository,
        [Parameter(Mandatory = $true)][string] $CommitSha,
        [Parameter(Mandatory = $true)][string] $Version,
        [Parameter(Mandatory = $true)][string] $ShortSha,
        [Parameter(Mandatory = $true)][string] $ProvenancePath,
        [Parameter(Mandatory = $true)][string] $UnsignedSha256,
        [Parameter(Mandatory = $true)][string] $SignedSha256
    )

    $key = & git -C $Repository config --get user.signingkey 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$key")) {
        throw 'No Git signing key is configured (git config user.signingkey). Refusing to create an unsigned tag and call it provenance.'
    }

    $tag = "release/eql-loadouts-$Version-$ShortSha"
    $existing = & git -C $Repository rev-parse -q --verify "refs/tags/$tag" 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw "Git tag $tag already exists. It was not replaced."
    }

    $provenanceHash = Get-Sha256Hex $ProvenancePath
    $message = @"
EQL Loadout Editor $Version

commit: $CommitSha
unsigned-sha256: $UnsignedSha256
signed-sha256: $SignedSha256
provenance-sha256: $provenanceHash
"@
    & git -C $Repository tag -s $tag -m $message $CommitSha
    if ($LASTEXITCODE -ne 0) {
        throw "git tag -s failed for $tag. The release files were left in place and the tag was not pushed."
    }

    Write-Host "Signed Git tag: $tag"
    Write-Host "The tag is local. Push it explicitly if you want it on the remote."
}

function Test-ReleaseCatalog {
    param(
        [Parameter(Mandatory = $true)][string] $ReleaseDirectory,
        [Parameter(Mandatory = $true)] $Provenance
    )

    $result = [ordered]@{
        HashValid = $false
        SignatureValid = $false
        PublisherMatched = $false
        TimestampPresent = $false
        Subject = $null
        Thumbprint = $null
        Message = $null
    }

    $catalogRelative = "$($Provenance.authentication.catalogFile)"
    if ([string]::IsNullOrWhiteSpace($catalogRelative)) {
        $result.Message = 'The provenance does not name an attestation catalog.'
        return [pscustomobject]$result
    }

    $catalogPath = Join-Path $ReleaseDirectory $catalogRelative
    if (-not (Test-Path -LiteralPath $catalogPath)) {
        $result.Message = "Attestation catalog was not found: $catalogPath"
        return [pscustomobject]$result
    }

    $covers = @(ConvertTo-ObjectList $Provenance.authentication.covers | ForEach-Object { "$_" })
    Push-Location $ReleaseDirectory
    try {
        foreach ($relative in $covers) {
            if (-not (Test-Path -LiteralPath $relative)) {
                $result.Message = "Catalog member is missing: $relative"
                return [pscustomobject]$result
            }
        }

        $hashCheck = Test-FileCatalog -Path $covers -CatalogFilePath $catalogRelative -Detailed
        $result.HashValid = ("$($hashCheck.Status)" -eq 'Valid')
        if (-not $result.HashValid) {
            $result.Message = "Catalog hash validation status is $($hashCheck.Status)."
        }
    }
    finally {
        Pop-Location
    }

    $signTool = Resolve-SignToolPath
    $verify = Invoke-SignToolCapture -SignToolPath $signTool -ArgumentList @('verify', '/pa', '/all', '/v', $catalogPath)
    $report = Get-AuthenticodeReport $catalogPath
    $result.Subject = $report.Subject
    $result.Thumbprint = $report.Thumbprint
    $result.PublisherMatched = [bool]$report.PublisherMatched
    $result.TimestampPresent = [bool]$report.TimestampPresent
    $result.SignatureValid = ($verify.ExitCode -eq 0 -and $report.Status -eq 'Valid' -and $report.PublisherMatched -and $report.TimestampPresent -and $verify.Output -match 'Successfully verified')
    if (-not $result.SignatureValid) {
        $result.Message = "Catalog Authenticode verification failed. SignTool exit $($verify.ExitCode). Status $($report.Status). $($report.StatusMessage) $($verify.Output)"
    }
    elseif (-not $result.HashValid) {
        $result.Message = 'Catalog signature is valid, but the covered file hashes do not match the catalog.'
    }
    else {
        $result.Message = 'Catalog signature and covered file hashes are valid.'
    }

    return [pscustomobject]$result
}

function Get-ReproducibleBuildBanner {
    return "REPRODUCIBLE BUILD VERIFIED $([char]0x2014) The specified Git commit produced an unsigned executable whose SHA-256 hash matches the original recorded build."
}

function Invoke-ReleaseVerify {
    param(
        [Parameter(Mandatory = $true)][string] $ReleaseDirectory,
        [switch] $RebuildFromCommit,
        [string] $Repository
    )

    if (-not [System.IO.Path]::IsPathRooted($ReleaseDirectory)) {
        $ReleaseDirectory = Join-Path (Get-Location).Path $ReleaseDirectory
    }

    $ReleaseDirectory = [System.IO.Path]::GetFullPath($ReleaseDirectory)
    $provenancePath = Join-Path $ReleaseDirectory 'evidence\release-provenance.json'
    $provenance = Read-JsonFile $provenancePath
    $failures = New-Object System.Collections.Generic.List[string]
    $exeName = "$($provenance.recorded.executable.fileName)"
    if ([string]::IsNullOrWhiteSpace($exeName)) {
        $exeName = $script:ShippedExeName
    }

    $exePath = Join-Path $ReleaseDirectory $exeName
    Write-Host 'RECORDED PROVENANCE'
    Write-Host "  Commit: $($provenance.recorded.git.commitSha)"
    Write-Host "  Subject: $($provenance.recorded.git.subject)"
    Write-Host "  Unsigned SHA-256: $($provenance.recorded.executable.unsignedSha256)"
    Write-Host "  Signed SHA-256: $($provenance.recorded.executable.signedSha256)"
    Write-Host "  Authentication claim: $($provenance.authentication.status)"
    Write-Host ''
    Write-Host 'INDEPENDENT CHECKS'

    if (-not (Test-Path -LiteralPath $exePath)) {
        $failures.Add("Executable was not found: $exePath")
        Write-Host "  Signed file: MISSING"
    }
    else {
        $actualSigned = Get-Sha256Hex $exePath
        $recordedSigned = "$($provenance.recorded.executable.signedSha256)".ToUpperInvariant()
        if ($actualSigned -eq $recordedSigned) {
            Write-Host "  Signed SHA-256: MATCH $actualSigned"
        }
        else {
            $failures.Add("Signed SHA-256 mismatch. File $actualSigned. Recorded $recordedSigned.")
            Write-Host "  Signed SHA-256: MISMATCH"
            Write-Host "    file:     $actualSigned"
            Write-Host "    recorded: $recordedSigned"
        }

        $signTool = $null
        try {
            $signTool = Resolve-SignToolPath
        }
        catch {
            $failures.Add($_.Exception.Message)
            Write-Host "  SignTool: MISSING"
        }

        if ($signTool) {
            $verify = Invoke-SignToolCapture -SignToolPath $signTool -ArgumentList @('verify', '/pa', '/all', '/v', $exePath)
            $report = Get-AuthenticodeReport $exePath
            $signatureOk = ($verify.ExitCode -eq 0 -and $report.Status -eq 'Valid' -and $report.PublisherMatched -and $verify.Output -match 'Successfully verified' -and $verify.Output -match 'sha256')
            if ($signatureOk) {
                Write-Host "  Authenticode: verified"
                Write-Host "  Publisher: $($report.Subject)"
                Write-Host "  Thumbprint: $($report.Thumbprint)"
            }
            else {
                $detail = if ($report.Status -eq 'NotSigned') {
                    'The file has no Authenticode signature.'
                }
                else {
                    "Status $($report.Status). $($report.StatusMessage)"
                }

                $failures.Add("Authenticode verification failed for $exeName. $detail")
                Write-Host "  Authenticode: FAILED ($($report.Status))"
                if ($report.Subject) {
                    Write-Host "  Signer: $($report.Subject)"
                }
            }

            if ($report.TimestampPresent -and $signatureOk) {
                Write-Host "  Timestamp: verified ($($report.TimestamperSubject))"
            }
            else {
                $failures.Add('The trusted timestamp was not verified.')
                Write-Host '  Timestamp: FAILED'
            }
        }
    }

    $authStatus = "$($provenance.authentication.status)"
    $catalogAuthenticated = $false
    if ($authStatus -eq 'authenticode-catalog') {
        try {
            $catalog = Test-ReleaseCatalog -ReleaseDirectory $ReleaseDirectory -Provenance $provenance
            if ($catalog.HashValid -and $catalog.SignatureValid) {
                $catalogAuthenticated = $true
                Write-Host '  Provenance catalog: AUTHENTICATED'
                Write-Host "  Catalog publisher: $($catalog.Subject)"
            }
            else {
                $failures.Add("Authenticated provenance failed. $($catalog.Message)")
                Write-Host '  Provenance catalog: FAILED'
                Write-Host "  $($catalog.Message)"
            }
        }
        catch {
            $failures.Add("Authenticated provenance failed. $($_.Exception.Message)")
            Write-Host "  Provenance catalog: FAILED $($_.Exception.Message)"
        }
    }
    elseif ($authStatus -eq 'unauthenticated') {
        Write-Host '  Provenance catalog: UNAUTHENTICATED METADATA'
        Write-Host '  The manifest hashes are editable assertions. This mode verified the signed file only.'
    }
    else {
        $failures.Add("Unknown provenance authentication status '$authStatus'.")
        Write-Host "  Provenance catalog: UNKNOWN STATUS $authStatus"
    }

    $gitTagAuthenticated = Test-SignedReleaseTags -Provenance $provenance -Repository $Repository -ReleaseDirectory $ReleaseDirectory
    if ($gitTagAuthenticated.Checked) {
        if ($gitTagAuthenticated.Valid) {
            Write-Host "  Signed Git tag: verified $($gitTagAuthenticated.Tag)"
        }
        else {
            $failures.Add($gitTagAuthenticated.Message)
            Write-Host "  Signed Git tag: FAILED $($gitTagAuthenticated.Message)"
        }
    }

    Write-Host ''
    Write-Host 'SOURCE TO BINARY'
    if (-not $RebuildFromCommit) {
        Write-Host '  Not checked. The signed hash and Authenticode checks do not prove which commit produced the unsigned executable.'
        Write-Host '  Re-run with -RebuildFromCommit to build that commit and compare the unsigned SHA-256.'
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Repository)) {
            $Repository = Get-RepositoryRoot
        }

        $recordedSha = "$($provenance.recorded.git.commitSha)".Trim()
        $recordedUnsigned = "$($provenance.recorded.executable.unsignedSha256)".Trim().ToUpperInvariant()
        $recordedSdk = "$($provenance.recorded.build.sdkVersion)".Trim()
        try {
            $currentSdk = Resolve-DotNetSdk
        }
        catch {
            $failures.Add($_.Exception.Message)
            Write-Host "  SDK: $($_.Exception.Message)"
            $currentSdk = $null
        }

        if ($currentSdk -and $currentSdk.SdkVersion -ne $recordedSdk) {
            $message = "SDK mismatch. This machine resolved $($currentSdk.SdkVersion) and the provenance records $recordedSdk. The rebuild was not started."
            $failures.Add($message)
            Write-Host "  $message"
        }
        elseif ($currentSdk) {
            if ($recordedSha -notmatch '^[0-9a-fA-F]{40}$') {
                $failures.Add("Recorded commit SHA '$recordedSha' is not a full SHA.")
            }
            else {
                try {
                    Resolve-GitCommit -Repository $Repository -Reference $recordedSha | Out-Null
                }
                catch {
                    $failures.Add($_.Exception.Message)
                    Write-Host "  Commit: $($_.Exception.Message)"
                    $recordedSha = $null
                }
            }

            if ($recordedSha) {
                $rebuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'eql-loadout-verify' ([guid]::NewGuid().ToString('N')))
                Write-Host "  Rebuilding $recordedSha"
                try {
                    $rebuilt = Invoke-ReleaseBuild -Commit $recordedSha -OutputDirectory $rebuildRoot -Repository $Repository
                    $rebuiltHash = "$($rebuilt.executable.unsignedSha256)".ToUpperInvariant()
                    $inputDifferences = @(Compare-InputHashes -Recorded $provenance.recorded.build.dependencyHashes -Actual $rebuilt.build.dependencyHashes)
                    if ($inputDifferences.Count -gt 0) {
                        $failures.Add('Recorded build-input hashes do not match the files at the recorded commit.')
                        Write-Host '  Build inputs: DIFFER'
                        foreach ($difference in $inputDifferences) {
                            Write-Host "    $difference"
                        }
                    }
                    else {
                        Write-Host '  Build inputs: match the recorded project-file hashes'
                    }

                    $recordedAuto = "$($provenance.recorded.build.environment.coreAutocrlf)"
                    $actualAuto = "$($rebuilt.build.environment.coreAutocrlf)"
                    if ($recordedAuto -and $actualAuto -and $recordedAuto -ne $actualAuto) {
                        $failures.Add("core.autocrlf differs. Recorded '$recordedAuto'. This rebuild '$actualAuto'.")
                        Write-Host "  core.autocrlf: recorded $recordedAuto, rebuild $actualAuto"
                    }

                    $environmentDiffers = $false
                    if ($recordedAuto -and $actualAuto -and $recordedAuto -ne $actualAuto) {
                        $environmentDiffers = $true
                    }

                    $hashMatches = $rebuiltHash -eq $recordedUnsigned
                    if ($hashMatches -and $inputDifferences.Count -eq 0 -and -not $environmentDiffers) {
                        Write-Host ''
                        Write-Host (Get-ReproducibleBuildBanner)
                        if (-not $catalogAuthenticated) {
                            Write-Host 'The unsigned hash remains an editable assertion because the provenance manifest is not authenticated.'
                        }
                    }
                    elseif (-not $hashMatches) {
                        $failures.Add("Unsigned rebuild hash $rebuiltHash does not match recorded unsigned hash $recordedUnsigned.")
                        Write-Host '  REBUILD MISMATCH'
                        Write-Host "    rebuilt:  $rebuiltHash"
                        Write-Host "    recorded: $recordedUnsigned"
                        Write-Host "    sdk:      $($rebuilt.build.sdkVersion)"
                    }
                    else {
                        Write-Host '  Unsigned SHA-256 matched, but the reproducible-build result is withheld because build inputs or core.autocrlf differ.'
                    }

                    if ($hashMatches -and $failures.Count -eq 0) {
                        Remove-Item -LiteralPath $rebuildRoot -Recurse -Force -ErrorAction SilentlyContinue
                    }
                    elseif (Test-Path -LiteralPath $rebuildRoot) {
                        Write-Host "  Rebuild kept at $rebuildRoot"
                    }
                }
                catch {
                    $failures.Add("Rebuild failed. $($_.Exception.Message)")
                    Write-Host "  Rebuild failed: $($_.Exception.Message)"
                    if (Test-Path -LiteralPath $rebuildRoot) {
                        Write-Host "  Partial rebuild left at $rebuildRoot"
                    }
                }
            }
        }
    }

    Write-Host ''
    if ($failures.Count -gt 0) {
        Write-Host 'VERIFICATION FAILED'
        foreach ($failure in $failures) {
            Write-Host "  $failure"
        }

        throw "Release verification failed with $($failures.Count) error(s)."
    }

    if ($catalogAuthenticated) {
        Write-Host 'Verification completed. The signed executable, its publisher, its timestamp, and the authenticated provenance catalog were checked.'
    }
    else {
        Write-Host 'Verification completed for the signed executable. The provenance manifest is unauthenticated metadata, so the recorded commit and unsigned hash were not independently sealed.'
    }
}

function Get-HashMapEntries {
    param($Value)

    $map = @{}
    if ($null -eq $Value) {
        return ,$map
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in @($Value.Keys)) {
            $map["$key"] = "$($Value[$key])".ToUpperInvariant()
        }

        return ,$map
    }

    foreach ($property in @($Value.PSObject.Properties)) {
        $map[$property.Name] = "$($property.Value)".ToUpperInvariant()
    }

    return ,$map
}

function Compare-InputHashes {
    param(
        $Recorded,
        $Actual
    )

    $differences = New-Object System.Collections.Generic.List[string]
    $recordedMap = Get-HashMapEntries $Recorded
    $actualMap = Get-HashMapEntries $Actual

    foreach ($name in $recordedMap.Keys) {
        if (-not $actualMap.ContainsKey($name)) {
            $differences.Add("Missing at commit: $name")
            continue
        }

        if ($recordedMap[$name] -ne $actualMap[$name]) {
            $differences.Add("$name recorded $($recordedMap[$name]) rebuilt $($actualMap[$name])")
        }
    }

    foreach ($name in $actualMap.Keys) {
        if (-not $recordedMap.ContainsKey($name)) {
            $differences.Add("Present at commit but not recorded: $name")
        }
    }

    return $differences
}

function Test-SignedReleaseTags {
    param(
        $Provenance,
        [string] $Repository,
        [string] $ReleaseDirectory
    )

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        try {
            $Repository = Get-RepositoryRoot
        }
        catch {
            return [pscustomobject]@{ Checked = $false; Valid = $false; Tag = $null; Message = $null }
        }
    }

    $sha = "$($Provenance.recorded.git.commitSha)"
    $provenancePath = Join-Path $ReleaseDirectory 'evidence\release-provenance.json'
    if (-not (Test-Path -LiteralPath $provenancePath)) {
        return [pscustomobject]@{ Checked = $false; Valid = $false; Tag = $null; Message = $null }
    }

    $provenanceHash = Get-Sha256Hex $provenancePath
    $tags = @(& git -C $Repository tag --points-at $sha 2>&1)
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Checked = $false; Valid = $false; Tag = $null; Message = $null }
    }

    foreach ($tag in @($tags)) {
        $name = "$tag".Trim()
        if ([string]::IsNullOrWhiteSpace($name) -or $name -match '^(fatal|error):') {
            continue
        }

        $message = & git -C $Repository for-each-ref --format='%(contents)' "refs/tags/$name" 2>&1
        $text = ($message | Out-String)
        if ($text -notmatch [regex]::Escape($provenanceHash)) {
            continue
        }

        & git -C $Repository tag -v $name 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            return [pscustomobject]@{
                Checked = $true
                Valid = $false
                Tag = $name
                Message = "Git tag $name references this provenance but its signature did not verify."
            }
        }

        $unsigned = "$($Provenance.recorded.executable.unsignedSha256)"
        $signed = "$($Provenance.recorded.executable.signedSha256)"
        if ($text -notmatch [regex]::Escape($unsigned) -or $text -notmatch [regex]::Escape($signed) -or $text -notmatch [regex]::Escape($sha)) {
            return [pscustomobject]@{
                Checked = $true
                Valid = $false
                Tag = $name
                Message = "Git tag $name verifies but does not contain the recorded commit and both executable hashes."
            }
        }

        return [pscustomobject]@{ Checked = $true; Valid = $true; Tag = $name; Message = $null }
    }

    return [pscustomobject]@{ Checked = $false; Valid = $false; Tag = $null; Message = $null }
}
