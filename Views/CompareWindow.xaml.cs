using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
        public string OldText { get; set; }
        public int LineIndex { get; set; }
        public bool IsLeft { get; set; }
        public bool OldIsPlaceholder { get; set; }
    }

    public partial class CompareWindow : Window
    {
        private string _leftPath, _rightPath;
        private string _leftFolder, _rightFolder;
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

        static readonly Brush BgDel = new SolidColorBrush(Color.FromArgb(80,255,80,80));
        static readonly Brush BgAdd = new SolidColorBrush(Color.FromArgb(80,80,200,80));
        static readonly Brush BgChg = new SolidColorBrush(Color.FromArgb(80,220,200,0));
        static readonly Brush BgPad = new SolidColorBrush(Color.FromArgb(30,120,120,120));
        static readonly Brush BgSel = new SolidColorBrush(Color.FromArgb(120,80,140,240));
        static readonly Brush FgNorm = new SolidColorBrush(Color.FromRgb(212,212,212));

        public CompareWindow(string leftPath, string rightPath, string leftFolder = null, string rightFolder = null)
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
            LoadAndDiff();
            UpdateTitles();
        }

        private void LoadAndDiff()
        {
            _leftLines = LoadFile(_leftPath); _rightLines = LoadFile(_rightPath);
            _leftDiff.Clear(); _rightDiff.Clear(); _leftNums.Clear(); _rightNums.Clear();
            var ops = DiffEngine.Compute(_leftLines, _rightLines);
            var opList = ops.ToList();
            BuildRowsFromOperations(opList);
            ApplyFilter();
            StatusBar.Text = $"Left: {_leftLines.Count} lines | Right: {_rightLines.Count} lines | Differences: {opList.Count(o=>o.Kind!=DiffKind.Equal)} items";
        }

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

                int rowCount = Math.Max(deletes.Count, inserts.Count);
                for (int row = 0; row < rowCount; row++)
                {
                    bool hasLeft = row < deletes.Count;
                    bool hasRight = row < inserts.Count;

                    if (hasLeft && hasRight)
                    {
                        int leftIndex = _leftDiff.Count;
                        int rightIndex = _rightDiff.Count;
                        AddLine(_leftDiff, _leftNums, deletes[row], leftLine++, BgChg, true, true);
                        AddLine(_rightDiff, _rightNums, inserts[row], rightLine++, BgChg, false, true);
                        FindCharDifferences(deletes[row], inserts[row], _leftDiff[leftIndex], _rightDiff[rightIndex]);
                    }
                    else if (hasLeft)
                    {
                        AddLine(_leftDiff, _leftNums, deletes[row], leftLine++, BgDel, true, true);
                        AddPlaceholderLine(_rightDiff, _rightNums, false);
                    }
                    else
                    {
                        AddPlaceholderLine(_leftDiff, _leftNums, true);
                        AddLine(_rightDiff, _rightNums, inserts[row], rightLine++, BgAdd, false, true);
                    }
                }
            }
        }


        private void FindCharDifferences(string left, string right, DiffLine leftDiff, DiffLine rightDiff)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return;

            int i = 0, j = 0;
            while (i < left.Length && i < right.Length && left[i] == right[i]) i++;
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

        private List<string> LoadFile(string p)
        {
            if (p == null || !File.Exists(p)) return new();
            var content = File.ReadAllText(p);
            content = content.Replace("\r\n", "\n");
            return content.Split('\n').ToList();
        }

        private void AddLine(ObservableCollection<DiffLine> col, ObservableCollection<string> nums, string text, int num, Brush bg, bool isLeft, bool isDifferenceRow)
        {
            col.Add(new DiffLine { Text=text, Background=bg, OriginalBackground=bg, Foreground=FgNorm, LineIndex=col.Count, IsLeft=isLeft, IsDifferenceRow=isDifferenceRow, LineNumber=num > 0 ? num.ToString() : "" });
            nums.Add(num > 0 ? num.ToString() : "");
        }

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

        private void UpdateSelection(int start, int end, bool isLeft)
        {
            foreach (var dl in _leftDiff.Concat(_rightDiff))
                if (dl.Background == BgSel) dl.Background = dl.OriginalBackground;

            int lo = Math.Min(start, end), hi = Math.Max(start, end);
            var col = isLeft ? _leftDiff : _rightDiff;
            for (int i = lo; i <= hi; i++)
                if (i < col.Count) col[i].Background = BgSel;

            _selStart = lo; _selEnd = hi; _selIsLeft = isLeft;
        }

        private void ClearSelection()
        {
            foreach (var dl in _leftDiff.Concat(_rightDiff))
                if (dl.Background == BgSel) dl.Background = dl.OriginalBackground;
            _selStart = _selEnd = -1;
        }

        private DiffLine GetDiffLineAt(MouseEventArgs e, IInputElement container)
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

        private void LeftScroll_PreDown(object sender, MouseButtonEventArgs e)
        {
            var dl = GetDiffLineAt(e, LeftScroll);
            if (dl == null) return;
            _isDragging = true;
            UpdateSelection(dl.LineIndex, dl.LineIndex, true);
        }

        private void LeftScroll_PreMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var dl = GetDiffLineAt(e, LeftScroll);
            if (dl != null) UpdateSelection(_selStart, dl.LineIndex, true);
        }

        private void LeftScroll_PreUp(object sender, MouseButtonEventArgs e) => _isDragging = false;

        private void RightScroll_PreDown(object sender, MouseButtonEventArgs e)
        {
            var dl = GetDiffLineAt(e, RightScroll);
            if (dl == null) return;
            _isDragging = true;
            UpdateSelection(dl.LineIndex, dl.LineIndex, false);
        }

        private void RightScroll_PreMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var dl = GetDiffLineAt(e, RightScroll);
            if (dl != null) UpdateSelection(_selStart, dl.LineIndex, false);
        }

        private void RightScroll_PreUp(object sender, MouseButtonEventArgs e) => _isDragging = false;

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

        private void DeleteSelectedLines_Click(object sender, RoutedEventArgs e)
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

        private void InsertLine_Click(object sender, RoutedEventArgs e)
        {
            if (_selStart < 0) { StatusBar.Text = "Select a line to insert above"; return; }
            var col = _selIsLeft ? _leftDiff : _rightDiff;
            var nums = _selIsLeft ? _leftNums : _rightNums;
            int insertIdx = _selStart;
            var newLine = new DiffLine { Text = "", Background = Brushes.Transparent, OriginalBackground = Brushes.Transparent, Foreground = FgNorm, LineIndex = insertIdx, IsLeft = _selIsLeft, IsDifferenceRow = true };
            col.Insert(insertIdx, newLine);
            nums.Insert(insertIdx, "");
            for (int i = insertIdx + 1; i < col.Count; i++)
                col[i].LineIndex = i;
            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
            StatusBar.Text = "Line inserted. Press Ctrl+S to save.";
        }

        private void LeftContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm?.PlacementTarget is Border b && b.Tag is DiffLine dl)
            {
                if (_selIsLeft != true || dl.LineIndex < _selStart || dl.LineIndex > _selEnd)
                    UpdateSelection(dl.LineIndex, dl.LineIndex, true);
            }
        }

        private void RightContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var cm = sender as ContextMenu;
            if (cm?.PlacementTarget is Border b && b.Tag is DiffLine dl)
            {
                if (_selIsLeft != false || dl.LineIndex < _selStart || dl.LineIndex > _selEnd)
                    UpdateSelection(dl.LineIndex, dl.LineIndex, false);
            }
        }

        private void OnKey(object sender, KeyEventArgs e)
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
                    for (int j = currentIndex + 1; j < col.Count; j++)
                        col[j].LineIndex = j;
                }

                currentIndex++;
            }

            if (_selIsLeft) UpdateLeftFile(); else UpdateRightFile();
            UpdateSelection(pasteStart, Math.Max(pasteStart, currentIndex - 1), _selIsLeft);
            StatusBar.Text = "Clipboard text pasted. Press Ctrl+S to save.";
        }

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

        private void CancelEditLine_Click(object sender, RoutedEventArgs e)
        {
            EditBorder.Visibility = Visibility.Collapsed;
            StatusBar.Text = "";
        }

        private void UpdateTitles()
        {
            LeftTitle.Text  = (_leftModified  ? "* " : "") + (_leftPath  ?? "(Empty)");
            RightTitle.Text = (_rightModified ? "* " : "") + (_rightPath ?? "(Empty)");
        }

        private void UpdateLeftFile()
        {
            _leftLines = _leftDiff.Where(l => !l.IsPlaceholder).Select(l => l.Text).ToList();
            _leftModified = true;
            UpdateTitles();
        }

        private void UpdateRightFile()
        {
            _rightLines = _rightDiff.Where(l => !l.IsPlaceholder).Select(l => l.Text).ToList();
            _rightModified = true;
            UpdateTitles();
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            _showOnlyDiff = false;
            App.SetRegValue("CompareShowOnlyDiff", "0");
            ApplyFilter();
        }

        private void FilterDiff_Click(object sender, RoutedEventArgs e)
        {
            _showOnlyDiff = true;
            App.SetRegValue("CompareShowOnlyDiff", "1");
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            foreach (var line in _leftDiff.Concat(_rightDiff))
                line.RowVisibility = !_showOnlyDiff || line.IsDifferenceRow ? Visibility.Visible : Visibility.Collapsed;

            FilterAllBtn.Background = new SolidColorBrush(_showOnlyDiff ? Color.FromRgb(58, 123, 213) : Color.FromRgb(82, 169, 232));
            FilterDiffBtn.Background = new SolidColorBrush(_showOnlyDiff ? Color.FromRgb(82, 169, 232) : Color.FromRgb(58, 123, 213));
            ClearSelection();
        }

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

        private void SaveFileWithPath(ref string targetPath, string otherPath, string targetFolder, string otherFolder, List<string> lines, bool isLeft)
        {
            if (targetPath != null)
            {
                File.WriteAllLines(targetPath, lines);
            }
            else if (otherPath != null && targetFolder != null && otherFolder != null)
            {
                string relativePath = Path.GetRelativePath(otherFolder, otherPath);
                targetPath = Path.Combine(targetFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.WriteAllLines(targetPath, lines);
            }
        }

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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _undoStack.Clear();
            LoadAndDiff();
            _leftModified = _rightModified = false;
            UpdateTitles();
            StatusBar.Text = "Changes discarded. Files reloaded.";
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
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

        private void LeftScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        { if (!_syncScroll) return; _syncScroll=false; RightScroll.ScrollToVerticalOffset(e.VerticalOffset); LeftLineScroll.ScrollToVerticalOffset(e.VerticalOffset); _syncScroll=true; }

        private void RightScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        { if (!_syncScroll) return; _syncScroll=false; LeftScroll.ScrollToVerticalOffset(e.VerticalOffset); RightLineScroll.ScrollToVerticalOffset(e.VerticalOffset); _syncScroll=true; }
    }

    public partial class EditLineDialog : Window
    {
        public string Result { get; private set; }
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

    public static class DiffEngine
    {
        public static List<DiffOp> Compute(List<string> left, List<string> right)
        {
            int m=left.Count, n=right.Count;
            var lcs=new int[m+1,n+1];
            for (int i=m-1;i>=0;i--) for (int j=n-1;j>=0;j--)
                lcs[i,j]=left[i]==right[j] ? lcs[i+1,j+1]+1 : Math.Max(lcs[i+1,j],lcs[i,j+1]);
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
