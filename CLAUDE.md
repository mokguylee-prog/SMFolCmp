# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SMFolCmp is a Windows folder comparison utility built with WPF (.NET 8.0). It compares two folders/directories and shows file differences, with support for line-by-line text file comparison.

## Build & Run

### Build (Release)
```powershell
.\build.ps1
```
Creates a self-contained, single-file executable at `D:\utils\SMFolCmp\SMFolCmp.exe` (~146 MB).

### Build (Debug)
```powershell
.\build.ps1 -Configuration Debug
```

### Run
```powershell
.\bin\Release\net8.0-windows\win-x64\publish\SMFolCmp.exe
```
Or: `D:\utils\SMFolCmp\SMFolCmp.exe` (Release build)

To register Windows Explorer context menu, click "⚙ Setup" button in the app and select "Register".

## Architecture

### Two-Window Design

**MainWindow (Folder Comparison)**
- Selects two folders and displays file comparison results in a DataGrid
- `FileItem` model tracks: Name, Paths, Size, Modified Date, Status (Identical/Modified/LeftOnly/RightOnly)
- Loads/saves last selected folders in `foldercomparer.cfg` for quick resumption
- Double-clicking a file row opens CompareWindow for line-by-line diff

**CompareWindow (Text File Comparison)**
- Displays side-by-side line-by-line comparison of two text files
- **DiffEngine**: Custom diff algorithm using LCS (longest common subsequence)
  - Returns operations: Equal, Delete, Insert, Change
  - Called from `LoadAndDiff()` to populate `_leftDiff` and `_rightDiff` collections
- **Line Numbers**: Implemented via `_leftNums` and `_rightNums` ObservableCollections
  - Line index shown only for real lines (padded lines show empty)
- **Color-coded diffs**: 
  - Transparent = Equal
  - Red tint = Delete
  - Green tint = Insert
  - Yellow tint = Change
  - Gray tint = Padding (for missing lines)
- **Synchronized scrolling**: Both sides scroll together when one is scrolled
- **Multi-line selection**: Click or drag to select one or more lines (blue highlight)
- **Context menu**: Right-click menu with copy-to-opposite and delete options
- **File modification indicator**: Asterisk (*) on title when either file is modified; cleared on save (Ctrl+S)
- **Edit support**: F2 opens `EditLineDialog` for inline line editing; Ctrl+S saves both files

### Key Classes

- **FileItem** (Models/): MVVM-compliant model with INotifyPropertyChanged
  - Status property drives row background color and status text
- **DiffLine**: Implements INotifyPropertyChanged. Holds text, background color, foreground color, and metadata for each displayed line. Used for drag-select highlighting and selection range tracking.
- **DiffEngine**: Static class with `Compute()` method (definition is truncated in source—check the full file for algorithm details)
- **EditLineDialog**: Runtime-constructed dialog for single-line editing (no XAML, pure C#)

## Key Implementation Details

### Folder Selection
- Uses `Ookii.Dialogs.Wpf.VistaFolderBrowserDialog` for native folder picker
- Falls back to manual path input if picker fails

### Config Persistence
- Simple text file format: `foldercomparer.cfg` with two lines (left path, right path)
- Auto-loads on startup; if both folders exist, triggers Compare_Click immediately

### File Access
- All file I/O in `LoadFile()` and `SaveFiles()` methods
- No streaming—entire files loaded into memory as line arrays

### Styling
- Dark theme (RGB 30,30,46 background) for CompareWindow
- Light theme for MainWindow (default WPF)
- All colors defined as static Brush instances in CompareWindow for reuse

## Post-Build Hook

`SMFolCmp.csproj` includes an `AfterTargets="Build"` hook that runs `copy-to-utils.ps1`. This copies the build output to an external location (check the script for details).

## Dependencies

- **Ookii.Dialogs.Wpf 5.0.1**: Provides Vista-style folder and file dialogs

## When to Ask for Clarification

- **DiffEngine algorithm**: The diff computation method is truncated in the file reads. If implementing diff features, read the full `CompareWindow.xaml.cs` to understand the complete algorithm.
- **Config file location**: Currently uses relative path `foldercomparer.cfg`. No roaming profile support yet.
- **Context menu registration**: Implemented in `SetupWindow.xaml.cs`. Use the app's Setup dialog to register/unregister.
