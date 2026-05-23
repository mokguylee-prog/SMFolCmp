using System;
using System.Windows;
using Microsoft.Win32;

namespace SMFolCmp.Views
{
    public partial class SetupWindow : Window
    {
        private static readonly string[] TextCompareExtensions = [".txt", ".html", ".htm", ".md", ".markdown"];
        private string _exePath;

        public SetupWindow(string? exePath = null)
        {
            InitializeComponent();
            _exePath = exePath ?? System.AppContext.BaseDirectory.TrimEnd('\\') + "\\SMFolCmp.exe";
            ExePathBox.Text = _exePath;
            CheckRegistrationStatus();
        }

        private void CheckRegistrationStatus()
        {
            bool isRegistered = IsContextMenuRegistered();
            InfoText.Text = isRegistered ? "✓ 컨텍스트 메뉴가 등록되어 있습니다." : "등록되지 않은 상태입니다.";
            InfoText.Foreground = isRegistered ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
        }

        private bool IsContextMenuRegistered()
        {
            try
            {
                // Check both folder and file menus
                var folderLeftKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\SMFolCmpLeft");
                var folderCompareKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\SMFolCmpCompare");
                var fileLeftKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\SMFolCmpLeft");
                var fileCompareKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\SMFolCmpCompare");
                var folderMultiCompareKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\SMFolCmpMultiCompare");
                var fileMultiCompareKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\SMFolCmpMultiCompare");
                bool textExtensionKeysExist = TextCompareExtensions.All(ext =>
                    Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\SMFolCmpLeft") != null &&
                    Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\SMFolCmpCompare") != null &&
                    Registry.CurrentUser.OpenSubKey($@"Software\Classes\SystemFileAssociations\{ext}\shell\SMFolCmpMultiCompare") != null);

                return folderLeftKey != null && folderCompareKey != null &&
                       fileLeftKey != null && fileCompareKey != null &&
                       folderMultiCompareKey != null && fileMultiCompareKey != null &&
                       textExtensionKeysExist;
            }
            catch
            {
                return false;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
                FileName = "SMFolCmp.exe",
                DefaultExt = ".exe"
            };
            if (dialog.ShowDialog() == true)
            {
                _exePath = dialog.FileName;
                ExePathBox.Text = _exePath;
                StatusText.Text = "";
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            _exePath = ExePathBox.Text.Trim();
            if (string.IsNullOrEmpty(_exePath))
            {
                StatusText.Text = "✗ exe 파일 경로를 입력해주세요.";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }
            if (!System.IO.File.Exists(_exePath))
            {
                StatusText.Text = $"✗ 파일을 찾을 수 없습니다: {_exePath}";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }
            try
            {
                UnregisterContextMenu();  // 기존 메뉴 제거 후 새로 등록
                RegisterContextMenu();
                StatusText.Text = "✓ 컨텍스트 메뉴가 등록되었습니다!\n폴더/파일을 우클릭하면 'with Left'와 'and Compare' 메뉴가 나타납니다.";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                CheckRegistrationStatus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"✗ 등록 실패: {ex.Message}\n관리자 권한이 필요할 수 있습니다.";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void UnregisterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UnregisterContextMenu();
                StatusText.Text = "✓ 컨텍스트 메뉴가 제거되었습니다.";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                CheckRegistrationStatus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"✗ 제거 실패: {ex.Message}";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void RegisterContextMenu()
        {
            var exeDir = System.IO.Path.GetDirectoryName(_exePath) ?? "";
            var icoPath = System.IO.Path.Combine(exeDir, "SMFolCmp.ico");

            // Folder context menus
            // "SMFolCmp with Left" - Save as left folder
            var folderLeftPath = @"Software\Classes\Directory\shell\SMFolCmpLeft";
            using (var key = Registry.CurrentUser.CreateSubKey(folderLeftPath))
            {
                key?.SetValue("", "SMFolCmp with Left");
                key?.SetValue("MultiSelectModel", "Single");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(folderLeftPath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" left:\"%1\"");
            }

            // "SMFolCmp and Compare" - Compare with saved left folder
            var folderComparePath = @"Software\Classes\Directory\shell\SMFolCmpCompare";
            using (var key = Registry.CurrentUser.CreateSubKey(folderComparePath))
            {
                key?.SetValue("", "SMFolCmp and Compare");
                key?.SetValue("MultiSelectModel", "Single");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(folderComparePath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" compare:\"%1\"");
            }

            // File context menus
            // "SMFolCmp with Left" - Save as left file
            var fileLeftPath = @"Software\Classes\*\shell\SMFolCmpLeft";
            using (var key = Registry.CurrentUser.CreateSubKey(fileLeftPath))
            {
                key?.SetValue("", "SMFolCmp with Left");
                key?.SetValue("MultiSelectModel", "Single");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(fileLeftPath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" left:\"%1\"");
            }

            // "SMFolCmp and Compare" - Compare with saved left file
            var fileComparePath = @"Software\Classes\*\shell\SMFolCmpCompare";
            using (var key = Registry.CurrentUser.CreateSubKey(fileComparePath))
            {
                key?.SetValue("", "SMFolCmp and Compare");
                key?.SetValue("MultiSelectModel", "Single");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(fileComparePath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" compare:\"%1\"");
            }

            RegisterMultiCompareVerb(@"Software\Classes\Directory\shell\SMFolCmpMultiCompare", icoPath);
            RegisterMultiCompareVerb(@"Software\Classes\*\shell\SMFolCmpMultiCompare", icoPath);

            foreach (var extension in TextCompareExtensions)
            {
                RegisterSingleFileVerb($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpLeft", "SMFolCmp with Left", "left", icoPath);
                RegisterSingleFileVerb($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpCompare", "SMFolCmp and Compare", "compare", icoPath);
                RegisterMultiCompareVerb($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpMultiCompare", icoPath);
            }
        }

        private void UnregisterContextMenu()
        {
            // Remove folder context menus
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SMFolCmp", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SMFolCmpLeft", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SMFolCmpCompare", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SMFolCmpPairCompare", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SMFolCmpMultiCompare", false); }
            catch (ArgumentException) { }

            // Remove file context menus
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SMFolCmp", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SMFolCmpLeft", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SMFolCmpCompare", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SMFolCmpPairCompare", false); }
            catch (ArgumentException) { }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SMFolCmpMultiCompare", false); }
            catch (ArgumentException) { }

            foreach (var extension in TextCompareExtensions)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpLeft", false); }
                catch (ArgumentException) { }

                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpCompare", false); }
                catch (ArgumentException) { }

                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{extension}\shell\SMFolCmpMultiCompare", false); }
                catch (ArgumentException) { }
            }

        }

        private void RegisterSingleFileVerb(string keyPath, string label, string mode, string icoPath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key?.SetValue("", label);
                key?.SetValue("MultiSelectModel", "Single");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }

            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" {mode}:\"%1\"");
            }
        }

        private void RegisterMultiCompareVerb(string keyPath, string icoPath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key?.SetValue("", "-SMFolCmp");
                key?.SetValue("MultiSelectModel", "Player");
                if (System.IO.File.Exists(icoPath)) key?.SetValue("Icon", icoPath);
                else key?.SetValue("Icon", _exePath);
            }

            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + @"\command"))
            {
                key?.SetValue("", $"\"{_exePath}\" --compare-selected \"%1\"");
            }
        }
    }
}
