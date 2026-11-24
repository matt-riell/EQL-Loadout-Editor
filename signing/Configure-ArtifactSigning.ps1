<#
.SYNOPSIS
    Confirms the existing Azure Artifact Signing account and writes SignTool metadata.

.DESCRIPTION
    Uses the current Azure CLI identity, or starts az login when none is present.
    It does not create an account or a certificate profile. Suggested names are
    used only when those resources exist. The generated files contain resource
    names and a regional endpoint, not passwords or tokens.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Configure-ArtifactSigning.ps1
#>
[CmdletBinding()]
param(
    [string] $SubscriptionId,
    [string] $AccountName,
    [string] $CertificateProfileName
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value.Replace('"', '""')) + '"'
}

function Invoke-Az {
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [switch] $AllowFailure,
        [switch] $Interactive
    )

    $az = Resolve-AzureCli
    if (-not $az) {
        throw 'Azure CLI is not installed. Run signing\Setup-SigningEnvironment.ps1.'
    }

    if ($Interactive) {
        & $az @Arguments
        $code = $LASTEXITCODE
        if ($code -ne 0 -and -not $AllowFailure) {
            throw "az $($Arguments -join ' ') failed with exit code $code."
        }

        return [pscustomobject]@{ ExitCode = $code; Text = ''; ErrorText = '' }
    }

    $argumentLine = (@($Arguments | ForEach-Object { ConvertTo-ProcessArgument "$_" })) -join ' '
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $env:ComSpec
    $start.Arguments = "/d /c `"`"$az`" $argumentLine`""
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $text = $stdoutTask.Result
    $errorText = $stderrTask.Result
    if ($process.ExitCode -ne 0 -and -not $AllowFailure) {
        throw "az $($Arguments -join ' ') failed with exit code $($process.ExitCode).`n$errorText"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Text = $text
        ErrorText = $errorText
    }
}

function Get-AzJson {
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [switch] $AllowFailure
    )

    $result = Invoke-Az -Arguments ($Arguments + @('-o', 'json')) -AllowFailure:$AllowFailure
    if ($result.ExitCode -ne 0) {
        return $null
    }

    $trimmed = "$($result.Text)".Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -eq '[]') {
        return ,@()
    }

    return ,($trimmed | ConvertFrom-Json)
}

function Get-AccountEndpoint {
    param(
        [Parameter(Mandatory = $true)] $Account
    )

    $fromApi = Find-NestedString -Value $Account -Pattern 'https://[a-z0-9]+\.codesigning\.azure\.net/?'
    $location = "$($Account.location)"
    if ([string]::IsNullOrWhiteSpace($location) -and $Account.properties) {
        $location = "$($Account.properties.location)"
    }

    $fromRegion = $null
    if (-not [string]::IsNullOrWhiteSpace($location)) {
        try {
            $fromRegion = Get-RegionEndpoint $location
        }
        catch {
            $fromRegion = $null
        }
    }

    if ($fromApi) {
        $normalized = "$fromApi".Trim().TrimEnd('/')
        if ($fromRegion -and $normalized -ne $fromRegion) {
            throw "The account URI $normalized does not match the endpoint $fromRegion for region $location. Refusing to guess which endpoint to sign against."
        }

        return $normalized
    }

    if ($fromRegion) {
        return $fromRegion
    }

    throw "Could not determine the Artifact Signing endpoint for account $($Account.name) in region '$location'."
}

try {
    if ([string]::IsNullOrWhiteSpace($AccountName)) {
        $AccountName = $script:SuggestedAccountName
    }

    if ([string]::IsNullOrWhiteSpace($CertificateProfileName)) {
        $CertificateProfileName = $script:SuggestedProfileName
    }

    $pair = Resolve-SigningToolPair
    Write-Host "SignTool $($pair.SignToolVersion) $($pair.SignToolArchitecture): $($pair.SignToolPath)"
    Write-Host "Artifact Signing library $($pair.DlibArchitecture): $($pair.DlibPath)"
    if ($pair.SignToolArchitecture -ne $pair.DlibArchitecture) {
        throw 'SignTool and Azure.CodeSigning.Dlib.dll architectures do not match.'
    }

    $account = Get-AzJson -Arguments @('account', 'show') -AllowFailure
    if (-not $account) {
        Write-Host 'Azure CLI is not signed in. Starting interactive az login.'
        $login = Invoke-Az -Arguments @('login') -Interactive
        if ($login.ExitCode -ne 0) {
            throw 'az login did not complete.'
        }

        $account = Get-AzJson -Arguments @('account', 'show')
    }

    Write-Host "Signed in as $($account.user.name) on subscription $($account.name) ($($account.id))."
    $subscriptions = @(ConvertTo-ObjectList (Get-AzJson -Arguments @('account', 'list', '--query', "[?state=='Enabled']")))
    if ($SubscriptionId) {
        $subscriptions = @($subscriptions | Where-Object { $_.id -eq $SubscriptionId })
        if ($subscriptions.Count -eq 0) {
            throw "Subscription $SubscriptionId was not found among the enabled subscriptions for this identity."
        }
    }

    $apiVersions = @('2025-10-01', '2024-09-30-preview', '2024-02-05-preview')
    $discovered = New-Object System.Collections.Generic.List[object]
    foreach ($subscription in $subscriptions) {
        $resources = @(ConvertTo-ObjectList (Get-AzJson -Arguments @(
            'resource', 'list',
            '--subscription', "$($subscription.id)",
            '--resource-type', 'Microsoft.CodeSigning/codeSigningAccounts'
        ) -AllowFailure))
        foreach ($resource in $resources) {
            if (-not $resource -or -not $resource.id) {
                continue
            }

            $full = $null
            foreach ($version in $apiVersions) {
                $full = Get-AzJson -Arguments @(
                    'rest', '--method', 'get',
                    '--url', "https://management.azure.com$($resource.id)?api-version=$version"
                ) -AllowFailure
                if ($full) {
                    break
                }
            }

            if (-not $full) {
                $full = $resource
            }

            $profiles = $null
            foreach ($version in $apiVersions) {
                $profiles = Get-AzJson -Arguments @(
                    'rest', '--method', 'get',
                    '--url', "https://management.azure.com$($resource.id)/certificateProfiles?api-version=$version"
                ) -AllowFailure
                if ($profiles) {
                    break
                }
            }

            foreach ($profile in @(ConvertTo-ObjectList $profiles.value)) {
                $discovered.Add([pscustomobject]@{
                    SubscriptionId = "$($subscription.id)"
                    SubscriptionName = "$($subscription.name)"
                    ResourceGroup = "$($resource.resourceGroup)"
                    AccountName = "$($resource.name)"
                    Location = "$($full.location)"
                    Account = $full
                    ProfileName = "$($profile.name)"
                    ProfileType = (Get-ProfileType $profile)
                    Profile = $profile
                    Scope = "$($resource.id)/certificateProfiles/$($profile.name)"
                })
            }
        }
    }

    if ($discovered.Count -eq 0) {
        throw "No Artifact Signing certificate profiles were found for this identity. This script does not create accounts or profiles. Confirm that $AccountName / $CertificateProfileName exists and that this identity can read Microsoft.CodeSigning resources."
    }

    $publicProfiles = @($discovered | Where-Object { $_.ProfileType -eq 'PublicTrust' })
    $preferred = @($publicProfiles | Where-Object { $_.AccountName -eq $AccountName -and $_.ProfileName -eq $CertificateProfileName })
    $chosen = $null
    $selection = ''
    if ($preferred.Count -eq 1) {
        $chosen = $preferred[0]
        $selection = "Using the suggested account '$AccountName' and Public Trust profile '$CertificateProfileName'."
    }
    elseif ($preferred.Count -gt 1) {
        throw "More than one Public Trust profile matched $AccountName / $CertificateProfileName. Pass -SubscriptionId to select one."
    }
    else {
        $onAccount = @($publicProfiles | Where-Object { $_.AccountName -eq $AccountName })
        if ($onAccount.Count -eq 1) {
            $chosen = $onAccount[0]
            $selection = "Suggested profile '$CertificateProfileName' was not found. The account '$AccountName' has one Public Trust profile, '$($chosen.ProfileName)', and that profile will be used."
        }
        elseif ($publicProfiles.Count -eq 1) {
            $chosen = $publicProfiles[0]
            $selection = "Suggested names '$AccountName' / '$CertificateProfileName' were not found. One Public Trust profile exists and will be used: $($chosen.AccountName) / $($chosen.ProfileName)."
        }
    }

    if (-not $chosen) {
        $lines = @($discovered | ForEach-Object { "$($_.SubscriptionName): $($_.AccountName) / $($_.ProfileName) type=$($_.ProfileType)" })
        throw "Could not choose a Public Trust profile without creating one. Profiles visible to this identity:`n$($lines -join "`n")"
    }

    Write-Host $selection
    $endpoint = Get-AccountEndpoint $chosen.Account
    Write-Host "Endpoint: $endpoint"
    $set = Invoke-Az -Arguments @('account', 'set', '--subscription', $chosen.SubscriptionId)
    if ($set.ExitCode -ne 0) {
        throw "Could not select subscription $($chosen.SubscriptionId)."
    }

    $signedIn = Get-AzJson -Arguments @('account', 'show')
    $principalId = $null
    $user = Get-AzJson -Arguments @('ad', 'signed-in-user', 'show') -AllowFailure
    if ($user -and $user.id) {
        $principalId = "$($user.id)"
    }

    $roleConfirmed = $false
    $roleCheck = 'not-confirmed'
    $roleArguments = @('role', 'assignment', 'list', '--scope', $chosen.Scope, '--include-inherited')
    if ($principalId) {
        $roleArguments += @('--assignee-object-id', $principalId)
    }
    elseif ($signedIn.user.name) {
        $roleArguments += @('--assignee', "$($signedIn.user.name)")
    }

    $roles = Get-AzJson -Arguments $roleArguments -AllowFailure
    if ($null -eq $roles) {
        $roles = Get-AzJson -Arguments @('role', 'assignment', 'list', '--scope', $chosen.Scope) -AllowFailure
    }

    if ($null -eq $roles) {
        $roleCheck = 'not-confirmed'
        Write-Host 'Could not read role assignments. The Artifact Signing Certificate Profile Signer role was not confirmed. Signing will still be attempted with this identity, and Azure will reject it if the role is missing.'
    }
    else {
        $roleNames = @($roles | ForEach-Object { "$($_.roleDefinitionName)" })
        $accepted = @('Artifact Signing Certificate Profile Signer', 'Trusted Signing Certificate Profile Signer')
        $roleConfirmed = [bool]($roleNames | Where-Object { $accepted -contains $_ })
        if ($roleConfirmed) {
            $roleCheck = 'confirmed'
            Write-Host 'Artifact Signing Certificate Profile Signer is assigned to this identity at the certificate profile scope.'
        }
        else {
            throw @"
This identity can read role assignments, and Artifact Signing Certificate Profile Signer is not assigned at:
$($chosen.Scope)

Do not continue by creating a new account. An owner can assign the existing profile with:
az role assignment create --assignee "$($signedIn.user.name)" --role "Artifact Signing Certificate Profile Signer" --scope "$($chosen.Scope)"
"@
        }
    }

    $config = [ordered]@{
        recordedAtUtc = Get-UtcTimestamp
        subscriptionId = $chosen.SubscriptionId
        subscriptionName = $chosen.SubscriptionName
        resourceGroup = $chosen.ResourceGroup
        accountName = $chosen.AccountName
        certificateProfileName = $chosen.ProfileName
        profileType = 'PublicTrust'
        endpoint = $endpoint
        location = $chosen.Location
        expectedPublisher = $script:ExpectedPublisher
        timestampUrl = $script:TimestampUrl
        signerRole = [ordered]@{
            name = 'Artifact Signing Certificate Profile Signer'
            status = $roleCheck
            confirmed = $roleConfirmed
            scope = $chosen.Scope
            principalName = "$($signedIn.user.name)"
        }
        selection = $selection
    }
    Save-SigningConfig $config
    $template = [ordered]@{
        Endpoint = $endpoint
        CodeSigningAccountName = $chosen.AccountName
        CertificateProfileName = $chosen.ProfileName
    }
    $templatePath = Join-Path (Get-SigningLocalDirectory) 'metadata.json'
    Write-JsonFile -Path $templatePath -Value $template
    Write-Host "Saved configuration to $(Get-SigningConfigFile)."
    Write-Host "Saved SignTool metadata template to $templatePath."
    Write-Host 'Neither file contains a token or password. Each release adds its own CorrelationId when it signs.'
    exit 0
}
catch {
    Write-Host $_.Exception.Message
    exit 1
}
