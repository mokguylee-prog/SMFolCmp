---
name: ui_refresh_after_operations
description: Preserves expanded folders while updating file system changes
metadata:
  type: project
  date: 2026-05-18
---

## Auto-Refresh UI After Copy/Delete Operations

### Problem
When performing copy or delete operations on files, the tree view would rebuild completely and collapse all expanded folders, causing the user to lose their navigation context.

### Solution
Implemented `RefreshUIWithExpandedState()` and `RestoreExpandedStateRecursive()` methods to:
1. Save the set of expanded folder paths before rebuild
2. Rebuild the comparison tree (calling `CompareFolders()`)
3. Restore expansion state to the saved folders

### Implementation Details

**SavedExpandedState:**
- `_expandedPaths`: HashSet<string> tracking which folder paths are expanded
- Updated in `RebuildFlatList()` using `AddFlatRecursive()`

**RefreshUIWithExpandedState():**
- Called from `CopyToOpposite_Click()` and `Delete_Click()` instead of `Compare_Click()`
- Saves expanded paths before comparison
- Rebuilds tree by calling `CompareFolders(leftPath, rightPath, _filterText)`
- Restores expanded state via `RestoreExpandedStateRecursive()`

**RestoreExpandedStateRecursive():**
- Traverses FileItem tree recursively
- Sets `IsExpanded = true` for items whose paths are in `_expandedPaths`
- Preserves user's navigation context across operations

### Result
Users can now expand/collapse folders, perform copy/delete operations, and the UI updates to reflect file system changes while maintaining their expanded folder state.
