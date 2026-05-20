---
name: unified_context_menu
description: Unified single SMFolCmp context menu with intelligent 1-folder vs 2-folder detection
metadata:
  type: project
  date: 2026-05-18
---

## Unified Context Menu Implementation

### Overview
Replaced the previous dual-menu approach (SMFolCmp with Left + SMFolCmp and Compare) with a single unified "SMFolCmp" menu that intelligently handles both single-folder and multi-folder scenarios.

### Changes Made

**1. SetupWindow.xaml.cs**
- `RegisterContextMenu()`: Creates single unified "SMFolCmp" menu (no prefix)
  - Calls program with: `SMFolCmp.exe "%1"`
  - No PowerShell script involved
  
- `IsContextMenuRegistered()`: Updated to check only for "SMFolCmp" key
  - Previously checked for both SMFolCmpLeft and SMFolCmpCompare
  
- `UnregisterContextMenu()`: Cleans up all old keys (for backwards compatibility) plus new SMFolCmp key

**2. App.xaml.cs OnStartup()**
- Removed old "left:" and "compare:" prefix handling
- Implemented intelligent 1 vs 2 folder detection using registry timestamps:
  - Uses `PendingFolder` and `PendingTime` registry values
  - Time window: 3 seconds between first and second execution

### Behavior Scenarios

#### Scenario 1: Single Folder Selected
```
User selects 1 folder → right-click → "SMFolCmp"
↓
First execution:
  - No pending folder exists
  - Saves folder path to registry as PendingFolder
  - Shows: "왼쪽 폴더로 저장되었습니다. {path}. 다른 폴더를 선택하여 비교하세요."
  - App exits
```

#### Scenario 2: Two Folders Selected
```
User selects 2 folders → right-click → "SMFolCmp"
↓
First execution (Folder A):
  - No pending folder exists
  - Saves Folder A as PendingFolder with timestamp
  - Shows message, app exits
  
Second execution (Folder B, within 3 seconds):
  - Finds PendingFolder (Folder A) still valid
  - Uses A as left folder, B as right folder
  - Opens comparison window with both folders
  - Clears PendingFolder to reset state
```

#### Scenario 3: Second Folder Takes Too Long (> 3 seconds)
```
If user waits > 3 seconds before selecting second folder:
  - Time window expired
  - Second execution is treated as new "first execution"
  - The second folder becomes the new PendingFolder
  - Behavior same as Scenario 1
```

### Registry Keys

**Unified Menu:**
```
HKEY_CURRENT_USER\Software\Classes\Directory\shell\SMFolCmp
└── Default: "SMFolCmp"
└── Icon: (path to SMFolCmp.exe or .ico)
└── command
    └── Default: "D:\utils\SMFolCmp\SMFolCmp.exe" "%1"
```

**Application Config:**
```
HKEY_CURRENT_USER\Software\SMFolCmp
├── LeftFolder: (last left folder path)
├── RightFolder: (last right folder path)
├── PendingFolder: (temporary storage during 2-folder selection)
└── PendingTime: (timestamp in Ticks)
```

### Testing Checklist

- [ ] Register context menu via Setup dialog
- [ ] Test single folder: Right-click 1 folder → shows SMFolCmp menu → click → shows save message
- [ ] Test two folders quick: Select 2 folders → right-click → first saves, second opens comparison (within 3 sec)
- [ ] Test timeout: Select 1 folder, wait 3+ seconds, select another → each treated as separate first execution
- [ ] Unregister: Verify old SMFolCmpLeft/SMFolCmpCompare keys are removed
- [ ] Auto-load: Verify MainWindow loads saved left/right folders on startup with no parameters

### Notes

- Time window (3 seconds) is generous for typical user selection speed
- App exits cleanly after first execution, no zombie processes
- Message guides user behavior appropriately for both single and dual folder scenarios
- Previous PowerShell script infrastructure no longer needed
