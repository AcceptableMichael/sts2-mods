---
alwaysApply: true
---

# Background

https://github.com/Alchyr/ModTemplate-StS2/wiki

# ilspycmd

This command is in the Cursor allowlist:
`.\scripts\run_ilspycmd.ps1`

To minimize approvals:
1. Set `working_directory` to this repo (`sts2 mods`).
2. Always start the command with `.\scripts\run_ilspycmd.ps1` (never `foreach`, `cd`, or `& "full\path"`).
3. Do not use outer `foreach` loops — use `-Types` on the script instead.

## Single type

```powershell
.\scripts\run_ilspycmd.ps1 -t "MegaCrit.Sts2.Core.Nodes.Screens.Settings.NBgmVolumeSlider" -Pattern "OnValueChanged|VolumeBgm" -Context 0,8
```

## Multiple types (probe candidate namespaces)

```powershell
.\scripts\run_ilspycmd.ps1 -Types @(
  'MegaCrit.Sts2.Core.Nodes.NGlobalUi',
  'MegaCrit.Sts2.Core.Nodes.GlobalUi.NGlobalUi',
  'MegaCrit.Sts2.Core.Nodes.TopBar.NTopBar'
) -Pattern "^(namespace|public class)|Potion|TopBar|Refresh" -First 8
```

## Find which type exists (`-FoundOnly`)

```powershell
.\scripts\run_ilspycmd.ps1 -Types @(
  'MegaCrit.Sts2.Core.Nodes.Screens.NTopBar',
  'MegaCrit.Sts2.Core.Nodes.CommonUi.NTopBar'
) -Pattern "PotionContainer" -First 3 -FoundOnly
```

Parameters: `-t` / `-Type`, `-Types` (array), `-Pattern`, `-Context` (default `0,8`), `-First`, `-FoundOnly` (print only successful matches).
