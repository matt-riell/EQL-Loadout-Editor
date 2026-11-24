<#
.SYNOPSIS
    Verifies and, where needed, installs the tools used to build and sign a release.

.DESCRIPTION
    Installs missing Git, Azure CLI, SignTool, the Artifact Signing client library,
    the Visual C++ redistributable, the .NET 8 runtime used by the signing client,
    and the .NET SDK pinned by global.json. Components that already meet the
    version requirements are left in place. The script does not change the
    PowerShell execution policy.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Setup-SigningEnvironment.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

function Invoke-WingetInstall {
    param(
        [Parameter(Mandatory = $true)][string] $Id,
        [string] $Version,
        [switch] $UserScope
    )

    $arguments = @(
        'install', '--id', $Id, '--exact',
        '--accept-package-agreements', '--accept-source-agreements',
        '--disable-interactivity'
    )
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $arguments += @('--version', $Version)
    }

    if ($UserScope) {
        $arguments += @('--scope', 'user')
    }

    Write-Host "Installing $Id with WinGet."
    $output = & winget @arguments 2>&1
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Host "$_" }
    if ($code -eq 0 -or $code -eq -1978335189) {
        return $code
    }

    Write-Host "WinGet returned $code for $Id. Retrying without --disable-interactivity."
    $retry = @($arguments | Where-Object { $_ -ne '--disable-interactivity' })
    $retryOutput = & winget @retry 2>&1
    $retryCode = $LASTEXITCODE
    $retryOutput | ForEach-Object { Write-Host "$_" }
    return $retryCode
}

function Install-OfficialNugetPackages {
    $tools = Join-Path (Get-SigningDirectory) 'tools'
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $nuget = Join-Path $tools 'nuget.exe'
    if (-not (Test-Path -LiteralPath $nuget)) {
        Write-Host "Downloading nuget.exe from dist.nuget.org."
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget -UseBasicParsing
    }

    $packages = Join-Path $tools 'nuget'
    Write-Host "Installing Microsoft.Windows.SDK.BuildTools and Microsoft.ArtifactSigning.Client under $packages."
    & $nuget install Microsoft.Windows.SDK.BuildTools -OutputDirectory $packages -NonInteractive
    if ($LASTEXITCODE -ne 0) {
        throw "nuget install Microsoft.Windows.SDK.BuildTools failed with exit code $LASTEXITCODE."
    }

    & $nuget install Microsoft.ArtifactSigning.Client -OutputDirectory $packages -NonInteractive
    if ($LASTEXITCODE -ne 0) {
        throw "nuget install Microsoft.ArtifactSigning.Client failed with exit code $LASTEXITCODE."
    }
}

function Install-DotNetSdk {
    $installed = $null
    try {
        $installed = Resolve-DotNetSdk
    }
    catch {
        $installed = $null
    }

    if ($installed) {
        Write-Host ".NET SDK $($script:RequiredSdk) is already installed at $($installed.DotNetPath)."
        return $installed
    }

    Write-Host ".NET SDK $($script:RequiredSdk) is missing. Trying WinGet."
    Invoke-WingetInstall -Id 'Microsoft.DotNet.SDK.10' -Version $script:RequiredSdk | Out-Null
    Update-SessionPath
    try {
        return Resolve-DotNetSdk
    }
    catch {
        Write-Host "WinGet did not provide SDK $($script:RequiredSdk). Using Microsoft's dotnet-install.ps1."
    }

    $installer = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    $target = Join-Path $env:ProgramFiles 'dotnet'
    if (-not (Test-IsAdministrator)) {
        $target = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
        Write-Host "This session is not elevated, so the SDK will be installed for the current user at $target."
    }

    & $installer -Version $script:RequiredSdk -InstallDir $target
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE."
    }

    return Resolve-DotNetSdk
}

function Install-Net8Runtime {
    param([string] $DotNetPath)

    $runtimes = @(Get-RuntimeVersions $DotNetPath)
    if (Test-RuntimePresent -Runtimes $runtimes -Name 'Microsoft.NETCore.App' -Major '8') {
        Write-Host '.NET 8 runtime is already installed.'
        return
    }

    Write-Host 'The Artifact Signing client tools require the .NET 8 runtime. Installing it.'
    Invoke-WingetInstall -Id 'Microsoft.DotNet.Runtime.8' | Out-Null
    Update-SessionPath
    $runtimes = @(Get-RuntimeVersions $DotNetPath)
    if (-not (Test-RuntimePresent -Runtimes $runtimes -Name 'Microsoft.NETCore.App' -Major '8')) {
        throw 'The .NET 8 runtime is still not visible to dotnet --list-runtimes. Install the x64 .NET 8 runtime from https://dotnet.microsoft.com/download/dotnet/8.0 and run this script again.'
    }
}

function Install-VcRedist {
    $existing = Test-VcRedistX64
    if ($existing) {
        Write-Host "Visual C++ redistributable x64 is already installed ($($existing.Version))."
        return $existing
    }

    if (-not (Test-IsAdministrator)) {
        throw 'The x64 Visual C++ redistributable is not installed, and this session is not elevated. Run this script from an elevated PowerShell window, or install https://aka.ms/vs/17/release/vc_redist.x64.exe and run the script again.'
    }

    Write-Host 'Installing the x64 Visual C++ redistributable.'
    $installer = Join-Path ([System.IO.Path]::GetTempPath()) 'vc_redist.x64.exe'
    Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $installer -UseBasicParsing
    $proc = Start-Process -FilePath $installer -ArgumentList @('/install', '/quiet', '/norestart') -Wait -PassThru
    if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 1638 -and $proc.ExitCode -ne 3010) {
        throw "vc_redist.x64.exe failed with exit code $($proc.ExitCode)."
    }

    $existing = Test-VcRedistX64
    if (-not $existing) {
        throw 'The Visual C++ redistributable installer finished, but the x64 runtime is still not registered.'
    }

    return $existing
}

try {
    Write-Host 'Checking the signing environment. Installed components that already qualify are not reinstalled.'
    Write-Host 'This script does not change the PowerShell execution policy.'

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        Invoke-WingetInstall -Id 'Git.Git' | Out-Null
        Update-SessionPath
        $git = Get-Command git -ErrorAction SilentlyContinue
    }

    if (-not $git) {
        throw 'Git for Windows is not available. Install it from https://git-scm.com/download/win and run this script again.'
    }

    $gitVersion = (& git --version | Out-String).Trim()
    Write-Host "Git: $gitVersion ($($git.Source))"

    $sdk = Install-DotNetSdk
    Install-Net8Runtime -DotNetPath $sdk.DotNetPath
    $runtimes = @(Get-RuntimeVersions $sdk.DotNetPath)
    if (-not (Test-RuntimePresent -Runtimes $runtimes -Name 'Microsoft.NETCore.App' -Major '10')) {
        throw "The .NET 10 runtime is not visible from $($sdk.DotNetPath). SDK $($script:RequiredSdk) includes it when that SDK is the one on this path."
    }

    $vc = Install-VcRedist
    $az = Resolve-AzureCli
    if (-not $az) {
        Write-Host 'Azure CLI is not installed. The official package is a machine-wide MSI, so installation needs an elevated window.'
        Invoke-WingetInstall -Id 'Microsoft.AzureCLI' | Out-Null
        if ((Test-IsAdministrator)) {
            $msi = Join-Path ([System.IO.Path]::GetTempPath()) 'azure-cli.msi'
            Write-Host 'Downloading the Azure CLI MSI from https://aka.ms/installazurecliwindows.'
            Invoke-WebRequest -Uri 'https://aka.ms/installazurecliwindows' -OutFile $msi -UseBasicParsing
            $install = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/i', $msi, '/quiet', '/norestart') -Wait -PassThru
            if ($install.ExitCode -ne 0 -and $install.ExitCode -ne 3010) {
                throw "The Azure CLI MSI failed with exit code $($install.ExitCode)."
            }
        }

        $az = Resolve-AzureCli
    }

    $pair = Find-SigningToolPair
    if (-not $pair) {
        Write-Host 'SignTool and the Artifact Signing library are not both available. Trying the Artifact Signing Client Tools installer.'
        $clientCode = Invoke-WingetInstall -Id 'Microsoft.Azure.ArtifactSigningClientTools'
        if ($clientCode -ne 0 -and $clientCode -ne -1978335189) {
            Write-Host "WinGet package Microsoft.Azure.ArtifactSigningClientTools returned $clientCode. Trying the previous Trusted Signing package id."
            Invoke-WingetInstall -Id 'Microsoft.Azure.TrustedSigningClientTools' | Out-Null
        }

        Update-SessionPath
        $pair = Find-SigningToolPair
        if (-not $pair) {
            if (-not (Test-IsAdministrator)) {
                Write-Host 'The client-tools installer did not put SignTool and the signing library on disk. Falling back to the official NuGet packages, which do not require elevation.'
            }

            Install-OfficialNugetPackages
            $pair = Find-SigningToolPair
        }
    }

    if (-not $pair) {
        throw "SignTool $($script:MinimumSignToolVersion) or newer and a matching Azure.CodeSigning.Dlib.dll were not found. Windows SDK 10.0.20348 is not compatible. Install the current Windows SDK or the Artifact Signing Client Tools and run this script again."
    }

    if ($pair.SignToolArchitecture -ne $pair.DlibArchitecture) {
        throw "SignTool is $($pair.SignToolArchitecture) and Azure.CodeSigning.Dlib.dll is $($pair.DlibArchitecture). They must match."
    }

    if (-not $az) {
        $tools = [ordered]@{
            recordedAtUtc = Get-UtcTimestamp
            gitPath = $git.Source
            gitVersion = $gitVersion
            dotnetPath = $sdk.DotNetPath
            sdkVersion = $sdk.SdkVersion
            signToolPath = $pair.SignToolPath
            signToolVersion = $pair.SignToolVersion
            signToolArchitecture = $pair.SignToolArchitecture
            dlibPath = $pair.DlibPath
            dlibArchitecture = $pair.DlibArchitecture
            vcRedistVersion = $vc.Version
        }
        Save-ToolPaths $tools
        throw 'Azure CLI is not installed. Its installer is a machine-wide MSI and this session is not elevated, so WinGet reported that no applicable installer was found. Open an elevated PowerShell window and run signing\Setup-SigningEnvironment.ps1 again. SignTool and the signing library were still installed for this user.'
    }

    Write-Host "Azure CLI: $az"

    $tools = [ordered]@{
        recordedAtUtc = Get-UtcTimestamp
        gitPath = $git.Source
        gitVersion = $gitVersion
        dotnetPath = $sdk.DotNetPath
        sdkVersion = $sdk.SdkVersion
        azureCliPath = $az
        signToolPath = $pair.SignToolPath
        signToolVersion = $pair.SignToolVersion
        signToolArchitecture = $pair.SignToolArchitecture
        dlibPath = $pair.DlibPath
        dlibArchitecture = $pair.DlibArchitecture
        vcRedistVersion = $vc.Version
        dotnet8Runtime = $true
        dotnet10Runtime = $true
    }
    Save-ToolPaths $tools

    Write-Host ''
    Write-Host 'Signing environment is ready.'
    Write-Host "  .NET SDK $($sdk.SdkVersion): $($sdk.DotNetPath)"
    Write-Host "  SignTool $($pair.SignToolVersion) $($pair.SignToolArchitecture): $($pair.SignToolPath)"
    Write-Host "  Artifact Signing library $($pair.DlibArchitecture): $($pair.DlibPath)"
    Write-Host "  Azure CLI: $az"
    Write-Host "  Visual C++ x64: $($vc.Version)"
    Write-Host "Tool paths were saved to $(Get-ToolPathsFile). That file is local and is not a credential."
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
