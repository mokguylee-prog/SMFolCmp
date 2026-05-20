using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SMFolCmp.Views
{
    public static class TextBlockEx
    {
        public static List<(int, int)> GetRedHighlights(TextBlock obj)
            => (List<(int, int)>)obj.GetValue(RedHighlightsProperty);

        public static void SetRedHighlights(TextBlock obj, List<(int, int)> value)
            => obj.SetValue(RedHighlightsProperty, value);

        public static readonly DependencyProperty RedHighlightsProperty =
            DependencyProperty.RegisterAttached(
                "RedHighlights",
                typeof(List<(int, int)>),
                typeof(TextBlockEx),
                new PropertyMetadata(null, OnRedHighlightsChanged));

        private static void OnRedHighlightsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = (TextBlock)d;
            var text = tb.Text;
            var highlights = (List<(int, int)>)e.NewValue;

            tb.Inlines.Clear();

            if (string.IsNullOrEmpty(text)) return;
            if (highlights == null || highlights.Count == 0)
            {
                tb.Inlines.Add(new Run { Text = text, Foreground = Brushes.White });
                return;
            }

            int lastPos = 0;
            foreach (var (start, length) in highlights)
            {
                if (start > lastPos && lastPos < text.Length)
                    tb.Inlines.Add(new Run { Text = text.Substring(lastPos, Math.Min(start - lastPos, text.Length - lastPos)), Foreground = Brushes.White });
                if (start < text.Length)
                    tb.Inlines.Add(new Run { Text = text.Substring(start, Math.Min(length, text.Length - start)), Foreground = Brushes.Red });
                lastPos = start + length;
            }

            if (lastPos < text.Length)
                tb.Inlines.Add(new Run { Text = text.Substring(lastPos), Foreground = Brushes.White });
        }
    }

    public class DiffLine : INotifyPropertyChanged
    {
        private string _text = "";
        private Brush _background = Brushes.Transparent;
        private List<(int start, int length)> _redHighlights = new();
        private Visibility _rowVisibility = Visibility.Visible;

        public string Text
        {
            get => _text;
            set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }
        public Brush Background
        {
            get => _background;
            set { _background = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background))); }
        }
        public Brush Foreground { get; set; } = new SolidColorBrush(Color.FromRgb(212,212,212));
        public Brush OriginalBackground { get; set; } = Brushes.Transparent;
        public string LineNumber { get; set; } = "";
        public int LineIndex { get; set; }
        public bool IsLeft { get; set; }
        public bool IsPlaceholder { get; set; }
        public bool IsDifferenceRow { get; set; }
        public Visibility RowVisibility
        {
            get => _rowVisibility;
            set { _rowVisibility = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowVisibility))); }
        }
        public List<(int start, int length)> RedHighlights
        {
            get => _redHighlights;
            set { _redHighlights = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedHighlights))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class UndoAction
    {
        public string OldText { get; set; } = "";
        public int LineIndex { get; set; }
        public bool IsLeft { get; set; }
        public bool OldIsPlaceholder { get; set; }
    }

    // 텍스트 파일 라인별 비교 창
    public partial class CompareWindow : Window
    {
        private string? _leftPath, _rightPath;
        private string? _leftFolder, _rightFolder;
        private List<string> _leftLines = new(), _rightLines = new();
        private ObservableCollection<DiffLine> _leftDiff = new(), _rightDiff = new();
        private ObservableCollection<string> _leftNums = new(), _rightNums = new();
        private int _selStart = -1, _selEnd = -1;
        private bool _selIsLeft = true;
        private bool _isDragging = false;
        private bool _leftModified = false, _rightModified = false;
        private bool _syncScroll = true;
        private bool _showOnlyDiff = true;
        private Stack<UndoAction> _undoStack = new();
        private bool _isDraggingDiffMap = false;
        private bool _isDiffMapUpdateQueued = false;

        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed class FilterScrollAnchor
        {
            public required DiffLine Line { get; init; }
            public bool IsLeft { get; init; }
            public double ViewportY { get; init; }
        }

        static readonly Brush BgDel = new SolidColorBrush(Color.FromArgb(80,255,80,80));
        static readonly Brush BgAdd = new SolidColorBrush(Color.FromArgb(80,80,200,80));
        static readonly Brush BgChg = new SolidColorBrush(Color.FromArgb(80,220,200,0));
        static readonly Brush BgPad = new SolidColorBrush(Color.FromArgb(30,120,120,120));
        static readonly Brush BgSel = new SolidColorBrush(Color.FromArgb(120,80,140,240));
        static readonly Brush FgNorm = new SolidColorBrush(Color.FromRgb(212,212,212));

        // 생성자: 두 파일 경로로 비교창 초기화
        public CompareWindow(string leftPath, string rightPath, string? leftFolder = null, string? rightFolder = null)
        {
            InitializeComponent();
            _leftPath = leftPath; _rightPath = rightPath;
            _leftFolder = leftFolder; _rightFolder = rightFolder;
            LeftLines.ItemsSource = _leftDiff; RightLines.ItemsSource = _rightDiff;
            LeftLineNumbers.ItemsSource = _leftDiff; RightLineNumbers.ItemsSource = _rightDiff;
            _showOnlyDiff = App.GetRegValue("CompareShowOnlyDiff", "1") != "0";
            KeyDown += OnKey;
            Closing += OnClosing;
            Closed += (s, e) => { App.SetRegValue("LeftFile", ""); App.SetRegValue("RightFile", ""); };
            Loaded += (_, _) => EnsureWindowFitsWorkArea();
            LoadAndDiff();
            UpdateTitles();
        }

        // 창 크기를 모니터 작업 영역 내로 제한 (System.Runtime.InteropServices, System.Windows.Interop)
        private void EnsureWindowFitsWorkArea()
        {
            if (WindowState == WindowState.Maximized) return;

            var workArea = GetCurrentMonitorWorkArea();

            if (Width > workArea.Width) Width = workArea.Width;
            if (Height > workArea.Height) Height = workArea.Height;

            if (Left < workArea.Left) Left = workArea.Left;
            if (Top < workArea.Top) Top = workArea.Top;
            if (Left + ActualWidth > workArea.Right) Left = Math.Max(workArea.Left, workArea.Right - ActualWidth);
            if (Top + ActualHeight > workArea.Bottom) Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
        }

        // 현재 모니터의 작업 영역(taskbar 제외) 반환 (System.Runtime.InteropServices)
        private Rect GetCurrentMonitorWorkArea()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;

            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;

            return new Rect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top);
        }

        // 파일 로드 및 diff 알고리즘 실행 (System.IO)
        private void LoadAndDiff()
        {
            _leftLines = LoadFile(_leftPath); _rightLines = LoadFile(_rightPath);
            RebuildDiffFromCurrentLines();
        }

        // 현재 라인 목록으로부터 diff 재구성 (DiffEngine LCS 알고리즘 사용)
        private void RebuildDiffFromCurrentLines()
        {
            _leftDiff.Clear(); _rightDiff.Clear(); _leftNums.Clear(); _rightNums.Clear();
            var ops = DiffEngine.Compute(_leftLines, _rightLines);
            var opList = ops.ToList();
            BuildRowsFromOperations(opList);
            ApplyFilter();
            StatusBar.Text = $"Left: {_leftLines.Count} lines | Right: {_rightLines.Count} lines | Differences: {opList.Count(o=>o.Kind!=DiffKind.Equal)} items";
            QueueUpdateDiffMap();
        }

        // Diff 연산(Equal, Delete, Insert, Change)을 UI 라인으로 변환 (색상, 라인 번호 적용)
        private void BuildRowsFromOperations(List<DiffOp> operations)
        {
            int leftLine = 1, rightLine = 1, index = 0;
            while (index < operations.Count)
            {
                var op = operations[index];
                if (op.Kind == DiffKind.Equal)
                {
                    AddLine(_leftDiff, _leftNums, op.L, leftLine++, Brushes.Transparent, true, false);
                    AddLine(_rightDiff, _rightNums, op.R, rightLine++, Brushes.Transparent, false, false);
                    index++;
                    continue;
                }

                // 연속된 변경사항(Delete, Insert, Change)을 그룹화
                var deletes = new List<string>();
                var inserts = new List<string>();
                while (index < operations.Count && operations[index].Kind != DiffKind.Equal)
                {
                    var changed = operations[index];
                    if (changed.Kind == DiffKind.Delete) deletes.Add(changed.L);
                    else if (changed.Kind == DiffKind.Insert) inserts.Add(changed.R);
                    else
                    {
                        deletes.Add(changed.L);
                        inserts.Add(changed.R);
                    }
                    index++;
                }

                // 좌우 행 수를 맞추기 위해 더 많은 쪽에 placeholder 추가
                int rowCount = Math.Max(deletes.Count, inserts.Count);
                for (int row = 0; row < rowCount; row++)
                {
                    bool hasLeft = row < deletes.Count;
                    bool hasRight = row < inserts.Count;

                    if (hasLeft && hasRight)
                    {
                        // 양쪽 모두 있으면 Change: 문자 단위 차이 찾기
                        int leftIndex = _leftDiff.Count;
                        int rightIndex = _rightDiff.Count;
                        AddLine(_leftDiff, _leftNums, deletes[row], leftLine++, BgChg, true, true);
                        AddLine(_rightDiff, _rightNums, inserts[row], rightLine++, BgChg, false, true);
                        FindCharDifferences(deletes[row], inserts[row], _leftDiff[leftIndex], _rightDiff[rightIndex]);
                    }
                    else if (hasLeft)
                    {
                        // 왼쪽만 있으면 Delete
                        AddLine(_leftDiff, _leftNums, deletes[row], leftLine++, BgDel, true, true);
                        AddPlaceholderLine(_rightDiff, _rightNums, false);
                    }
                    else
                    {
                        // 오른쪽만 있으면 Insert
                        AddPlaceholderLine(_leftDiff, _leftNums, true);
                        AddLine(_rightDiff, _rightNums, inserts[row], rightLine++, BgAdd, false, true);
                    }
                }
            }
        }


        // 두 라인의 문자 단위 차이 찾기 (앞뒤에서 일치하는 부분 제외)
        private void FindCharDifferences(string left, string right, DiffLine leftDiff, DiffLine rightDiff)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return;

            // 앞에서부터 일치하는 문자 개수
            int i = 0, j = 0;
            while (i < left.Length && i < right.Length && left[i] == right[i]) i++;
            // 뒤에서부터 일치하는 문자 개수
            while (j < left.Length - i && j < right.Length - i && left[left.Length - 1 - j] == right[right.Length - 1 - j]) j++;

            int diffStartLeft = i, diffLenLeft = Math.Max(0, left.Length - i - j);
            int diffStartRight = i, diffLenRight = Math.Max(0, right.Length - i - j);

            var leftHighlights = new List<(int, int)>();
            var rightHighlights = new List<(int, int)>();

            if (diffLenLeft > 0) leftHighlights.Add((diffStartLeft, diffLenLeft));
            if (diffLenRight > 0) rightHighlights.Add((diffStartRight, diffLenRight));

            leftDiff.RedHighlights = leftHighlights;
            rightDiff.RedHighlights = rightHighlights;
        }

        // 파일을 라인 리스트로 로드 (System.IO, System.Linq)
        private List<string> LoadFile(string? p)
        {
            if (p == null || !File.Exists(p)) return new();
            var content = File.ReadAllText(p);
            content = content.Replace("\r\n", "\n");
            return content.Split('\n').ToList();
        }

        // 라인을 diff 컬렉션에 추가 (System.Collections.ObjectModel)
        private void AddLine(ObservableCollection<DiffLine> col, ObservableCollection<string> nums, string text, int num, Brush bg, bool isLeft, bool isDifferenceRow)
        {
            col.Add(new DiffLine { Text=text, Background=bg, OriginalBackground=bg, Foreground=FgNorm, LineIndex=col.Count, IsLeft=isLeft, IsDifferenceRow=isDifferenceRow, LineNumber=num > 0 ? num.ToString() : "" });
            nums.Add(num > 0 ? num.ToString() : "");
        }

        // 빈 라인(padding)을 diff 컬렉션에 추가 (좌우 행 수 맞추기용)
        private void AddPlaceholderLine(ObservableCollection<DiffLine> col, ObservableCollection<string> nums, bool isLeft)
        {
            col.Add(new DiffLine
            {
                Text = "",
                Background = BgPad,
                OriginalBackground = BgPad,
                Foreground = FgNorm,
                LineIndex = col.Count,
                IsLeft = isLeft,
                IsPlaceholder = true,
                IsDifferenceRow = true
            });
            nums.Add("");
        }

        // 라인 범위 선택 업데이트 (System.Windows.Media)
        private void UpdateSelection(int start, int end, bool isLeft)
        {
            // 기존 선택 배경색 복구
            foreach (var dl in _leftDiff.Concat(_rightDiff))
                if (dl.Background == BgSel) dl.Background = dl.OriginalBackground;

            // 새 선택 범위에 선택 색 적용
            int lo = Math.Min(start, end), hi = Math.Max(start, end);
            var col = isLeft ? _leftDiff : _rightDiff;
            for (int i = lo; i <= hi; i++)
                if (i < col.Count) col[i].Background = BgSel;

            _selStart = lo; _selEnd = hi; _selIsLeft = isLeft;
        }

        // 선택 초기화 (선택 색 제거)
        private void ClearSelection()
        {
            foreach (var dl in _leftDiff.Concat(_rightDiff))
                if (dl.Background == BgSel) dl.Background = dl.OriginalBackground;
            _selStart = _selEnd = -1;
        }

        // 마우스 위치의 DiffLine 항목 찾기 (System.Windows.Media)
        private DiffLine? GetDiffLineAt(MouseEventArgs e, IInputElement container)
        {
            var pt = e.GetPosition((IInputElement)container);
            var hit = VisualTreeHelper.HitTest((Visual)container, pt);
            var el = hit?.VisualHit as DependencyObject;
            while (el != null)
            {
                if (el is Border b && b.Tag is DiffLine dl) return dl;
                el = VisualTreeHelper.GetParent(el);
            }
            return null;
        }

        // 왼쪽 스크롤 마우스 다운 (선택 시작) (System.Windows.Input)
        private void LeftScroll_PreDown(object sender, MouseButtonEventArgs e)
        {
            var dl = GetDiffLineAt(e, LeftScroll);
            if (dl == null) return;
            _isDragging = true;
            UpdateSelection(dl.LineIndex, dl.LineIndex, true);
        }

        // 왼쪽 스크롤 마우스 이동 (드래그 선택) (System.Windows.Input)
        private void LeftScroll_PreMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var dl = GetDiffLineAt(e, LeftScroll);
            if (dl != null) UpdateSelection(_selStart, dl.LineIndex, true);
        }

        // 왼쪽 스크롤 마우스 업 (드래그 선택 종료) (System.Windows.Input)
        private void LeftScroll_PreUp(object sender, MouseButtonEventArgs e) => _isDragging = false;

        // 오른쪽 스크롤 마우스 다운 (선택 시작) (System.Windows.Input)
        private void RightScroll_PreDown(object sender, MouseButtonEventArgs e)
        {
            var dl = GetDiffLineAt(e, RightScroll);
            if (dl == null) return;
            _isDragging = true;
            UpdateSelection(dl.LineIndex, dl.LineIndex, false);
        }

        // 오른쪽 스크롤 마우스 이동 (드래그 선택) (System.Windows.Input)
        private void RightScroll_PreMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var dl = GetDiffLineAt(e, RightScroll);
            if (dl != null) UpdateSelection(_selStart, dl.LineIndex, false);
        }

        // 오른쪽 스크롤 마우스 업 (드래그 선택 종료) (System.Windows.Input)
        private void RightScroll_PreUp(object sender, MouseButtonEventArgs e) => _isDragging = false;

        // 선택된 왼쪽 라인을 오른쪽으로 복사
        private void CopySelectionToRight_Click(object sender, RoutedEventArgs e)
        {
            if (_selStart < 0 || !_selIsLeft) return;
            for (int i = _selStart; i <= _selEnd; i++)
            {
                if (i < _leftDiff.Count && i < _rightDiff.Count)
                {
                    _undoStack.Push(new UndoAction { OldText = _rightDiff[i].Text, LineIndex = i, IsLeft = false, OldIsPlaceholder = _rightDiff[i].IsPlaceholder });
                    _rightDiff[i].Text = _leftDiff[i].Text;
                    _rightDiff[i].IsPlaceholder = false;
                }
            }
            UpdateRightFile();
        }

        // 선택된 오른쪽 라인을 왼쪽으로 복사
        private void CopySelectionToLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_selStart < 0 || _selIsLeft) return;
            for (int i = _selStart; i <= _selEnd; i++)
            {
                if (i < _leftDiff.Count && i < _rightDiff.Count)
                {
                    _undoStack.Push(new UndoAction { OldText = _leftDiff[i].Text, LineIndex = i, IsLeft = true, OldIsPlaceholder = _leftDiff[i].IsPlaceholder });
                    _leftDiff[i].Text = _rightDiff[i].Text;
                    _leftDiff[i].IsPlaceholder = false;
                }
            }
            UpdateLeftFile();
        }

        // 선택된 라인 삭제 (placeholder로 변환)
        private void DeleteSelectedLines_Click(object? sender, RoutedEventArgs? e)
        {
            if (_selStart < 0) return;
            var col = _selIsLeft ? _leftDiff : _rightDiff;
            for (int i = _selStart; i <= _selEnd; i++)
            {
                if (i < col.Count)
                {
                    _undoStack.Push(new UndoAction { OldText = col[i].Text, LineIndex = i, IsLeft = _selIsLeft, OldIsPlaceholder = col[i].IsPlaceholder });
                    col[i].Text = "";
                    col[i].IsPlaceholder = true;
                }
            }
            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
        }

        // 선택된 위치 위에 빈 라인 삽입
        private void InsertLine_Click(object? sender, RoutedEventArgs? e)
        {
            if (_selStart < 0) { StatusBar.Text = "Select a line to insert above"; return; }
            var col = _selIsLeft ? _leftDiff : _rightDiff;
            var nums = _selIsLeft ? _leftNums : _rightNums;
            int insertIdx = _selStart;
            var newLine = new DiffLine { Text = "", Background = Brushes.Transparent, OriginalBackground = Brushes.Transparent, Foreground = FgNorm, LineIndex = insertIdx, IsLeft = _selIsLeft, IsDifferenceRow = true };
            col.Insert(insertIdx, newLine);
            nums.Insert(insertIdx, "");
            // 이후 라인들의 LineIndex 재설정
            for (int i = insertIdx + 1; i < col.Count; i++)
                col[i].LineIndex = i;
            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
            StatusBar.Text = "Line inserted. Press Ctrl+S to save.";
        }

        // 왼쪽 컨텍스트 메뉴 열기 (필요시 선택 업데이트)
        private void LeftContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm?.PlacementTarget is Border b && b.Tag is DiffLine dl)
            {
                if (_selIsLeft != true || dl.LineIndex < _selStart || dl.LineIndex > _selEnd)
                    UpdateSelection(dl.LineIndex, dl.LineIndex, true);
            }
        }

        // 오른쪽 컨텍스트 메뉴 열기 (필요시 선택 업데이트)
        private void RightContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm?.PlacementTarget is Border b && b.Tag is DiffLine dl)
            {
                if (_selIsLeft != false || dl.LineIndex < _selStart || dl.LineIndex > _selEnd)
                    UpdateSelection(dl.LineIndex, dl.LineIndex, false);
            }
        }

        // 키보드 이벤트 처리 (F2, Ctrl+S, Ctrl+Z, Ctrl+C, Ctrl+V, Delete, Esc, Ins) (System.Windows.Input)
        private void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2) OpenEditDialog();
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) SaveFiles();
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo();
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopySelectedTextToClipboard();
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) PasteClipboardText();
            if (e.Key == Key.Delete) DeleteSelectedLines_Click(null, null);
            if (e.Key == Key.Escape) Cancel_Click(null, null);
            if (e.Key == Key.Insert) InsertLine_Click(null, null);
        }

        // 선택된 라인 텍스트를 클립보드로 복사 (placeholder 제외) (System.Linq)
        private void CopySelectedTextToClipboard()
        {
            if (_selStart < 0) return;

            var col = _selIsLeft ? _leftDiff : _rightDiff;
            var selectedText = col
                .Skip(_selStart)
                .Take(_selEnd - _selStart + 1)
                .Where(line => !line.IsPlaceholder)
                .Select(line => line.Text)
                .ToList();

            if (selectedText.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, selectedText));
        }

        // 클립보드 텍스트를 선택된 위치에 붙여넣기 (선택 범위 치환 또는 추가)
        private void PasteClipboardText()
        {
            if (!Clipboard.ContainsText()) return;

            var col = _selIsLeft ? _leftDiff : _rightDiff;
            var nums = _selIsLeft ? _leftNums : _rightNums;
            var pastedLines = Clipboard.GetText()
                .Replace("\r\n", "\n")
                .Split('\n')
                .ToList();

            if (pastedLines.Count == 0) return;

            bool hasSelection = _selStart >= 0;
            int pasteStart = hasSelection ? _selStart : col.Count;
            int replaceCount = hasSelection ? Math.Max(1, _selEnd - _selStart + 1) : 0;
            int currentIndex = pasteStart;

            for (int i = 0; i < pastedLines.Count; i++)
            {
                if (i < replaceCount && currentIndex < col.Count)
                {
                    // 기존 라인 치환
                    _undoStack.Push(new UndoAction
                    {
                        OldText = col[currentIndex].Text,
                        LineIndex = currentIndex,
                        IsLeft = _selIsLeft,
                        OldIsPlaceholder = col[currentIndex].IsPlaceholder
                    });
                    col[currentIndex].Text = pastedLines[i];
                    col[currentIndex].IsPlaceholder = false;
                }
                else
                {
                    // 새 라인 삽입
                    var newLine = new DiffLine
                    {
                        Text = pastedLines[i],
                        Background = Brushes.Transparent,
                        OriginalBackground = Brushes.Transparent,
                        Foreground = FgNorm,
                        LineIndex = currentIndex,
                        IsLeft = _selIsLeft,
                        IsDifferenceRow = true
                    };
                    col.Insert(currentIndex, newLine);
                    nums.Insert(currentIndex, "");
                    // 이후 라인들의 LineIndex 재설정
                    for (int j = currentIndex + 1; j < col.Count; j++)
                        col[j].LineIndex = j;
                }

                currentIndex++;
            }

            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
            UpdateSelection(pasteStart, Math.Max(pasteStart, currentIndex - 1), _selIsLeft);
            StatusBar.Text = "Clipboard text pasted. Press Ctrl+S to save.";
        }

        // 마지막 편집 작업 취소 (Undo 스택 사용)
        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Pop();
            var col = action.IsLeft ? _leftDiff : _rightDiff;
            if (action.LineIndex < col.Count)
            {
                col[action.LineIndex].Text = action.OldText;
                col[action.LineIndex].IsPlaceholder = action.OldIsPlaceholder;
                if (action.IsLeft) UpdateLeftFile(); else UpdateRightFile();
            }
        }

        // 라인 편집 다이얼로그 열기 (인라인 텍스트박스)
        private void OpenEditDialog()
        {
            if (_selStart < 0) { StatusBar.Text = "Select a line to edit"; return; }
            var lines = _selIsLeft ? _leftDiff : _rightDiff;
            OriginalLineText.Text = lines[_selStart].Text;
            EditLineBox.Text = lines[_selStart].Text;
            EditBorder.Visibility = Visibility.Visible;
            EditLineBox.Focus();
            EditLineBox.SelectAll();
        }

        // 편집한 라인 저장 및 diff 재계산
        private void SaveEditLine_Click(object sender, RoutedEventArgs e)
        {
            if (_selStart < 0) return;
            var lines = _selIsLeft ? _leftDiff : _rightDiff;
            _undoStack.Push(new UndoAction { OldText = lines[_selStart].Text, LineIndex = _selStart, IsLeft = _selIsLeft, OldIsPlaceholder = lines[_selStart].IsPlaceholder });
            lines[_selStart].Text = EditLineBox.Text;
            lines[_selStart].IsPlaceholder = false;
            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
            EditBorder.Visibility = Visibility.Collapsed;
            StatusBar.Text = "Changes saved. Recomparing...";
            LoadAndDiff();
        }

        // 편집 텍스트박스 Enter 처리 (Alt+Enter는 줄바꿈) (System.Windows.Input)
        private void EditLineBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool isEnter = e.Key == Key.Enter || e.SystemKey == Key.Enter;
            if (!isEnter) return;

            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                EditLineBox.SelectedText = Environment.NewLine;
                e.Handled = true;
                return;
            }

            SaveEditLine_Click(sender, e);
            e.Handled = true;
        }

        // 라인 편집 취소
        private void CancelEditLine_Click(object sender, RoutedEventArgs e)
        {
            EditBorder.Visibility = Visibility.Collapsed;
            StatusBar.Text = "";
        }

        // 제목 표시줄 업데이트 (수정 여부 표시)
        private void UpdateTitles()
        {
            LeftTitle.Text  = (_leftModified  ? "* " : "") + (_leftPath  ?? "(Empty)");
            RightTitle.Text = (_rightModified ? "* " : "") + (_rightPath ?? "(Empty)");
        }

        // 왼쪽 파일의 라인 목록 업데이트 (placeholder 제외)
        private void UpdateLeftFile()
        {
            _leftLines = _leftDiff.Where(l => !l.IsPlaceholder).Select(l => l.Text).ToList();
            _leftModified = true;
            UpdateTitles();
        }

        // 오른쪽 파일의 라인 목록 업데이트 (placeholder 제외)
        private void UpdateRightFile()
        {
            _rightLines = _rightDiff.Where(l => !l.IsPlaceholder).Select(l => l.Text).ToList();
            _rightModified = true;
            UpdateTitles();
        }

        // 좌우 파일 교환 및 diff 재구성
        private void SwapFiles_Click(object sender, RoutedEventArgs e)
        {
            (_leftPath, _rightPath) = (_rightPath, _leftPath);
            (_leftFolder, _rightFolder) = (_rightFolder, _leftFolder);
            (_leftLines, _rightLines) = (_rightLines, _leftLines);
            (_leftModified, _rightModified) = (_rightModified, _leftModified);

            _undoStack.Clear();
            EditBorder.Visibility = Visibility.Collapsed;
            ClearSelection();
            RebuildDiffFromCurrentLines();
            UpdateTitles();

            ApplySyncedVerticalOffset(0);
            StatusBar.Text += " | Files swapped";
        }

        // 필터: 전체 라인 표시
        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            var anchor = CaptureFilterScrollAnchor();
            _showOnlyDiff = false;
            App.SetRegValue("CompareShowOnlyDiff", "0");

            ApplyFilter();
            RestoreFilterScrollAnchor(anchor);
        }

        // 필터: 차이나는 라인만 표시
        private void FilterDiff_Click(object sender, RoutedEventArgs e)
        {
            var anchor = CaptureFilterScrollAnchor();
            _showOnlyDiff = true;
            App.SetRegValue("CompareShowOnlyDiff", "1");

            ApplyFilter();
            RestoreFilterScrollAnchor(anchor);
        }

        // 필터 적용 (라인 가시성 설정, 버튼 스타일 업데이트)
        private void ApplyFilter()
        {
            foreach (var line in _leftDiff.Concat(_rightDiff))
                line.RowVisibility = !_showOnlyDiff || line.IsDifferenceRow ? Visibility.Visible : Visibility.Collapsed;

            FilterAllBtn.Background = new SolidColorBrush(_showOnlyDiff ? Color.FromRgb(58, 123, 213) : Color.FromRgb(82, 169, 232));
            FilterDiffBtn.Background = new SolidColorBrush(_showOnlyDiff ? Color.FromRgb(82, 169, 232) : Color.FromRgb(58, 123, 213));

            if (_selStart < 0)
            {
                ClearSelection();
            }

            QueueUpdateDiffMap();
        }

        // Diff 맵 업데이트 대기열 추가 (중복 업데이트 방지)
        private void QueueUpdateDiffMap()
        {
            if (_isDiffMapUpdateQueued) return;

            _isDiffMapUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isDiffMapUpdateQueued = false;
                UpdateDiffMap();
            }), DispatcherPriority.Loaded);
        }

        // Diff 맵 업데이트 (차이나는 라인을 미니맵으로 표시)
        private void UpdateDiffMap()
        {
            DiffMapCanvas.Children.Clear();

            var totalRows = Math.Max(_leftDiff.Count, _rightDiff.Count);
            var height = DiffMapCanvas.ActualHeight;
            var width = DiffMapCanvas.ActualWidth;
            if (totalRows == 0 || height <= 0 || width <= 0) return;

            var rowHeight = height / totalRows;
            // 차이 마커 표시
            for (int i = 0; i < totalRows; i++)
            {
                var isDifference =
                    i < _leftDiff.Count && _leftDiff[i].IsDifferenceRow ||
                    i < _rightDiff.Count && _rightDiff[i].IsDifferenceRow;

                if (!isDifference) continue;

                var marker = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(4, width - 6),
                    Height = Math.Max(2, rowHeight),
                    Fill = new SolidColorBrush(Color.FromRgb(255, 77, 90))
                };
                Canvas.SetLeft(marker, 3);
                Canvas.SetTop(marker, Math.Min(height - marker.Height, i * rowHeight));
                DiffMapCanvas.Children.Add(marker);
            }

            // 현재 뷰포트 표시
            var visibleRange = GetVisibleLineRange();
            if (visibleRange.HasValue)
            {
                var (start, end) = visibleRange.Value;
                var top = start * rowHeight;
                var bottom = Math.Min(height, (end + 1) * rowHeight);
                var viewport = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(4, width - 2),
                    Height = Math.Max(12, bottom - top),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))
                };
                Canvas.SetLeft(viewport, 1);
                Canvas.SetTop(viewport, Math.Max(0, Math.Min(height - viewport.Height, top)));
                DiffMapCanvas.Children.Add(viewport);
            }
        }

        // 현재 보이는 라인 범위 조회 (System.Windows.Media)
        private (int Start, int End)? GetVisibleLineRange()
        {
            int? start = null;
            int? end = null;

            for (int i = 0; i < _leftDiff.Count; i++)
            {
                if (_leftDiff[i].RowVisibility != Visibility.Visible) continue;
                if (LeftLines.ItemContainerGenerator.ContainerFromItem(_leftDiff[i]) is not FrameworkElement item) continue;

                var top = item.TransformToAncestor(LeftScroll).Transform(new Point(0, 0)).Y;
                var bottom = top + item.ActualHeight;
                if (bottom < 0 || top > LeftScroll.ViewportHeight) continue;

                start ??= i;
                end = i;
            }

            if (!start.HasValue || !end.HasValue) return null;
            return (start.Value, end.Value);
        }

        // Diff 맵 캔버스 크기 변경 (System.Windows.Controls)
        private void DiffMapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => QueueUpdateDiffMap();

        // Diff 맵 클릭/드래그로 스크롤 (System.Windows.Input)
        private void DiffMapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingDiffMap = true;
            DiffMapCanvas.CaptureMouse();
            ScrollToDiffMapPosition(e.GetPosition(DiffMapCanvas).Y);
            e.Handled = true;
        }

        // Diff 맵 드래그 중 스크롤 업데이트 (System.Windows.Input)
        private void DiffMapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingDiffMap || e.LeftButton != MouseButtonState.Pressed) return;
            ScrollToDiffMapPosition(e.GetPosition(DiffMapCanvas).Y);
            e.Handled = true;
        }

        // Diff 맵 드래그 종료 (System.Windows.Input)
        private void DiffMapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingDiffMap = false;
            DiffMapCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        // Diff 맵 위치에 해당하는 라인으로 스크롤
        private void ScrollToDiffMapPosition(double y)
        {
            var totalRows = _leftDiff.Count;
            var height = DiffMapCanvas.ActualHeight;
            if (totalRows == 0 || height <= 0) return;

            var ratio = Math.Clamp(y / height, 0, 1);
            var lineIndex = Math.Clamp((int)Math.Round(ratio * (totalRows - 1)), 0, totalRows - 1);
            var targetLine = GetNearestVisibleLine(_leftDiff[lineIndex], true);
            if (targetLine == null) return;

            if (LeftLines.ItemContainerGenerator.ContainerFromItem(targetLine) is not FrameworkElement item) return;

            item.BringIntoView();
            UpdateLayout();
            QueueUpdateDiffMap();
        }

        // 필터 적용 시 스크롤 위치 고정점 저장 (선택, 마우스, 또는 상단 라인)
        private FilterScrollAnchor? CaptureFilterScrollAnchor()
        {
            // 선택된 라인이 있으면 선택 라인 사용
            if (_selStart >= 0)
            {
                var selectedList = _selIsLeft ? _leftDiff : _rightDiff;
                if (_selStart < selectedList.Count)
                {
                    var selectedLine = selectedList[_selStart];
                    var selectedY = GetLineViewportY(selectedLine, _selIsLeft);
                    return new FilterScrollAnchor
                    {
                        Line = selectedLine,
                        IsLeft = _selIsLeft,
                        ViewportY = selectedY ?? ((_selIsLeft ? LeftScroll : RightScroll).ViewportHeight / 2)
                    };
                }
            }

            // 마우스가 왼쪽 스크롤 위에 있으면 마우스 위치의 라인 사용
            if (LeftScroll.IsMouseOver)
            {
                var line = GetDiffLineAtMouse(LeftScroll);
                if (line != null)
                    return new FilterScrollAnchor { Line = line, IsLeft = true, ViewportY = Mouse.GetPosition(LeftScroll).Y };
            }

            // 마우스가 오른쪽 스크롤 위에 있으면 마우스 위치의 라인 사용
            if (RightScroll.IsMouseOver)
            {
                var line = GetDiffLineAtMouse(RightScroll);
                if (line != null)
                    return new FilterScrollAnchor { Line = line, IsLeft = false, ViewportY = Mouse.GetPosition(RightScroll).Y };
            }

            // 그 외 상단의 보이는 라인 사용
            return CaptureTopVisibleAnchor(true) ?? CaptureTopVisibleAnchor(false);
        }

        // 상단의 첫 보이는 라인 찾기 (필터 변경시 스크롤 기준점)
        private FilterScrollAnchor? CaptureTopVisibleAnchor(bool isLeft)
        {
            var diffList = isLeft ? _leftDiff : _rightDiff;
            foreach (var line in diffList)
            {
                if (line.RowVisibility != Visibility.Visible) continue;

                var y = GetLineViewportY(line, isLeft);
                if (y.HasValue && y.Value >= 0)
                    return new FilterScrollAnchor { Line = line, IsLeft = isLeft, ViewportY = y.Value };
            }

            return null;
        }

        // 마우스 위치의 라인 찾기 (System.Windows.Media, System.Windows.Input)
        private DiffLine? GetDiffLineAtMouse(ScrollViewer scrollViewer)
        {
            var pt = Mouse.GetPosition(scrollViewer);
            var hit = VisualTreeHelper.HitTest(scrollViewer, pt);
            var el = hit?.VisualHit as DependencyObject;
            while (el != null)
            {
                if (el is Border b && b.Tag is DiffLine dl) return dl;
                el = VisualTreeHelper.GetParent(el);
            }
            return null;
        }

        // 라인이 스크롤 뷰에서의 Y 위치 조회 (System.Windows.Media)
        private double? GetLineViewportY(DiffLine line, bool isLeft)
        {
            var scrollViewer = isLeft ? LeftScroll : RightScroll;
            var itemsControl = isLeft ? LeftLines : RightLines;
            if (itemsControl.ItemContainerGenerator.ContainerFromItem(line) is not FrameworkElement item)
                return null;

            return item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        }

        // 저장된 스크롤 고정점 복구 (필터 변경 후)
        private void RestoreFilterScrollAnchor(FilterScrollAnchor? anchor)
        {
            if (anchor?.Line == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateLayout();

                var targetLine = GetNearestVisibleLine(anchor.Line, anchor.IsLeft);
                if (targetLine == null) return;

                var scrollViewer = anchor.IsLeft ? LeftScroll : RightScroll;
                var itemsControl = anchor.IsLeft ? LeftLines : RightLines;
                if (itemsControl.ItemContainerGenerator.ContainerFromItem(targetLine) is not FrameworkElement item) return;

                item.BringIntoView();
                UpdateLayout();

                var currentY = GetLineViewportY(targetLine, anchor.IsLeft);
                if (!currentY.HasValue) return;

                ApplySyncedVerticalOffset(scrollViewer.VerticalOffset + currentY.Value - anchor.ViewportY);
            }), DispatcherPriority.Loaded);
        }

        // 주어진 라인 또는 가까운 보이는 라인 찾기 (System.Linq)
        private DiffLine? GetNearestVisibleLine(DiffLine anchorLine, bool isLeft)
        {
            if (anchorLine.RowVisibility == Visibility.Visible)
                return anchorLine;

            var diffList = isLeft ? _leftDiff : _rightDiff;
            var anchorIndex = diffList.IndexOf(anchorLine);
            if (anchorIndex < 0) return diffList.FirstOrDefault(line => line.RowVisibility == Visibility.Visible);

            // 원점에서 앞뒤로 확장하여 가장 가까운 보이는 라인 찾기
            for (int distance = 1; distance < diffList.Count; distance++)
            {
                var previous = anchorIndex - distance;
                if (previous >= 0 && diffList[previous].RowVisibility == Visibility.Visible)
                    return diffList[previous];

                var next = anchorIndex + distance;
                if (next < diffList.Count && diffList[next].RowVisibility == Visibility.Visible)
                    return diffList[next];
            }

            return null;
        }

        // 좌우 스크롤 뷰를 동기화된 오프셋으로 설정 (스크롤 동기화)
        private void ApplySyncedVerticalOffset(double offset)
        {
            var boundedOffset = Math.Max(0, offset);
            _syncScroll = false;
            LeftScroll.ScrollToVerticalOffset(boundedOffset);
            RightScroll.ScrollToVerticalOffset(boundedOffset);
            LeftLineScroll.ScrollToVerticalOffset(boundedOffset);
            RightLineScroll.ScrollToVerticalOffset(boundedOffset);
            _syncScroll = true;
            QueueUpdateDiffMap();
        }

        // 특정 라인으로 스크롤 (동기화 포함)
        private void ScrollToLineIndex(int lineIndex, bool isLeft)
        {
            var scrollViewer = isLeft ? LeftScroll : RightScroll;
            var otherScroller = isLeft ? RightScroll : LeftScroll;
            var itemsControl = isLeft ? LeftLines : RightLines;
            var diffList = isLeft ? _leftDiff : _rightDiff;

            if (lineIndex < 0 || lineIndex >= diffList.Count) return;

            int visibleIndex = 0;
            for (int i = 0; i <= lineIndex; i++)
            {
                if (diffList[i].RowVisibility == Visibility.Visible)
                {
                    if (i == lineIndex)
                    {
                        var item = itemsControl.ItemContainerGenerator.ContainerFromIndex(visibleIndex) as FrameworkElement;
                        if (item == null) return;

                        _syncScroll = false;
                        item.BringIntoView();
                        double offset = scrollViewer.VerticalOffset - scrollViewer.ViewportHeight / 3;
                        scrollViewer.ScrollToVerticalOffset(Math.Max(0, offset));
                        otherScroller.ScrollToVerticalOffset(Math.Max(0, offset));
                        _syncScroll = true;
                        return;
                    }
                    visibleIndex++;
                }
            }
        }

        // 두 파일 모두 저장 (Ctrl+S)
        private void SaveFiles()
        {
            try
            {
                SaveFileWithPath(ref _leftPath, _rightPath, _leftFolder, _rightFolder, _leftLines, true);
                SaveFileWithPath(ref _rightPath, _leftPath, _rightFolder, _leftFolder, _rightLines, false);
                _leftModified = _rightModified = false;
                UpdateTitles();
                StatusBar.Text = "Files saved!";
                LoadAndDiff();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // 파일을 경로로 저장 (경로가 없으면 상대경로로 구성) (System.IO)
        private void SaveFileWithPath(ref string? targetPath, string? otherPath, string? targetFolder, string? otherFolder, List<string> lines, bool isLeft)
        {
            if (targetPath != null)
            {
                File.WriteAllLines(targetPath, lines);
            }
            else if (otherPath != null && targetFolder != null && otherFolder != null)
            {
                // 다른 파일의 상대경로를 이용하여 목표 경로 구성
                string relativePath = Path.GetRelativePath(otherFolder, otherPath);
                targetPath = Path.Combine(targetFolder, relativePath);
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllLines(targetPath, lines);
            }
        }

        // 왼쪽 파일만 저장
        private void SaveLeft_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileWithPath(ref _leftPath, _rightPath, _leftFolder, _rightFolder, _leftLines, true);
                if (_leftPath != null)
                {
                    _leftModified = false;
                    UpdateTitles();
                    StatusBar.Text = "Left file saved!";
                    LoadAndDiff();
                }
                else
                    MessageBox.Show("Cannot determine path to save left file");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // 오른쪽 파일만 저장
        private void SaveRight_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileWithPath(ref _rightPath, _leftPath, _rightFolder, _leftFolder, _rightLines, false);
                if (_rightPath != null)
                {
                    _rightModified = false;
                    UpdateTitles();
                    StatusBar.Text = "Right file saved!";
                    LoadAndDiff();
                }
                else
                    MessageBox.Show("Cannot determine path to save right file");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // 모든 변경사항 폐기 및 파일 재로드 (Esc)
        private void Cancel_Click(object? sender, RoutedEventArgs? e)
        {
            _undoStack.Clear();
            LoadAndDiff();
            _leftModified = _rightModified = false;
            UpdateTitles();
            StatusBar.Text = "Changes discarded. Files reloaded.";
        }

        // 창 닫기 전 저장 확인
        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            bool wasModified = _leftModified || _rightModified;
            if (!wasModified) return;

            var result = MessageBox.Show("저장되지 않은 변경사항이 있습니다.\n지금 저장하시겠습니까?", "파일 저장", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SaveFiles();
                if (Owner is MainWindow mainWindow)
                    mainWindow.SelectAndCompareFiles(_leftPath, _rightPath);
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (result == MessageBoxResult.No && Owner is MainWindow mw)
            {
                mw.SelectAndCompareFiles(_leftPath, _rightPath);
            }
        }

        // 왼쪽 스크롤 변경시 우측 동기화 (System.Windows.Controls)
        private void LeftScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        { if (!_syncScroll) return; _syncScroll=false; RightScroll.ScrollToVerticalOffset(e.VerticalOffset); LeftLineScroll.ScrollToVerticalOffset(e.VerticalOffset); RightLineScroll.ScrollToVerticalOffset(e.VerticalOffset); _syncScroll=true; QueueUpdateDiffMap(); }

        // 오른쪽 스크롤 변경시 좌측 동기화 (System.Windows.Controls)
        private void RightScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        { if (!_syncScroll) return; _syncScroll=false; LeftScroll.ScrollToVerticalOffset(e.VerticalOffset); LeftLineScroll.ScrollToVerticalOffset(e.VerticalOffset); RightLineScroll.ScrollToVerticalOffset(e.VerticalOffset); _syncScroll=true; QueueUpdateDiffMap(); }
    }

    public partial class EditLineDialog : Window
    {
        public string Result { get; private set; } = "";
        public EditLineDialog(string cur)
        {
            Title="Edit Line (F2)"; Width=700; Height=150; WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=new SolidColorBrush(Color.FromRgb(30,30,46));
            var tb = new TextBox { Text=cur, FontFamily=new FontFamily("Consolas"), FontSize=14, Foreground=Brushes.White, Background=new SolidColorBrush(Color.FromRgb(40,40,60)), Margin=new Thickness(10), Padding=new Thickness(6) };
            tb.SelectAll();
            var ok = new Button { Content="OK", Width=80, Height=30, Margin=new Thickness(10,0,10,10), Background=new SolidColorBrush(Color.FromRgb(58,123,213)), Foreground=Brushes.White, BorderThickness=new Thickness(0), HorizontalAlignment=HorizontalAlignment.Right };
            ok.Click += (_,_) => { Result=tb.Text; DialogResult=true; };
            tb.KeyDown += (_,e) => { if (e.Key==Key.Enter) { Result=tb.Text; DialogResult=true; } };
            Content = new StackPanel { Children={tb,ok} };
        }
    }

    public enum DiffKind { Equal, Delete, Insert, Change }
    public record DiffOp(DiffKind Kind, string L, string R);

    // 최장공통부분수열(LCS) 알고리즘으로 두 문자열 배열의 차이 계산
    public static class DiffEngine
    {
        // 두 라인 리스트의 diff 연산 계산 (LCS 기반 Myers-style diff)
        public static List<DiffOp> Compute(List<string> left, List<string> right)
        {
            int m=left.Count, n=right.Count;
            // LCS 테이블 구성 (뒤에서부터 앞으로)
            var lcs=new int[m+1,n+1];
            for (int i=m-1;i>=0;i--) for (int j=n-1;j>=0;j--)
                lcs[i,j]=left[i]==right[j] ? lcs[i+1,j+1]+1 : Math.Max(lcs[i+1,j],lcs[i,j+1]);

            // LCS 테이블을 따라 diff 연산 추적
            var ops=new List<DiffOp>(); int li=0,ri=0;
            while (li<m||ri<n)
            {
                if (li<m&&ri<n&&left[li]==right[ri]) { ops.Add(new(DiffKind.Equal,left[li],right[ri])); li++;ri++; }
                else if (li<m&&ri<n&&lcs[li+1,ri]==lcs[li,ri+1]&&lcs[li,ri]!=lcs[li+1,ri+1]) { ops.Add(new(DiffKind.Change,left[li],right[ri])); li++;ri++; }
                else if (ri<n&&(li>=m||lcs[li,ri+1]>=lcs[li+1,ri])) { ops.Add(new(DiffKind.Insert,"",right[ri])); ri++; }
                else { ops.Add(new(DiffKind.Delete,left[li],"")); li++; }
            }
            return ops;
        }
    }
}
