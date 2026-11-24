# EQL Loadout Editor

## Download and run

The same steps are on the website: [genx-tech.com/eql-loadout](https://genx-tech.com/eql-loadout).

Open the [latest release](https://github.com/matt-riell/EQL-Loadout-Editor/releases/latest) and download the zip named like `EqlLoadouts-1.0.0-<commit>.zip`. That zip is the program.

1. Download that zip.
2. Extract it to a folder on your computer.
3. Run `EqlLoadouts.exe` in that folder.

The .NET runtime is already inside `EqlLoadouts.exe`. Nothing else has to be installed. Leave `LICENSE` in the same folder. The splash screen opens it. The `evidence` folder can stay or be ignored. It is not needed to run the editor.

> [!CAUTION]
> **The game should be at the character select screen when you save or restore. If your EverQuest Legends character is in the world, then EQL can overwrite the UI files and your changes will be lost.**

### While you play

Turn logging on with `/log on`.

Make a macro whose line is `/who` followed by the character name, for example `/who Zenator`. Run that macro instead of typing the command.

- After switching loadouts, run the macro. The editor reads that line from the character log and uses it to tell which loadout is in use.
- Run the macro again when the character's level changes. The new level makes spell search, spell details, and upgrade suggestions match the level the character is now.

Run `/outputfile inventory` when your bags change. The editor uses that export to show what is in each bag.

## Messy Details

## Summary
EQL Loadout Editor edits EverQuest Legends character and UI loadout files. You edit a single loadout. You can save that loadout's layout to another loadout, to all loadouts on a server, or across all servers. Saving writes those copied and edited sections. Before anything is written, the original files are copied into a backup folder in the game directory so you can restore them if needed. Macros and spell loadouts cannot be edited while the destination is All loadouts or All servers. An all-destination save copies the changed and highlighted window sections. Spell loadouts stay on the loadout that was edited.

## NOTE: The game should be at the character select screen when you save or restore. If your EverQuest Legends character is in the world, then EQL can overwrite the UI files and your changes will be lost.

## Features

### Loadouts and saving

- Open the EverQuest Legends folder and list the loadouts for a character and server.
- Copy one or more selected sections from the source loadout to a single destination loadout. When the destination is every loadout for that character and server, or every server, Save writes the window sections you changed and the windows highlighted on the layout.
- Save windows, character changes (macros and spell loadouts), or both. Character changes can replace the section or merge without deleting keys the editor did not change. A UI save replaces only the window sections that changed.
- Before saving, the editor reminds you to be at the character select screen. That reminder can be turned off.
- Original files are copied to `LoadoutBackups\MODIFIED` before a save. If the server has no full backup yet, the editor says so and creates one first.
- Each time the editor starts, and when you use Backup Files, every character's loadouts are copied into `LoadoutBackups\FULL`, including unsaved edits. Live game files are not changed. Each backup is a date, then the server and character, then the loadout, with separate character and windows files. If the EverQuest Legends folder is missing or has no characters, the editor says that no backup was performed.
- Restore from a full or modified backup. Check a server and character to include every loadout in that backup, or check individual loadouts. Choose the character file, the windows file, or both.
- Undo and redo edits (Ctrl+Z and Ctrl+Y). Ctrl+S saves.

### Window layout

- Arrange windows on a layout of the game's screen size. Drag to move, snap to a grid, and align, stack, match size, or center the selection on the screen. The space left between stacked windows is set in settings.
- Find windows by name in the section list, and show or hide a single window or a whole group.
- Ctrl+A selects the shown windows in the current section. When every section is selected, it selects every shown window.
- Hotbar windows are listed under Hotbars and are placed on the layout with the other windows.
- Inventory bags, with Bank first, are arranged on that layout. Bag contents come from the game's `/outputfile inventory` export, `{character}_{server}-Inventory.txt`. The editor can remind you to refresh that export when it starts.
- Chat filters are shown for the selected chat window. When chat windows are saved, the editor asks whether to copy those filters to the character's other loadouts. Window positions are saved either way.

### Classes

- Each loadout has three classes, set with the Class picker. The picker lists the class combinations and levels found in `/who` results in that character's log.
- Editing macros or spell loadouts on a loadout without classes opens Choose Classes. The classes are preselected from the spells in that loadout's in-use spell sets.
- The character's level from `/who` sets the level used for spell search, spell details, and upgrades.

### Macros

- Edit macros when a single destination is selected. Drag a source macro onto a destination button, or into Changes to copy it onto the same button.
- Click an empty destination slot to create a macro, and right-click a destination macro to clear it. Macro names can be given a color.
- Changes lists copied, new, modified, and deleted destination macros. Revert one change, or use Clear All.

### Spell loadouts

- Edit spell loadouts when a single destination is selected. Add, delete, and rename spell sets, and show or hide unused spell sets.
- Drag a gem onto another slot to swap them, or clear a gem.
- Spell search uses the loadout's classes, can be filtered by class, and sorts by level or by name. Spells those classes cannot use are marked. Drag a spell from search onto a gem.
- The details panel shows the icon, name, class and level, stat lines, and description.
- Spell power ranks are read from the "You begin casting" lines in the character's log. A ranked spell shows its rank after the name, such as II, and its amounts use that rank.
- A gem shows **Upgrade** when the loadout's classes can use a better spell at the character's level. An upgrade is a later spell in the same line, or a similar spell with the same category, resist, and target that does more damage or healing per second. A spell that adds a slow, snare, or other penalty is not offered. Hover over Upgrade to compare the spell in the Spell Upgrade panel, and click it to put that spell on the gem.

### Application

- Choose a spell icon set and size. The default set matches the game's Default textures.
- Switch between a light theme and a dark theme.
- A second launch does not open another window. It brings the running instance to the foreground.
- Reset the application to its default settings.

## Installing
The program and the .NET runtime are in one file, `EqlLoadouts.exe`. Download the release zip, extract it to the location you want, and run `EqlLoadouts.exe`. Keep `LICENSE` in the same folder, because the splash screen opens it. The `evidence` folder holds the signing records for that release and is not needed to run the editor.

Current version is 1.0.0. Signing and verifying a release are described in [signing/README.md](signing/README.md).

## Requirements to Build

- Windows 10 or later
- [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/) with the .NET desktop development workload, or the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

The project targets .NET 10. Use a Visual Studio release that can build .NET 10. An older Visual Studio that stops at .NET 8 or .NET 9 cannot build this solution.

## Install Visual Studio Community

1. Download the installer from [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/).
2. Run the installer. Community is free for individuals and for the uses described on that page.
3. On the Workloads tab, select **.NET desktop development**. That workload includes the WPF tools this application needs.
4. Install. If .NET 10 is listed under Individual components and is not already selected, select the .NET 10 SDK as well.
5. Open `EqlLoadouts.slnx` from this folder. Visual Studio will restore the NuGet packages on first open.

## Build and run in Visual Studio

1. In Solution Explorer, confirm `EqlLoadouts.App` is the startup project.
2. Press F5.

F5 builds a debug copy named `EqlLoadouts.App.exe`. That copy needs the .NET desktop runtime installed by Visual Studio. It is not the single-file program produced by the publish script.

A debug build adds developer settings that a published build does not have: the Raw keys tab, hot button page and slot editing, suggested spell sets and gem suggestions, saving modified copies, and clearing saved classes.

## Publish a single executable

From this folder, in PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish-win-x64.ps1
```

`global.json` pins the build to .NET SDK 10.0.402 and does not allow a newer SDK to be substituted. The script checks `dotnet --version` and stops when that SDK is not the one selected. It then runs the tests in Release and publishes a self-contained Windows x64 program. The output folder is `artifacts\<version>`, using the `Version` in `src\EqlLoadouts.App\EqlLoadouts.App.csproj`. That version is 1.0.0, so the folder is `artifacts\1.0.0`. It contains:

- `EqlLoadouts.exe`, the program and the .NET runtime in one file
- `LICENSE`, which the splash screen opens
- `CONTRIBUTOR_AGREEMENT.md`
- `hash.txt`, with the SHA-256 of the exe and of each license file
- `Verify.ps1`, which checks each file against `expected_hash.txt`

If a test fails, the script stops and leaves the previous publish folder in place. The same source and the same .NET SDK produce the same exe. A different SDK produces a different exe. After the files are written, the script compares their hashes with `expected_hash.txt` in this folder and prints a warning when one does not match. The publish still finishes. Replace `expected_hash.txt` when a new publish is the hash later builds should match. Pass `-Output` to publish into a different folder. The output folder is removed and rewritten only after the tests pass.

To let PowerShell run local scripts without the bypass flag:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

You can then run `.\publish-win-x64.ps1` directly.

## Check a published exe

A publish folder contains `EqlLoadouts.exe`, `LICENSE`, `CONTRIBUTOR_AGREEMENT.md`, `hash.txt`, and `Verify.ps1`. `hash.txt` has one SHA-256 line per file:

```text
SHA256 EqlLoadouts.exe <hex>
SHA256 LICENSE <hex>
SHA256 CONTRIBUTOR_AGREEMENT.md <hex>
```

The hex is uppercase. From the repository root, check the published 1.0.0 files:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\artifacts\1.0.0\Verify.ps1
```

`Verify.ps1` hashes the exe and the two license files in its own folder. It compares each hash with `expected_hash.txt`. The script looks for that file beside itself first, then in parent folders. A match prints one line per file:

```text
EqlLoadouts.exe hash verified against expected hash. <hex>
```

When a hash does not match, the script prints a warning that the hashes do not match and not to run the application, then exits with an error. A matching result shows the files were not changed after `expected_hash.txt` was written. It does not, by itself, show they were built from this source.

To show the exe was built from the source, check out the commit for that version and publish again. `global.json` selects .NET SDK 10.0.402. A different SDK produces a different exe, and the publish script stops if `dotnet --version` is not 10.0.402. Publish into a separate folder so the version folder stays in place:

```powershell
dotnet --version
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish-win-x64.ps1 -Output .\artifacts\verify
powershell -NoProfile -ExecutionPolicy Bypass -File .\artifacts\verify\Verify.ps1
```

`Verify.ps1` compares the new files with the `expected_hash.txt` in that commit. That check passes only when that file records the exe published from that commit. Otherwise, compare the `EqlLoadouts.exe` line in the new `hash.txt` with the `hash.txt` from the original publish. A debug build from Visual Studio, or a publish made with a different SDK, will not match.

Git can change line endings on checkout. If only the license hashes differ, compare those files without converting line endings. `hash.txt` is written after the exe is built and is not inside the exe. `Verify.ps1` is copied in after `hash.txt` and is not one of the hashed files.

## License

Copyright (c) 2026 Generation X Technologies, Inc.

EQL Loadout Editor is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE). The same terms are published at <https://polyformproject.org/licenses/noncommercial/1.0.0>. You may use, change, and share the software for noncommercial purposes. You may not sell it or use it to make a profit. Commercial use is licensed separately, and commercial rights stay with Generation X Technologies, Inc.

Bug fixes and enhancements belong in a pull request. To accept a pull request, the company needs a license to the change, and the person who wrote it keeps the copyright. You license your contribution to the company under the [Contributor Agreement](CONTRIBUTOR_AGREEMENT.md). The company may include it in a commercial version and must also keep it under the project license in effect when you submit it.

This is a summary, not the full terms. A lawyer should review the license and the agreement before you rely on them.
