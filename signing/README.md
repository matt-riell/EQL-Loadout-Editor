# Signing EQL Loadout Editor

These scripts build the existing single-file Windows executable from one Git commit, sign it with Azure Artifact Signing, and keep the evidence for that signature separate from the program. They call `publish-win-x64.ps1` unchanged. That script is what makes the unsigned exe reproducible: .NET SDK 10.0.402, `EqlSingleFilePublish=true`, and the deterministic settings already in the project.

Authenticode signing changes the exe. Compare a rebuild with the unsigned SHA-256. The signed SHA-256 will differ every time the file is signed, because the timestamp and signature metadata are not reproducible.

`EqlLoadouts.exe` is the program. `LICENSE` is copied beside it because the splash screen opens that file. `CONTRIBUTOR_AGREEMENT.md` is part of the existing publish output. The `evidence` folder is for release audit. Users do not need it to run the editor.

The Git commit is not embedded in the executable. `IncludeSourceRevisionInInformationalVersion` is false, and the publish sets a fixed source revision id so the unsigned bytes stay stable. The commit is recorded in `evidence\release-provenance.json`.

## Prerequisites

- Windows 10 or later, x64.
- An elevated PowerShell window for the Azure CLI MSI and, if it is not already present, the Visual C++ redistributable. SignTool and the Artifact Signing library can be installed without elevation from Microsoft's NuGet packages. The script does not change the execution policy.
- An existing Azure Artifact Signing account with a Public Trust certificate profile for Generation X Technologies, Inc. The scripts do not create an account or a profile.
- The signing identity needs the **Artifact Signing Certificate Profile Signer** role on that certificate profile.
- The project SDK is exactly **.NET SDK 10.0.402**. `global.json` disables roll-forward. The `Microsoft.ArtifactSigning.Client` package installed by setup requires the .NET 10 runtime. Setup also checks for the .NET 8 runtime named in Microsoft's Artifact Signing integration article, so an older client build can still load.

Suggested resource names, used only when they already exist:

- Account: `genxtechsigning26`
- Certificate profile: `genx-public-signing`

## One-time setup

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Setup-SigningEnvironment.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Configure-ArtifactSigning.ps1
```

Setup checks Git, Azure CLI, SignTool 10.0.22621.755 or newer, `Azure.CodeSigning.Dlib.dll`, the x64 Visual C++ redistributable, the .NET 8 runtime the signing client uses, the .NET 10 runtime, and SDK 10.0.402. It installs only what is missing. SignTool and the signing library must be the same architecture. The Windows SDK 10.0.20348 SignTool is rejected.

Configure signs in with `az login` when the CLI has no session. It searches enabled subscriptions for an existing Artifact Signing account, requires a Public Trust profile, and checks the signer role when the identity is allowed to read role assignments. It writes:

- `signing\local\artifact-signing.config.json`
- `signing\local\metadata.json`

Those files hold the account name, profile name, and regional endpoint. They do not hold passwords or tokens. `signing\local\` is gitignored.

Each release writes its own metadata file under `evidence` and adds a `CorrelationId` so the signing request can be matched to the release record.

## Azure permission requirements

The identity that runs `az login` must be able to read the Artifact Signing account and must have:

`Artifact Signing Certificate Profile Signer`

scoped to the certificate profile:

```text
/subscriptions/<subscription>/resourceGroups/<group>/providers/Microsoft.CodeSigning/codeSigningAccounts/<account>/certificateProfiles/<profile>
```

Configure does not assign that role. An owner can assign it with:

```powershell
az role assignment create --assignee "<sign-in name>" --role "Artifact Signing Certificate Profile Signer" --scope "<certificate profile scope>"
```

The endpoint must be the regional `https://<region>.codesigning.azure.net` URI for the account. A region mismatch fails signing with 403.

For an audit trail beyond the local evidence folder, turn on diagnostic settings for the Artifact Signing account in the Azure portal and send the account's sign logs to a Log Analytics workspace the organization controls. Do that in the portal so the log categories are the ones that account actually exposes. The release `CorrelationId` is the value to look up. The scripts do not change diagnostic settings.

## Sign a release

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit HEAD
```

Or pass a full commit SHA, tag, or branch:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit "<full Git SHA>"
```

The script:

1. Resolves the Git reference to a full commit SHA.
2. Builds that commit in a clean detached worktree by running `publish-win-x64.ps1`.
3. Records the unsigned SHA-256.
4. Signs `EqlLoadouts.exe` with SignTool, SHA-256 file and timestamp digests, the configured certificate profile, and `http://timestamp.acs.microsoft.com`.
5. Verifies the signature with `signtool verify /pa /all /v` and requires the publisher text `Generation X Technologies, Inc.`
6. Records the signed SHA-256.
7. Writes `evidence\release-provenance.json`.
8. Builds an Authenticode catalog over the exe, the manifest, and the build records, then signs that catalog with the same profile.

The catalog is the release attestation. SignTool cannot Authenticode-sign the JSON file directly, so the JSON is a member of the signed catalog. Verification checks the catalog signature and the hashes inside it.

Completed output:

```text
releases\EqlLoadouts-1.0.0-<12-character commit>\
  EqlLoadouts.exe
  LICENSE
  CONTRIBUTOR_AGREEMENT.md
  evidence\
    release-provenance.json
    release-attestation.cat
    signature-verification.txt
    catalog-signature-verification.txt
    build-log.txt
    build-record.json
    signing-record.json
    unsigned-hash.txt
    artifact-signing.metadata.json
    artifact-signing.catalog-metadata.json
```

The same command also writes `releases\EqlLoadouts-1.0.0-<12-character commit>.zip` beside that folder. The zip contains the files from the release directory, with `evidence` beside `EqlLoadouts.exe`. Attach that zip to the GitHub release. `releases\` is already ignored by git. The folder name uses the first 12 characters of the commit. The full SHA is in the provenance file.

If a Git signing key is already configured in `user.signingkey`, this also creates a local signed tag. It is not pushed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit HEAD -CreateSignedTag
```

The script refuses to create an unsigned tag. It does not create a new key.

The individual steps are:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Build-Release.ps1 -Commit HEAD -OutputDirectory .\releases\manual
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Sign-Release.ps1 -ExePath .\releases\manual\EqlLoadouts.exe
```

`Publish-SignedRelease.ps1` is the command that also writes and seals the provenance.

## Verify a signed release

Signature and file integrity, without rebuilding:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Verify-Release.ps1 -ReleaseDirectory .\releases\EqlLoadouts-1.0.0-<12-character commit>
```

This checks the signed SHA-256, the Authenticode signature, the publisher, the timestamp, and the signed catalog when the manifest says one exists. It prints the recorded Git commit. It does not, by itself, prove that commit produced the unsigned exe.

## Prove the connection to a Git commit

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Verify-Release.ps1 -ReleaseDirectory .\releases\EqlLoadouts-1.0.0-<12-character commit> -RebuildFromCommit
```

This checks out that full commit in another clean worktree, runs `publish-win-x64.ps1`, and compares the new unsigned SHA-256 with `recorded.executable.unsignedSha256`. On a match it prints:

```text
REPRODUCIBLE BUILD VERIFIED — The specified Git commit produced an unsigned executable whose SHA-256 hash matches the original recorded build.
```

That sentence is printed only after the hashes are compared. The rebuild must use SDK 10.0.402. A different SDK stops the check and is reported. `core.autocrlf` and the recorded project-file hashes are compared as well, and a difference fails the check instead of being ignored.

The provenance file says which values were written down and which were checked on the release machine. `authentication.status` is either `authenticode-catalog` or `unauthenticated`.

- `authenticode-catalog` is a claim until `Verify-Release.ps1` validates `evidence\release-attestation.cat`. A valid catalog is signed by Generation X Technologies, Inc. and covers the manifest bytes, so an edit to the recorded commit or the unsigned hash fails verification.
- `unauthenticated` means the manifest is metadata. The unsigned hash is an assertion someone can edit. A rebuild can still show that the recorded commit produces the recorded unsigned hash, but a person who can change both the commit and the hash could point the file at a different build. The verifier says that explicitly when the catalog is not authenticated.

No self-signed certificate is created for the manifest.

## What the provenance contains

`evidence\release-provenance.json` separates:

- `recorded.git`, `recorded.build`, `recorded.executable.unsignedSha256`, and `recorded.azure` are assertions from the release scripts.
- `verified.authenticode`, `verified.timestamp`, and `recorded.executable.signedSha256` are checks the release machine already ran. The verifier runs them again.
- `authentication` says whether a signed catalog covers the file.

It includes the commit SHA, repository, commit timestamp, author, subject, release tag when one already pointed at the commit, application version, SDK, Release configuration, `win-x64`, the publish command, build time, project-file hashes, the note that no NuGet lock file is used, both exe hashes, file size, signer subject and thumbprint, timestamp result, account, profile, endpoint, correlation id, and signing time.

## Troubleshooting

**SDK version does not match.** `dotnet --version` must print `10.0.402`. Install that SDK and run setup again. A newer 10.0 SDK will not be used, because `global.json` sets `rollForward` to `disable`.

**SignTool or the signing library is missing.** Run setup. If the WinGet installer needs elevation, open an elevated PowerShell window and run it again. Setup falls back to the official NuGet packages `Microsoft.Windows.SDK.BuildTools` and `Microsoft.ArtifactSigning.Client` when the installer cannot run.

**Architecture mismatch.** Use the x64 SignTool with the x64 `Azure.CodeSigning.Dlib.dll`. Setup records the pair it selected in `signing\local\tool-paths.json`.

**403 or `SignerSign()` during signing.** The metadata endpoint is not the region of the account, or the identity does not have Artifact Signing Certificate Profile Signer on that profile. Run Configure again and read the account and role it reports.

**Azure CLI did not install.** WinGet error `No applicable installer found` means the session is not elevated. The official Azure CLI package is a machine-wide MSI. Open PowerShell with Run as administrator and run `signing\Setup-SigningEnvironment.ps1` again.

**`az login` waits for a browser.** Configure starts an interactive login when `az account show` fails. Finish that login in the browser. Tokens stay in the Azure CLI user profile, not in the repository.

**The signed hash does not match a rebuild.** Compare the rebuild with the unsigned hash in the provenance file. The signed hash is supposed to change.

**Signature verification fails after the file is edited.** That is the expected Authenticode result. `signtool verify /pa /all /v` and the signed SHA-256 both fail when the exe bytes change.

**Catalog verification fails after the manifest is edited.** The catalog hash no longer matches. If the catalog signature is valid but the hashes are not, the manifest or another covered file was changed after signing.

**The provenance is unauthenticated.** Catalog signing did not finish, or the catalog was removed. The exe can still have a valid Authenticode signature. Treat the commit and unsigned hash in the JSON as unsealed until a catalog or a signed Git tag checks out.

**Timestamp verification fails.** The Microsoft timestamp service is `http://timestamp.acs.microsoft.com`. Artifact Signing certificates last about three days, so a signature without a trusted timestamp is rejected.

**Build inputs differ.** The verifier prints the project file whose SHA-256 changed and the `core.autocrlf` values. A line-ending difference in the checkout can change the unsigned exe. Use the same Git line-ending settings as the release build.

**Output directory is not empty.** Build-Release refuses to reuse an existing exe. Remove the directory or choose another `-OutputDirectory`.

**Execution policy.** Run the scripts with `-ExecutionPolicy Bypass`, as shown above. Do not change the machine policy for these scripts.
