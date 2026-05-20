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
        private enum FilterMode { All, Diff, Diff2, Same }

        private string? _leftFolder, _rightFolder;
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
        private ListBox? _lastFocusedGrid;
        private bool _isDragSelecting = false;
        private int _dragStartIndex = -1;
        private bool _isComparing = false;
        private CancellationTokenSource? _compareCancellation;
        private FileItem? _compareToSourceItem;
        private bool _compareToMode = false;
        private string? _compareToSourcePath;
        private ListBox? _compareToSourceGrid;

        private sealed class PreciseCompareCandidate
        {
            public required FileItem Item { get; init; }
            public required string LeftPath { get; init; }
            public required string RightPath { get; init; }
        }

        // 생성자: 폴더 경로를 초기화하고 UI 설정
        public MainWindow(string? leftFolder = null, string? rightFolder = null)
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

        // 레지스트리에서 저장된 설정 로드 (폴더 경로, 제외 패턴)
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

        // 현재 설정을 레지스트리에 저장 (System.Windows.Input)
        private void SaveConfig()
        {
            SetRegValue("LeftFolder", _leftFolder ?? "");
            SetRegValue("RightFolder", _rightFolder ?? "");
            SetRegValue("ExcludeFilePatterns", _excludeFilePatterns ?? "");
            SetRegValue("ExcludeFolderPatterns", _excludeFolderPatterns ?? "");
        }

        // 레지스트리에서 값 읽기 (Microsoft.Win32)
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

        // 레지스트리에 값 저장 (Microsoft.Win32)
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

        // 왼쪽 폴더 경로 설정하고 저장
        private void SetLeftFolder(string folder)
        {
            _leftFolder = folder;
            LeftPathBox.Text = folder;
            LeftPathBox.Foreground = System.Windows.Media.Brushes.White;
            SaveConfig();
        }

        // 오른쪽 폴더 경로 설정하고 저장
        private void SetRightFolder(string folder)
        {
            _rightFolder = folder;
            RightPathBox.Text = folder;
            RightPathBox.Foreground = System.Windows.Media.Brushes.White;
            SaveConfig();
        }

        // 왼쪽 폴더 선택 버튼 클릭 (Ookii.Dialogs.Wpf)
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

        // 오른쪽 폴더 선택 버튼 클릭 (Ookii.Dialogs.Wpf)
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

        // 경로 상자에 파일 드래그 시작 감지 (System.Windows.Input)
        private void PathBox_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        // 경로 상자에 파일 드래그 중 (System.Windows.Input)
        private void PathBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        // 왼쪽 경로 상자에 폴더 드래그 드롭 (System.IO)
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

        // 오른쪽 경로 상자에 폴더 드래그 드롭 (System.IO)
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

        // 폴더 선택 대화창 표시 (Ookii.Dialogs.Wpf)
        private string? PickFolder()
        {
            var dlg = new VistaFolderBrowserDialog { Description = "폴더를 선택하세요", UseDescriptionForTitle = true };
            return dlg.ShowDialog(this) == true ? dlg.SelectedPath : null;
        }

        // 두 폴더 비교 시작 (메타데이터 빠른 비교 후 파일 내용 정밀 비교) (System.Threading)
        private async void Compare_Click(object? sender, RoutedEventArgs? e)
        {
            if (_isComparing)
            {
                RequestCompareCancellation();
                return;
            }

            if (string.IsNullOrEmpty(_leftFolder) || string.IsNullOrEmpty(_rightFolder))
            { MessageBox.Show("두 폴더를 모두 선택하세요", "경고", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // 확장된 폴더 상태 저장하여 비교 후 복구
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
                // 메타데이터(크기, 날짜)로 빠른 비교
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

                // 정밀 비교(파일 내용) 진행률 표시
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

        // 좌우 폴더 교환 및 재비교
        private void Swap_Click(object? sender, RoutedEventArgs? e)
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

        // 확장된 폴더 상태를 유지하며 UI 새로고침
        private void RefreshUIWithExpandedState()
        {
            Compare_Click(null, null);
        }

        // 저장된 확장 상태 재귀적으로 복구
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

        // 메타데이터(크기, 수정일)로 재귀적 폴더 비교 (System.IO, System.Linq)
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
                if (il && li is not null) { item.IsDirectory = li is DirectoryInfo; item.LeftPath = li.FullName; item.LeftSize = li is FileInfo lf ? lf.Length : 0; item.LeftModified = li.LastWriteTime; }
                if (ir && ri is not null) { item.IsDirectory = ri is DirectoryInfo; item.RightPath = ri.FullName; item.RightSize = ri is FileInfo rf ? rf.Length : 0; item.RightModified = ri.LastWriteTime; }
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

        // 폴더의 파일/폴더 목록 조회 (System.IO)
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

        // 파일을 메타데이터로 비교 (정밀 비교 필요시 후보에 추가)
        private CompareStatus CompareFilesByMetadata(FileItem item, IList<PreciseCompareCandidate> preciseCandidates)
        {
            if (item.LeftSize != item.RightSize) return CompareStatus.Modified;
            if (item.LeftPath is null || item.RightPath is null) return CompareStatus.Modified;

            // 크기가 같으면 정밀 비교 후보에 추가
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

        // 파일 내용으로 정밀 비교 (System.Collections.Generic)
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

        // 두 파일의 내용 비교 (System.IO)
        private CompareStatus CompareFileContent(PreciseCompareCandidate candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool sameContent = FilesHaveSameContent(candidate.LeftPath, candidate.RightPath, cancellationToken);
            if (!sameContent) return CompareStatus.Modified;
            if (candidate.Item.LeftModified != candidate.Item.RightModified) return CompareStatus.DateOnly;
            return CompareStatus.Identical;
        }

        // 두 파일의 바이너리 내용 비교 (버퍼링으로 효율화) (System.IO)
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

        // 진행 중인 비교 작업 중지 요청 (System.Threading)
        private void RequestCompareCancellation()
        {
            if (_compareCancellation == null || _compareCancellation.IsCancellationRequested) return;

            _compareCancellation.Cancel();
            CompareBtn.Content = "⏳ Stopping...";
            CompareBtn.ToolTip = "Stopping comparison";
            StatusText.Text = "폴더 비교 중지 요청 중...";
        }

        // 비교 상태 및 버튼 상태 업데이트 (System.Windows.Media)
        private void SetCompareRunningState(bool isRunning)
        {
            _isComparing = isRunning;
            CompareBtn.Content = isRunning ? "■ Stop (F5)" : "⟳ F5";
            CompareBtn.ToolTip = isRunning ? "Stop comparison (F5)" : "Refresh (F5)";
            CompareBtn.Background = new SolidColorBrush(isRunning
                ? Color.FromRgb(192, 57, 43)
                : Color.FromRgb(30, 126, 52));
        }

        // 정밀 비교 결과 상태 반영
        private void ApplyPreciseStatuses(IReadOnlyDictionary<FileItem, CompareStatus> preciseStatuses)
        {
            foreach (var (item, status) in preciseStatuses)
                item.Status = status;
        }

        // 자식 항목 상태로부터 폴더 상태 재계산 (재귀)
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

        // 자식 항목들의 상태로부터 폴더 상태 결정
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

        // 그리드 항목 더블클릭 (폴더는 확장, 파일은 비교창 열기) (System.Windows.Input)
        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as ListBox;
            if (!(grid?.SelectedItem is FileItem item)) return;

            if (item.IsDirectory)
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

        // 비교창 열기 버튼
        private void OpenCompare_Click(object sender, RoutedEventArgs e) => OpenSelectedCompare();

        // 선택된 파일의 비교창 열기
        private void OpenSelectedCompare()
        {
            FileItem? item = LeftGrid.SelectedItem as FileItem ?? RightGrid.SelectedItem as FileItem;
            if (item is null || item.IsDirectory) return;
            if (string.IsNullOrEmpty(item.LeftPath) || string.IsNullOrEmpty(item.RightPath)) return;
            new CompareWindow(item.LeftPath, item.RightPath, _leftFolder, _rightFolder) { Owner = this }.Show();
        }

        // 다른 파일과 비교 모드 시작
        private void CompareTo_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSourceGrid()?.SelectedItem as FileItem;
            if (item is null || item.IsDirectory) return;

            if (_compareToSourceItem != null) _compareToSourceItem.IsCompareToSource = false;
            _compareToSourceItem = item;
            item.IsCompareToSource = true;

            _compareToSourceGrid = GetSourceGrid();
            bool fromLeft = _compareToSourceGrid == LeftGrid;
            _compareToSourcePath = fromLeft ? item.LeftPath : item.RightPath;
            if (string.IsNullOrEmpty(_compareToSourcePath)) return;
            _compareToMode = true;
            Mouse.OverrideCursor = Cursors.Help;
            StatusText.Text = $"Compare mode: Click a file in the {(fromLeft ? "right" : "left")} pane";
        }

        // 지정된 파일을 선택하고 비교 (System.Linq)
        public void SelectAndCompareFiles(string? leftPath, string? rightPath)
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

        // 폴더 확장/축소 아이콘 클릭 (System.Windows.Input)
        private void ExpandIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FileItem item && item.HasChildren)
            {
                item.IsExpanded = !item.IsExpanded;
                RebuildFlatList();
                e.Handled = true;
            }
        }

        // 현재 포커스가 있는 그리드 반환
        private ListBox GetSourceGrid() => _lastFocusedGrid ?? LeftGrid;

        // 선택된 파일/폴더를 반대쪽으로 복사 (System.IO)
        private void CopyToOpposite_Click(object sender, RoutedEventArgs e)
        {
            var src = GetSourceGrid();
            bool fromLeft = src == LeftGrid;
            var items = src.SelectedItems.Cast<FileItem>().ToList();
            string? srcRoot = fromLeft ? _leftFolder : _rightFolder;
            string? dstRoot = fromLeft ? _rightFolder : _leftFolder;
            if (string.IsNullOrEmpty(srcRoot) || string.IsNullOrEmpty(dstRoot)) return;

            foreach (var item in items)
            {
                string? path = fromLeft ? item.LeftPath : item.RightPath;
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

        // 폴더를 재귀적으로 복사 (System.IO)
        private void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }

        // 트리 구조를 평탄화된 목록으로 재구성 (필터링, 정렬 포함)
        private void RebuildFlatList()
        {
            _flatItems.Clear();
            var roots = GetVisibleItems(_rootItems);
            for (int i = 0; i < roots.Count; i++)
                AddFlatRecursive(roots[i], new List<bool>(), i == roots.Count - 1);
        }

        // 트리 항목을 평탄 목록에 재귀적으로 추가 (들여쓰기 정보 포함)
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

        // 필터 조건에 맞는 항목 조회 (폴더 우선, 알파벳순 정렬) (System.Linq)
        private List<FileItem> GetVisibleItems(IEnumerable<FileItem> items)
        {
            return items
                .Where(item => IsItemVisible(item))
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name)
                .ToList();
        }

        // 항목 표시 여부 결정 (제외 패턴, 필터 모드 확인)
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
                FilterMode.Same => item.Status == CompareStatus.Identical
                    || item.IsDirectory && HasMatchingDescendants(item),
                _ => true
            };
        }

        // 폴더의 자식 항목 중 필터 조건에 맞는 항목 존재 확인 (재귀)
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
                    FilterMode.Same => child.Status == CompareStatus.Identical,
                    _ => true
                };
                if (childMatches) return true;
                if (child.IsDirectory && HasMatchingDescendants(child)) return true;
            }
            return false;
        }

        // 상태 표시줄 텍스트 업데이트 (통계 계산)
        private void UpdateStatusText(string suffix = "")
        {
            int id = 0, da = 0, mo = 0, lo = 0, ro = 0, total = 0;
            CountRecursive(_rootItems, ref id, ref da, ref mo, ref lo, ref ro, ref total);
            StatusText.Text = $"총 {total}개 | 동일: {id}  변경: {mo}  날짜만: {da}  왼쪽만: {lo}  오른쪽만: {ro}{suffix}";
            FilterAllBtn.Content = $"All ({total})";
            FilterDiffBtn.Content = $"Diff ({mo + da + lo + ro})";
            FilterDiff2Btn.Content = $"Diff2 ({mo + lo + ro})";
            FilterSameBtn.Content = $"Same ({id})";
        }

        // 상태별 항목 개수 재귀적 계산
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

        // 설정 창 열기
        private void Setup_Click(object sender, RoutedEventArgs e)
        {
            var setupWindow = new SetupWindow { Owner = this };
            setupWindow.ShowDialog();
        }

        // 선택된 파일/폴더 삭제 (System.IO)
        private void Delete_Click(object? sender, RoutedEventArgs? e)
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
                        string? path = isLeft ? f.LeftPath : f.RightPath;
                        if (path != null && File.Exists(path)) File.Delete(path);
                    }
                }
            }

            foreach (var d in dirs)
            {
                string? path = isLeft ? d.LeftPath : d.RightPath;
                if (path == null || !Directory.Exists(path)) continue;
                var dlg = new ConfirmDeleteDialog($"폴더를 삭제합니다:\n{path}");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                    Directory.Delete(path, true);
            }
            RefreshUIWithExpandedState();
        }

        // 그리드 키 이벤트 (Delete, F5) (System.Windows.Input)
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

        // 필터 버튼: 전체 항목 표시
        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.All;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        // 필터 버튼: 다른 항목 표시 (날짜만 다른 것 포함)
        private void FilterDiff_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.Diff;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        // 필터 버튼: 다른 항목 표시 (날짜만 다른 것 제외)
        private void FilterDiff2_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.Diff2;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        // 필터 버튼: 동일한 항목만 표시
        private void FilterSame_Click(object sender, RoutedEventArgs e)
        {
            _filterMode = FilterMode.Same;
            UpdateFilterButtonStyles();
            RebuildFlatList();
        }

        // 활성 필터 버튼 스타일 업데이트 (System.Windows.Media)
        private void UpdateFilterButtonStyles()
        {
            var activeColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219));
            var inactiveColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 123, 213));

            FilterAllBtn.Background = _filterMode == FilterMode.All ? activeColor : inactiveColor;
            FilterDiffBtn.Background = _filterMode == FilterMode.Diff ? activeColor : inactiveColor;
            FilterDiff2Btn.Background = _filterMode == FilterMode.Diff2 ? activeColor : inactiveColor;
            FilterSameBtn.Background = _filterMode == FilterMode.Same ? activeColor : inactiveColor;
        }

        // 왼쪽 그리드 마우스 다운 (선택, 확장, Compare To 모드) (System.Windows.Input)
        private void LeftGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastFocusedGrid = LeftGrid;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                if (listBox is null) return;
                var item = listBox.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
                if (item != null)
                {
                    if (item is not FrameworkElement element) return;
                    var fileItem = element.DataContext as FileItem;
                    if (fileItem is null) return;
                    var index = listBox.Items.IndexOf(fileItem);
                    if (index >= 0)
                    {
                        if (_compareToMode)
                        {
                            if (fileItem.IsDirectory && fileItem.HasChildren)
                            {
                                fileItem.IsExpanded = !fileItem.IsExpanded;
                                RebuildFlatList();
                                e.Handled = true;
                                return;
                            }
                            listBox.SelectedIndex = index;
                            HandleCompareToSelection(LeftGrid);
                            e.Handled = true;
                            return;
                        }

                        bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                        bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                        // Ctrl/Shift 없이 폴더 클릭시 확장/축소
                        if (fileItem.IsDirectory && fileItem.HasChildren && !ctrlPressed && !shiftPressed)
                        {
                            _isDragSelecting = false;
                            _dragStartIndex = -1;
                            return;
                        }
                        else if (shiftPressed && listBox.SelectedIndex >= 0)
                        {
                            // Shift: 범위 선택
                            int start = Math.Min(listBox.SelectedIndex, index);
                            int end = Math.Max(listBox.SelectedIndex, index);
                            listBox.SelectedItems.Clear();
                            for (int i = start; i <= end; i++)
                                listBox.SelectedItems.Add(listBox.Items[i]);
                            e.Handled = true;
                        }
                        else if (ctrlPressed)
                        {
                            // Ctrl: 토글 선택
                            if (listBox.SelectedItems.Contains(listBox.Items[index]))
                                listBox.SelectedItems.Remove(listBox.Items[index]);
                            else
                                listBox.SelectedItems.Add(listBox.Items[index]);
                            e.Handled = true;
                        }
                        else
                        {
                            // 드래그 선택 시작
                            _isDragSelecting = true;
                            _dragStartIndex = index;
                        }
                    }
                }
            }
        }

        // 오른쪽 그리드 마우스 다운 (선택, 확장, Compare To 모드) (System.Windows.Input)
        private void RightGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastFocusedGrid = RightGrid;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var listBox = sender as ListBox;
                if (listBox is null) return;
                var item = listBox.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
                if (item != null)
                {
                    if (item is not FrameworkElement element) return;
                    var fileItem = element.DataContext as FileItem;
                    if (fileItem is null) return;
                    var index = listBox.Items.IndexOf(fileItem);
                    if (index >= 0)
                    {
                        if (_compareToMode)
                        {
                            if (fileItem.IsDirectory && fileItem.HasChildren)
                            {
                                fileItem.IsExpanded = !fileItem.IsExpanded;
                                RebuildFlatList();
                                e.Handled = true;
                                return;
                            }
                            listBox.SelectedIndex = index;
                            HandleCompareToSelection(RightGrid);
                            e.Handled = true;
                            return;
                        }

                        bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                        bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                        if (fileItem.IsDirectory && fileItem.HasChildren && !ctrlPressed && !shiftPressed)
                        {
                            _isDragSelecting = false;
                            _dragStartIndex = -1;
                            return;
                        }
                        else if (shiftPressed && listBox.SelectedIndex >= 0)
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

        // 왼쪽 그리드 마우스 이동 (드래그 선택) (System.Windows.Input)
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

            var item = listBox.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
            if (item != null)
            {
                if (item is not FrameworkElement element) return;
                var index = listBox.Items.IndexOf(element.DataContext);
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

        // 오른쪽 그리드 마우스 이동 (드래그 선택) (System.Windows.Input)
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

            var item = listBox.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
            if (item != null)
            {
                if (item is not FrameworkElement element) return;
                var index = listBox.Items.IndexOf(element.DataContext);
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

        // 왼쪽 그리드 마우스 업 (드래그 선택 종료) (System.Windows.Input)
        private void LeftGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e) => _isDragSelecting = false;

        // 오른쪽 그리드 마우스 업 (드래그 선택 종료) (System.Windows.Input)
        private void RightGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e) => _isDragSelecting = false;

        // 컨텍스트 메뉴 표시 전 복사 방향 텍스트 업데이트
        private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool fromLeft = GetSourceGrid() == LeftGrid;
            var menu = (sender as FrameworkElement)?.ContextMenu;
            if (menu?.Items[0] is MenuItem copyItem)
                copyItem.Header = fromLeft ? "→ 오른쪽으로 복사" : "← 왼쪽으로 복사";
        }

        // 왼쪽 그리드 스크롤 동기화 (System.Windows.Controls)
        private void LeftGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            GetScrollViewer(RightGrid)?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        // 오른쪽 그리드 스크롤 동기화 (System.Windows.Controls)
        private void RightGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll) return;
            _isSyncingScroll = true;
            GetScrollViewer(LeftGrid)?.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingScroll = false;
        }

        // 왼쪽 그리드 선택 변경 (Compare To 모드 처리, 선택 동기화) (System.Windows.Controls)
        private void LeftGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HandleCompareToSelection(LeftGrid)) return;

            if (_isSyncingSelection) return;
            _isSyncingSelection = true;
            RightGrid.SelectedIndex = LeftGrid.SelectedIndex;
            _isSyncingSelection = false;
        }

        // 오른쪽 그리드 선택 변경 (Compare To 모드 처리, 선택 동기화) (System.Windows.Controls)
        private void RightGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HandleCompareToSelection(RightGrid)) return;

            if (_isSyncingSelection) return;
            _isSyncingSelection = true;
            LeftGrid.SelectedIndex = RightGrid.SelectedIndex;
            _isSyncingSelection = false;
        }

        // Compare To 모드 선택 처리 (반대편 파일 선택시 비교창 열고 모드 종료)
        private bool HandleCompareToSelection(ListBox grid)
        {
            var sourcePath = _compareToSourcePath;
            var sourceGrid = _compareToSourceGrid;
            if (!_compareToMode || sourcePath == null || sourceGrid == null) return false;

            var item = grid.SelectedItem as FileItem;
            if (item is null || item.IsDirectory) return false;

            bool isOtherGrid = grid != sourceGrid;
            if (!isOtherGrid) return false;

            string? targetPath = grid == LeftGrid ? item.LeftPath : item.RightPath;
            if (targetPath == null) return false;

            string leftPath = sourceGrid == LeftGrid ? sourcePath : targetPath;
            string rightPath = sourceGrid == LeftGrid ? targetPath : sourcePath;

            new CompareWindow(leftPath, rightPath) { Owner = this }.Show();
            if (_compareToSourceItem != null) _compareToSourceItem.IsCompareToSource = false;
            _compareToMode = false;
            _compareToSourcePath = null;
            _compareToSourceGrid = null;
            _compareToSourceItem = null;
            Mouse.OverrideCursor = null;
            StatusText.Text = "Select folders and click Compare button.";
            return true;
        }

        // 요소 트리에서 ScrollViewer 찾기 (System.Windows.Media)
        private ScrollViewer? GetScrollViewer(DependencyObject o)
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

        // 제외 패턴 문자열을 리스트로 파싱 (System.Linq)
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

        // 이름이 패턴 목록 중 하나와 일치하는지 확인
        private bool MatchesPattern(string name, List<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (WildcardMatch(name, pattern))
                    return true;
            }
            return false;
        }

        // 와일드카드 패턴 매칭 (* = 임의 문자, ? = 단일 문자)
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
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SMFolCmp.exe");
            var buildDate = File.Exists(exePath)
                ? File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd")
                : DateTime.Now.ToString("yyyy-MM-dd");
            Title = $"SMFolCmp v{version} ({buildDate})";
        }
    }
}


