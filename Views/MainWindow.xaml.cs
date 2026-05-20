using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMFolCmp.Models;
using Ookii.Dialogs.Wpf;
using Microsoft.Win32;

namespace SMFolCmp.Views
{
    public partial class MainWindow : Window
    {
        private enum FilterMode { All, Diff, Diff2 }

        private string _leftFolder, _rightFolder;
        private List<FileItem> _rootItems = new();
        private ObservableCollection<FileItem> _flatItems = new();
        private FilterMode _filterMode = FilterMode.All;
        private string _excludeFilePatterns = "";
        private string _excludeFolderPatterns = "";
        private List<string> _filePatternsList = new();
        private List<string> _folderPatternsList = new();
        private const string REG_PATH = @"Software\SMFolCmp";
        private bool _isSyncingScroll = false;
        private bool _isSyncingSelection = false;
        private ListBox _lastFocusedGrid;
        private bool _isDragSelecting = false;
        private int _dragStartIndex = -1;
        private bool _isComparing = false;
        private CancellationTokenSource? _compareCancellation;

        private sealed class PreciseCompareCandidate
        {
            public required FileItem Item { get; init; }
            public required string LeftPath { get; init; }
            public required string RightPath { get; init; }
        }

        public MainWindow(string leftFolder = null, string rightFolder = null)
        {
            InitializeComponent();
            SetWindowTitle();
            LeftGrid.ItemsSource = _flatItems;
            RightGrid.ItemsSource = _flatItems;
            KeyDown += (s, e) => { if (e.Key == Key.F5) { Compare_Click(null, null); e.Handled = true; } };
            Closed += (_, _) => _compareCancellation?.Cancel();

            // constructor에서 전달받은 폴더가 있으면 사용, 없으면 레지스트리에서 로드
            if (!string.IsNullOrEmpty(leftFolder) && !string.IsNullOrEmpty(rightFolder))
            {
                _leftFolder = leftFolder;
                _rightFolder = rightFolder;
                LeftPathBox.Text = leftFolder;
                RightPathBox.Text = rightFolder;
                LeftPathBox.Foreground = System.Windows.Media.Brushes.White;
                RightPathBox.Foreground = System.Windows.Media.Brushes.White;
                SaveConfig();
                Dispatcher.InvokeAsync(() => Compare_Click(null, null), System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                LoadConfig();
                // config에서 로드한 폴더들로 자동 비교
                if (!string.IsNullOrEmpty(_leftFolder) && !string.IsNullOrEmpty(_rightFolder))
                {
                    Dispatcher.InvokeAsync(() => Compare_Click(null, null), System.Windows.Threading.DispatcherPriority.Normal);
                }
            }
        }

        private void LoadConfig()
        {
            string leftFolder = GetRegValue("LeftFolder", "");
            string rightFolder = GetRegValue("RightFolder", "");
            string excludeFilePatterns = GetRegValue("ExcludeFilePatterns", "");
            string excludeFolderPatterns = GetRegValue("ExcludeFolderPatterns", "");

            if (!string.IsNullOrEmpty(leftFolder) && Directory.Exists(leftFolder))
            {
                _leftFolder = leftFolder;
                LeftPathBox.Text = leftFolder;
                LeftPathBox.Foreground = System.Windows.Media.Brushes.White;
            }

            if (!string.IsNullOrEmpty(rightFolder) && Directory.Exists(rightFolder))
            {
                _rightFolder = rightFolder;
                RightPathBox.Text = rightFolder;
                RightPathBox.Foreground = System.Windows.Media.Brushes.White;
            }

            _excludeFilePatterns = excludeFilePatterns;
            _excludeFolderPatterns = excludeFolderPatterns;
            ExcludeFilePatternsBox.Text = excludeFilePatterns;
            ExcludeFolderPatternsBox.Text = excludeFolderPatterns;
            ParsePatterns();
        }

        private void SaveConfig()
        {
            SetRegValue("LeftFolder", _leftFolder ?? "");
            SetRegValue("RightFolder", _rightFolder ?? "");
            SetRegValue("ExcludeFilePatterns", _excludeFilePatterns ?? "");
            SetRegValue("ExcludeFolderPatterns", _excludeFolderPatterns ?? "");
        }

        private string GetRegValue(string valueName, string defaultValue = "")
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REG_PATH))
                {
                    return key?.GetValue(valueName) as string ?? defaultValue;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        private void SetRegValue(string valueName, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_PATH))
                {
                    key?.SetValue(valueName, value);
                }
            }
            catch { }
        }

        private void SetLeftFolder(string folder)
        {
            _leftFolder = folder;
            LeftPathBox.Text = folder;
            LeftPathBox.Foreground = System.Windows.Media.Brushes.White;
            SaveConfig();
        }

        private void SetRightFolder(string folder)
        {
            _rightFolder = folder;
            RightPathBox.Text = folder;
            RightPathBox.Foreground = System.Windows.Media.Brushes.White;
            SaveConfig();
        }

        private void SelectLeftFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var p = PickFolder();
                if (p != null) SetLeftFolder(p);
            }
            catch
            {
                MessageBox.Show("폴더 경로를 직접 입력하세요 (예: D:\\utils\\FoldA)", "폴더 선택");
            }
        }

        private void SelectRightFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var p = PickFolder();
                if (p != null) SetRightFolder(p);
            }
            catch
            {
                MessageBox.Show("폴더 경로를 직접 입력하세요 (예: D:\\utils\\FoldB)", "폴더 선택");
            }
        }

        private void PathBox_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void PathBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void LeftPathBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    var path = files[0];
                    if (Directory.Exists(path))
                    {
                        SetLeftFolder(path);
                        Compare_Click(null, null);
                    }
                }
            }
        }

        private void RightPathBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    var path = files[0];
                    if (Directory.Exists(path))
                    {
                        SetRightFolder(path);
                        Compare_Click(null, null);
                    }
                }
            }
        }

        private string PickFolder()
        {
            var dlg = new VistaFolderBrowserDialog { Description = "폴더를 선택하세요", UseDescriptionForTitle = true };
            return dlg.ShowDialog(this) == true ? dlg.SelectedPath : null;
        }

        private async void Compare_Click(object sender, RoutedEventArgs e)
        {
            if (_isComparing)
            {
                RequestCompareCancellation();
                return;
            }

            if (string.IsNullOrEmpty(_leftFolder) || string.IsNullOrEmpty(_rightFolder))
            { MessageBox.Show("두 폴더를 모두 선택하세요", "경고", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var expandedPaths = new HashSet<string>();
            foreach (var item in _flatItems)
            {
                if (item.IsExpanded && item.IsDirectory)
                {
                    expandedPaths.Add(item.LeftPath ?? item.RightPath ?? "");
                }
            }

            var cancellation = new CancellationTokenSource();
            _compareCancellation = cancellation;
            SetCompareRunningState(true);
            StatusText.Text = "빠른 폴더 비교 중...";

            var newRootItems = new List<FileItem>();
            var preciseCandidates = new List<PreciseCompareCandidate>();
            bool quickResultDisplayed = false;
            try
            {
                await Task.Run(() =>
                    CompareFoldersByMetadata(_leftFolder, _rightFolder, newRootItems, preciseCandidates, 0, cancellation.Token),
                    cancellation.Token);

                _rootItems = newRootItems;
                RestoreExpandedStateRecursive(_rootItems, expandedPaths);
                RebuildFlatList();
                quickResultDisplayed = true;

                if (preciseCandidates.Count == 0)
                {
                    UpdateStatusText();
                    return;
                }

                UpdateStatusText($" | 정밀 비교 중: 0/{preciseCandidates.Count}");
                var progress = new Progress<int>(completed =>
                    UpdateStatusText($" | 정밀 비교 중: {completed}/{preciseCandidates.Count}"));

                var preciseStatuses = await Task.Run(() =>
                    CompareFilesPrecisely(preciseCandidates, cancellation.Token, progress),
                    cancellation.Token);

                ApplyPreciseStatuses(preciseStatuses);
                RecalculateFolderStatuses(_rootItems);
                RebuildFlatList();
                UpdateStatusText();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = quickResultDisplayed
                    ? "정밀 비교가 중지되었습니다. 빠른 비교 결과만 표시 중입니다."
                    : "폴더 비교가 중지되었습니다.";
            }
            finally
            {
                if (_compareCancellation == cancellation)
                {
                    _compareCancellation.Dispose();
                    _compareCancellation = null;
                    SetCompareRunningState(false);
                }
            }
        }

        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_leftFolder) || string.IsNullOrEmpty(_rightFolder))
            {
                MessageBox.Show("두 폴더를 모두 선택하세요", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var temp = _leftFolder;
            _leftFolder = _rightFolder;
            _rightFolder = temp;

            LeftPathBox.Text = _leftFolder;
            RightPathBox.Text = _rightFolder;

            SaveConfig();
            Compare_Click(null, null);
        }

        private void RefreshUIWithExpandedState()
        {
            Compare_Click(null, null);
        }

        private void RestoreExpandedStateRecursive(List<FileItem> items, HashSet<string> expandedPaths)
        {
            foreach (var item in items)
            {
                if (item.IsDirectory && (expandedPaths.Contains(item.LeftPath ?? "") || expandedPaths.Contains(item.RightPath ?? "")))
                {
                    item.IsExpanded = true;
                }

                if (item.IsDirectory && item.Children.Count > 0)
                {
                    RestoreExpandedStateRecursive(item.Children, expandedPaths);
                }
            }
        }

        private void CompareFoldersByMetadata(
            string left,
            string right,
            IList<FileItem> target,
            IList<PreciseCompareCandidate> preciseCandidates,
            int depth,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var le = GetEntries(left, cancellationToken);
            var re = GetEntries(right, cancellationToken);
            foreach (var name in le.Keys.Union(re.Keys).OrderBy(n => n))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool il = le.TryGetValue(name, out var li); bool ir = re.TryGetValue(name, out var ri);
                var item = new FileItem { Name = name, Depth = depth };
                if (il) { item.IsDirectory = li is DirectoryInfo; item.LeftPath = li.FullName; item.LeftSize = li is FileInfo lf ? lf.Length : 0; item.LeftModified = li.LastWriteTime; }
                if (ir) { item.IsDirectory = ri is DirectoryInfo; item.RightPath = ri.FullName; item.RightSize = ri is FileInfo rf ? rf.Length : 0; item.RightModified = ri.LastWriteTime; }
                item.Status = il && ir
                    ? (item.IsDirectory ? CompareStatus.Identical : CompareFilesByMetadata(item, preciseCandidates))
                    : il ? CompareStatus.LeftOnly : CompareStatus.RightOnly;

                if (item.IsDirectory)
                {
                    CompareFoldersByMetadata(item.LeftPath ?? "", item.RightPath ?? "", item.Children, preciseCandidates, depth + 1, cancellationToken);
                    if (item.Children.Count > 0)
                        item.Status = DetermineFolderStatus(item.Children, item.Status);
                }

                target.Add(item);
            }
        }

        private Dictionary<string, FileSystemInfo> GetEntries(string dir, CancellationToken cancellationToken)
        {
            var d = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    d[f.Name] = f;
                }
            }
            return d;
        }

        private CompareStatus CompareFilesByMetadata(FileItem item, IList<PreciseCompareCandidate> preciseCandidates)
        {
            if (item.LeftSize != item.RightSize) return CompareStatus.Modified;

            preciseCandidates.Add(new PreciseCompareCandidate
            {
                Item = item,
                LeftPath = item.LeftPath,
                RightPath = item.RightPath
            });

            return item.LeftModified != item.RightModified
                ? CompareStatus.DateOnly
                : CompareStatus.Identical;
        }

        private Dictionary<FileItem, CompareStatus> CompareFilesPrecisely(
            IReadOnlyCollection<PreciseCompareCandidate> candidates,
            CancellationToken cancellationToken,
            IProgress<int>? progress)
        {
            var statuses = new Dictionary<FileItem, CompareStatus>();
            int completed = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                statuses[candidate.Item] = CompareFileContent(candidate, cancellationToken);
                completed++;
                if (completed == candidates.Count || completed % 25 == 0)
                    progress?.Report(completed);
            }

            return statuses;
        }

        private CompareStatus CompareFileContent(PreciseCompareCandidate candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool sameContent = FilesHaveSameContent(candidate.LeftPath, candidate.RightPath, cancellationToken);
            if (!sameContent) return CompareStatus.Modified;
            if (candidate.Item.LeftModified != candidate.Item.RightModified) return CompareStatus.DateOnly;
            return CompareStatus.Identical;
        }

        private bool FilesHaveSameContent(string a, string b, CancellationToken cancellationToken)
        {
            const int BUF = 65536;
            using var f1 = File.OpenRead(a); using var f2 = File.OpenRead(b);
            var b1 = new byte[BUF]; var b2 = new byte[BUF]; int r1;
            while ((r1 = f1.Read(b1, 0, BUF)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int r2 = f2.Read(b2, 0, BUF);
                if (r1 != r2) return false;
                if (!b1.AsSpan(0, r1).SequenceEqual(b2.AsSpan(0, r2))) return false;
            }
            return true;
        }

        private void RequestCompareCancellation()
        {
            if (_compareCancellation == null || _compareCancellation.IsCancellationRequested) return;

            _compareCancellation.Cancel();
            CompareBtn.Content = "⏳ Stopping...";
            CompareBtn.ToolTip = "Stopping comparison";
            StatusText.Text = "폴더 비교 중지 요청 중...";
        }

        private void SetCompareRunningState(bool isRunning)
        {
            _isComparing = isRunning;
            CompareBtn.Content = isRunning ? "■ Stop (F5)" : "⟳ F5";
            CompareBtn.ToolTip = isRunning ? "Stop comparison (F5)" : "Refresh (F5)";
            CompareBtn.Background = new SolidColorBrush(isRunning
                ? Color.FromRgb(192, 57, 43)
                : Color.FromRgb(30, 126, 52));
        }

        private void ApplyPreciseStatuses(IReadOnlyDictionary<FileItem, CompareStatus> preciseStatuses)
        {
            foreach (var (item, status) in preciseStatuses)
                item.Status = status;
        }

        private void RecalculateFolderStatuses(IEnumerable<FileItem> items)
        {
            foreach (var item in items)
            {
                if (!item.IsDirectory) continue;

                RecalculateFolderStatuses(item.Children);
                if (item.Children.Count > 0)
                    item.Status = DetermineFolderStatus(item.Children, item.Status);
            }
        }

        private CompareStatus DetermineFolderStatus(IEnumerable<FileItem> children, CompareStatus fallbackStatus)
        {
            bool hasModified = false;
            bool hasDateOnly = false;
            bool hasLeftOnly = false;
            bool hasRightOnly = false;

            foreach (var child in children)
            {
                switch (child.Status)
                {
                    case CompareStatus.Modified: hasModified = true; break;
                    case CompareStatus.DateOnly: hasDateOnly = true; break;
                    case CompareStatus.LeftOnly: hasLeftOnly = true; break;
                    case CompareStatus.RightOnly: hasRightOnly = true; break;
                }
            }

            if (hasModified || hasLeftOnly && hasRightOnly) return CompareStatus.Modified;
            if (hasLeftOnly) return CompareStatus.LeftOnly;
            if (hasRightOnly) return CompareStatus.RightOnly;
            if (hasDateOnly) return CompareStatus.DateOnly;
            return fallbackStatus == CompareStatus.LeftOnly || fallbackStatus == CompareStatus.RightOnly
                ? fallbackStatus
                : CompareStatus.Identical;
        }

        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as ListBox;
            if (grid?.SelectedItem is FileItem item && item.IsDirectory)
            {
                if (item.HasChildren)
                {
                    item.IsExpanded = !item.IsExpanded;
                    RebuildFlatList();
                }
                return;
            }
            OpenSelectedCompare();
        }

        private void OpenCompare_Click(object sender, RoutedEventArgs e) => OpenSelectedCompare();

        private void OpenSelectedCompare()
        {
            FileItem item = LeftGrid.SelectedItem as FileItem ?? RightGrid.SelectedItem as FileItem;
            if (item is null || item.IsDirectory) return;
            new CompareWindow(item.LeftPath, item.RightPath, _leftFolder, _rightFolder) { Owner = this }.Show();
        }

        public void SelectAndCompareFiles(string leftPath, string rightPath)
        {
            if (string.IsNullOrEmpty(leftPath) || string.IsNullOrEmpty(rightPath)) return;

            var item = _flatItems.FirstOrDefault(f => f.LeftPath == leftPath && f.RightPath == rightPath);
            if (item != null)
            {
                LeftGrid.SelectedItem = item;
                RightGrid.SelectedItem = item;
                Dispatcher.InvokeAsync(() => Compare_Click(null, null));
            }
        }

        private void ExpandIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FileItem item && item.HasChildren)
            {
                item.IsExpanded = !item.IsExpanded;
                RebuildFlatList();
                e.Handled = true;
            }
        }

        private ListBox GetSourceGrid() => _lastFocusedGrid ?? LeftGrid;

        private void CopyToOpposite_Click(object sender, RoutedEventArgs e)
        {
            var src = GetSourceGrid();
            bool fromLeft = src == LeftGrid;
            var items = src.SelectedItems.Cast<FileItem>().ToList();
            string srcRoot = fromLeft ? _leftFolder : _rightFolder;
            string dstRoot = fromLeft ? _rightFolder : _leftFolder;

            foreach (var item in items)
            {
                string path = fromLeft ? item.LeftPath : item.RightPath;
                if (path == null) continue;
                try
                {
                    string rel = Path.GetRelativePath(srcRoot, path);
                    string dst = Path.Combine(dstRoot, rel);
                    if (item.IsDirectory)
                        CopyDirectoryRecursive(path, dst);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(path, dst, true);
                    }
                }
                catch (Exception ex) { MessageBox.Show($"{item.Name}: {ex.Message}"); }
            }
            RefreshUIWithExpandedState();
        }

        private void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }

        private void RebuildFlatList()
        {
            _flatItems.Clear();
            var roots = GetVisibleItems(_rootItems);
            for (int i = 0; i < roots.Count; i++)
                AddFlatRecursive(roots[i], new List<bool>(), i == roots.Count - 1);
        }

        private void AddFlatRecursive(FileItem item, List<bool> ancestorHasNextVisibleSibling, bool isLastVisibleSibling)
        {
            item.AncestorHasNextVisibleSibling = ancestorHasNextVisibleSibling;
            item.IsLastVisibleSibling = isLastVisibleSibling;
            _flatItems.Add(item);

            if (item.IsExpanded && item.IsDirectory)
            {
                var children = GetVisibleItems(item.Children);
                for (int i = 0; i < children.Count; i++)
                {
                    var childAncestors = new List<bool>(ancestorHasNextVisibleSibling)
                    {
                        !isLastVisibleSibling
                    };
                    AddFlatRecursive(children[i], childAncestors, i == children.Count - 1);
                }
            }
        }

        private List<FileItem> GetVisibleItems(IEnumerable<FileItem> items)
        {
            return items
                .Where(item => IsItemVisible(item))
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name)
                .ToList();
        }

        private bool IsItemVisible(FileItem item)
        {
            if (item.IsDirectory && IsFolderExcluded(item.Name)) return false;
            if (!item.IsDirectory && IsFileExcluded(item.Name)) return false;

            return _filterMode switch
            {
                FilterMode.All => true,
                FilterMode.Diff => item.Status != CompareStatus.Identical
                    || item.IsDirectory && HasMatchingDescendants(item),
                FilterMode.Diff2 => item.Status != CompareStatus.Identical && item.Status != CompareStatus.DateOnly
                    || item.IsDirectory && HasMatchingDescendants(item),
                _ => true
            };
        }

        private bool HasMatchingDescendants(FileItem item)
        {
            foreach (var child in item.Children)
            {
                if (child.IsDirectory && IsFolderExcluded(child.Name)) continue;
                if (!child.IsDirectory && IsFileExcluded(child.Name)) continue;

                bool childMatches = _filterMode switch
                {
                    FilterMode.All => true,
                    FilterMode.Diff => child.Status != CompareStatus.Identical,
                    FilterMode.Diff2 => child.Status != CompareStatus.Identical && child.Status != CompareStatus.DateOnly,
                    _ => true
                };
                if (childMatches) return true;
                if (child.IsDirectory && HasMatchingDescendants(child)) return true;
            }
            return false;
        }

        private void UpdateStatusText(string suffix = "")
        {
            int id = 0, da = 0, mo = 0, lo = 0, ro = 0, total = 0;
            CountRecursive(_rootItems, ref id, ref da, ref mo, ref lo, ref ro, ref total);
            StatusText.Text = $"총 {total}개 | 동일: {id}  변경: {mo}  날짜만: {da}  왼쪽만: {lo}  오른쪽만: {ro}{suffix}";
            FilterAllBtn.Content = $"All ({total})";
            FilterDiffBtn.Content = $"Diff ({mo + da + lo + ro})";
            FilterDiff2Btn.Content = $"Diff2 ({mo + lo + ro})";
        }

        private void CountRecursive(IEnumerable<FileItem> items, ref int id, ref int da, ref int mo, ref int lo, ref int ro, ref int total)
        {
            foreach (var item in items)
            {
                total++;
                switch (item.Status)
                {
                    case CompareStatus.Identical: id++; break;
                    case CompareStatus.DateOnly: da++; break;
                    case CompareStatus.Modified: mo++; break;
                    case CompareStatus.LeftOnly: lo++; break;
                    case CompareStatus.RightOnly: ro++; break;
                }
                if (item.IsDirectory && item.Children.Count > 0)
                    CountRecursive(item.Children, ref id, ref da, ref mo, ref lo, ref ro, ref total);
            }
        }

        private void Setup_Click(object sender, RoutedEventArgs e)
        {
            var setupWindow = new SetupWindow { Owner = this };
            setupWindow.ShowDialog();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var src = GetSourceGrid();
            bool isLeft = src == LeftGrid;
            var items = src.SelectedItems.Cast<FileItem>().ToList();
            var files = items.Where(i => !i.IsDirectory).ToList();
            var dirs = items.Where(i => i.IsDirectory).ToList();

            if (files.Count > 0)
            {
                var dlg = new ConfirmDeleteDialog($"{files.Count}개 파일을 삭제합니다.");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    foreach (var f in files)
                    {
                        string path = isLeft ? f.LeftPath : f.RightPath;
                        if (path != null && File.Exists(path)) File.Delete(path);
                    }
                }
            }

            foreach (var d in dirs)
            {
                string path = isLeft ? d.LeftPath : d.RightPath;
                if (path == null || !Directory.Exists(path)) continue;
                var dlg = new ConfirmDeleteDialog($"폴더를 삭제합니다:\n{path}");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                    Directory.Delete(path, true);
            }
            RefreshUIWithExpandedState();
        }

        private void FileGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                _lastFocusedGrid = sender as ListBox;
                Delete_Click(sender, null);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                Compare_Click(sender, null);
                e.Handled = true;
            }
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.All;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        private void FilterDiff_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.Diff;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        private void FilterDiff2_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.Diff2;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        private void UpdateFilterButtonStyles()
        {
            var activeColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219));
            var inactiveColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 123, 213));

            FilterAllBtn.Background = _filterMode == FilterMode.All ? activeColor : inactiveColor;
            FilterDiffBtn.Background = _filterMode == FilterMode.Diff ? activeColor : inactiveColor;
            FilterDiff2Btn.Background = _filterMode == FilterMode.Diff2 ? activeColor : inactiveColor;
        }

        private void LeftGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastFocusedGrid = LeftGrid;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                var item = listBox?.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
                if (item != null)
                {
                    var index = listBox.Items.IndexOf(((FrameworkElement)item).DataContext);
                    if (index >= 0)
                    {
                        bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                        bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                        if (shiftPressed && listBox.SelectedIndex >= 0)
                        {
                            int start = Math.Min(listBox.SelectedIndex, index);
                            int end = Math.Max(listBox.SelectedIndex, index);
                            listBox.SelectedItems.Clear();
                            for (int i = start; i <= end; i++)
                                listBox.SelectedItems.Add(listBox.Items[i]);
                            e.Handled = true;
                        }
                        else if (ctrlPressed)
                        {
                            if (listBox.SelectedItems.Contains(listBox.Items[index]))
                                listBox.SelectedItems.Remove(listBox.Items[index]);
                            else
                                listBox.SelectedItems.Add(listBox.Items[index]);
                            e.Handled = true;
                        }
                        else
                        {
                            _isDragSelecting = true;
                            _dragStartIndex = index;
                        }
                    }
                }
            }
        }

        private void RightGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastFocusedGrid = RightGrid;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                var item = listBox?.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
                if (item != null)
                {
                    var index = listBox.Items.IndexOf(((FrameworkElement)item).DataContext);
                    if (index >= 0)
                    {
                        bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                        bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                        if (shiftPressed && listBox.SelectedIndex >= 0)
                        {
                            int start = Math.Min(listBox.SelectedIndex, index);
                            int end = Math.Max(listBox.SelectedIndex, index);
                            listBox.SelectedItems.Clear();
                            for (int i = start; i <= end; i++)
                                listBox.SelectedItems.Add(listBox.Items[i]);
                            e.Handled = true;
                        }
                        else if (ctrlPressed)
                        {
                            if (listBox.SelectedItems.Contains(listBox.Items[index]))
                                listBox.SelectedItems.Remove(listBox.Items[index]);
                            else
                                listBox.SelectedItems.Add(listBox.Items[index]);
                            e.Handled = true;
                        }
                        else
                        {
                            _isDragSelecting = true;
                            _dragStartIndex = index;
                        }
                    }
                }
            }
        }

        private void LeftGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragSelecting || _dragStartIndex < 0) return;
            var listBox = sender as ListBox;
            if (listBox == null || _dragStartIndex >= listBox.Items.Count)
            {
                _isDragSelecting = false;
                _dragStartIndex = -1;
                return;
            }

            var item = listBox?.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
            if (item != null)
            {
                var index = listBox.Items.IndexOf(((FrameworkElement)item).DataContext);
                if (index >= 0)
                {
                    int start = Math.Min(_dragStartIndex, index);
                    int end = Math.Max(_dragStartIndex, index);
                    listBox.SelectedItems.Clear();
                    for (int i = start; i <= end; i++)
                        listBox.SelectedItems.Add(listBox.Items[i]);
                }
            }
        }

        private void RightGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragSelecting || _dragStartIndex < 0) return;
            var listBox = sender as ListBox;
            if (listBox == null || _dragStartIndex >= listBox.Items.Count)
            {
                _isDragSelecting = false;
                _dragStartIndex = -1;
                return;
            }

            var item = listBox?.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
            if (item != null)
            {
                var index = listBox.Items.IndexOf(((FrameworkElement)item).DataContext);
                if (index >= 0)
                {
                    int start = Math.Min(_dragStartIndex, index);
                    int end = Math.Max(_dragStartIndex, index);
                    listBox.SelectedItems.Clear();
                    for (int i = start; i <= end; i++)
                        listBox.SelectedItems.Add(listBox.Items[i]);
                }
            }
        }

        private void LeftGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e) => _isDragSelecting = false;
        private void RightGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e) => _isDragSelecting = false;

        private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool fromLeft = GetSourceGrid() == LeftGrid;
            var menu = (sender as FrameworkElement)?.ContextMenu;
            if (menu?.Items[0] is MenuItem copyItem)
                copyItem.Header = fromLeft ? "→ 오른쪽으로 복사" : "← 왼쪽으로 복사";
        }

        private void LeftGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            GetScrollViewer(RightGrid)?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        private void RightGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            GetScrollViewer(LeftGrid)?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        private void LeftGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return;
            _isSyncingSelection = true;
            RightGrid.SelectedIndex = LeftGrid.SelectedIndex;
            _isSyncingSelection = false;
        }

        private void RightGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return;
            _isSyncingSelection = true;
            LeftGrid.SelectedIndex = RightGrid.SelectedIndex;
            _isSyncingSelection = false;
        }

        private ScrollViewer GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ParsePatterns()
        {
            _filePatternsList = _excludeFilePatterns
                .Split(';')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            _folderPatternsList = _excludeFolderPatterns
                .Split(';')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        private bool MatchesPattern(string name, List<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (WildcardMatch(name, pattern))
                    return true;
            }
            return false;
        }

        private bool WildcardMatch(string filename, string pattern)
        {
            int fileIdx = 0, patternIdx = 0;
            int fileLength = filename.Length, patternLength = pattern.Length;
            int fileIdxStar = -1, patternIdxStar = -1;

            while (fileIdx < fileLength)
            {
                if (patternIdx < patternLength)
                {
                    if (pattern[patternIdx] == '*')
                    {
                        fileIdxStar = fileIdx;
                        patternIdxStar = patternIdx;
                        patternIdx++;
                    }
                    else if (pattern[patternIdx] == '?' || pattern[patternIdx] == filename[fileIdx])
                    {
                        fileIdx++;
                        patternIdx++;
                    }
                    else if (fileIdxStar != -1)
                    {
                        fileIdxStar++;
                        fileIdx = fileIdxStar;
                        patternIdx = patternIdxStar + 1;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (fileIdxStar != -1)
                {
                    fileIdxStar++;
                    fileIdx = fileIdxStar;
                    patternIdx = patternIdxStar + 1;
                }
                else
                {
                    return false;
                }
            }

            while (patternIdx < patternLength && pattern[patternIdx] == '*')
                patternIdx++;

            return patternIdx == patternLength;
        }

        private bool IsFileExcluded(string fileName)
        {
            return MatchesPattern(fileName, _filePatternsList);
        }

        private bool IsFolderExcluded(string folderName)
        {
            return MatchesPattern(folderName, _folderPatternsList);
        }

        private void SaveFilePatterns_Click(object sender, RoutedEventArgs e)
        {
            _excludeFilePatterns = ExcludeFilePatternsBox.Text;
            ParsePatterns();
            SaveConfig();
            RebuildFlatList();
        }

        private void SaveFolderPatterns_Click(object sender, RoutedEventArgs e)
        {
            _excludeFolderPatterns = ExcludeFolderPatternsBox.Text;
            ParsePatterns();
            SaveConfig();
            RebuildFlatList();
        }

        private void SetWindowTitle()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var buildDate = string.IsNullOrEmpty(exePath)
                ? DateTime.Now.ToString("yyyy-MM-dd")
                : System.IO.File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd");
            Title = $"SMFolCmp v{version} ({buildDate})";
        }
    }
}


