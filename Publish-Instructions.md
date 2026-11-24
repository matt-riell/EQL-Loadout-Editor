# Publish and sign EQL Loadout Editor

Run these commands from the repository root. The publish uses the existing `publish-win-x64.ps1` script and .NET SDK 10.0.402. Signing uses the existing Azure Artifact Signing Public Trust profile. These commands do not create an Azure account or certificate profile.

## 1. Confirm the signing tools

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Setup-SigningEnvironment.ps1
```

This checks Git, .NET SDK 10.0.402, SignTool, and the Azure Artifact Signing library. It installs only what is missing.

## 2. Sign in and confirm the existing certificate profile

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Configure-ArtifactSigning.ps1
```

If Azure CLI has no session, this opens an interactive `az login`. Finish that in the browser. The script then finds the existing Public Trust profile and writes `signing\local\artifact-signing.config.json`. The signed-in identity needs the **Artifact Signing Certificate Profile Signer** role on that profile.

## 3. Build that commit and sign it

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\signing\Publish-SignedRelease.ps1 -Commit HEAD
```

Use a full commit SHA, tag, or branch instead of `HEAD` when the release is not the current checkout. The script checks out that commit in a clean worktree, runs `publish-win-x64.ps1`, signs `EqlLoadouts.exe`, and signs the provenance catalog.

The signed program is written to:

```text
releases\EqlLoadouts-1.0.0-<12-character commit>\EqlLoadouts.exe
```

`LICENSE` is placed beside the executable because the splash screen opens it. The signature evidence is in the `evidence` folder next to the executable. The script also writes `releases\EqlLoadouts-1.0.0-<12-character commit>.zip` beside that folder. Upload that zip to the GitHub release.
