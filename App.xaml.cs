using System;
using System.Windows;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SMFolCmp.Views;

namespace SMFolCmp
{
    public partial class App : Application
    {
        private const string REG_PATH = @"Software\SMFolCmp";

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                {
                    var exception = ex.ExceptionObject as Exception;
                    File.WriteAllText("error.log", $"{DateTime.Now}: {exception?.ToString()}");
                    MessageBox.Show($"Error: {exception?.Message}\n\n{exception?.StackTrace}", "Fatal Error");
                };

                DispatcherUnhandledException += (s, ex) =>
                {
                    File.WriteAllText("error.log", $"{DateTime.Now}: {ex.Exception?.ToString()}");
                    MessageBox.Show($"Error: {ex.Exception?.Message}\n\n{ex.Exception?.StackTrace}", "Fatal Error");
                    ex.Handled = true;
                };

                // 우클릭 컨텍스트 메뉴로 실행된 경우
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length >= 3 && args[1] == "--compare-selected")
                {
                    var selectedPaths = args.Skip(2)
                        .Where(path => Directory.Exists(path) || File.Exists(path))
                        .ToList();

                    if (selectedPaths.Count == 2 &&
                        ((Directory.Exists(selectedPaths[0]) && Directory.Exists(selectedPaths[1])) ||
                         (File.Exists(selectedPaths[0]) && File.Exists(selectedPaths[1]))))
                    {
                        MainWindow = File.Exists(selectedPaths[0])
                            ? new CompareWindow(selectedPaths[0], selectedPaths[1])
                            : new MainWindow(selectedPaths[0], selectedPaths[1]);
                        MainWindow.Show();
                        return;
                    }

                    if (selectedPaths.Count == 1)
                    {
                        HandlePendingCompare(selectedPaths[0]);
                        return;
                    }

                    MessageBox.Show("Compare requires exactly two folders or exactly two files.", "SMFolCmp");
                    Shutdown(0);
                    return;
                }
                if (args.Length == 4 && args[1] == "--compare-pair")
                {
                    string firstPath = args[2];
                    string secondPath = args[3];

                    if ((Directory.Exists(firstPath) && Directory.Exists(secondPath)) ||
                        (File.Exists(firstPath) && File.Exists(secondPath)))
                    {
                        MainWindow = File.Exists(firstPath)
                            ? new CompareWindow(firstPath, secondPath)
                            : new MainWindow(firstPath, secondPath);
                        MainWindow.Show();
                        return;
                    }
                }

                if (args.Length > 1)
                {
                    string param = args[1];
                    string mode = "";  // "left", "compare", 또는 ""
                    string path = "";

                    // 접두사 처리: "left:/path" 또는 "compare:/path"
                    if (param.StartsWith("left:"))
                    {
                        mode = "left";
                        path = param.Substring(5);
                    }
                    else if (param.StartsWith("compare:"))
                    {
                        mode = "compare";
                        path = param.Substring(8);
                    }
                    else
                    {
                        path = param;  // 통합 메뉴 모드
                    }

                    // 동작 1: "left:" 모드 - 왼쪽으로 저장만 함
                    if (mode == "left" && (Directory.Exists(path) || File.Exists(path)))
                    {
                        if (File.Exists(path))
                            SetRegValue("LeftFile", path);
                        else
                            SetRegValue("LeftFolder", path);
                        Shutdown(0);
                        return;
                    }

                    // 동작 2, 3, 4: "compare:" 모드
                    if (mode == "compare" && (Directory.Exists(path) || File.Exists(path)))
                    {
                        bool isFile = File.Exists(path);
                        string pendingPath = GetRegValue("PendingPath", "");
                        long pendingTime = long.TryParse(GetRegValue("PendingTime", "0"), out var t) ? t : 0;
                        long now = DateTime.Now.Ticks;
                        long timeWindowTicks = TimeSpan.FromSeconds(3).Ticks;

                        // 동작 3: 두 번째 실행 - 최근(3초 이내)에 저장된 pending이 있으면 비교 시작
                        if (!string.IsNullOrEmpty(pendingPath) && (Directory.Exists(pendingPath) || File.Exists(pendingPath)) &&
                            (now - pendingTime) < timeWindowTicks && pendingPath != path)
                        {
                            SetRegValue("PendingPath", "");
                            SetRegValue("PendingTime", "");

                            bool isPendingFile = File.Exists(pendingPath);
                            if (isFile || isPendingFile)
                            {
                                MainWindow = new CompareWindow(pendingPath, path);
                            }
                            else
                            {
                                MainWindow = new MainWindow(pendingPath, path);
                            }
                            MainWindow.Show();
                            return;
                        }

                        // 동작 2: pending이 없고 저장된 left가 있으면 즉시 비교
                        string savedLeftKey = isFile ? "LeftFile" : "LeftFolder";
                        string savedLeft = GetRegValue(savedLeftKey, "");
                        if (!string.IsNullOrEmpty(savedLeft) && (Directory.Exists(savedLeft) || File.Exists(savedLeft)))
                        {
                            bool isSavedFile = File.Exists(savedLeft);
                            if (isFile || isSavedFile)
                            {
                                MainWindow = new CompareWindow(savedLeft, path);
                            }
                            else
                            {
                                MainWindow = new MainWindow(savedLeft, path);
                            }
                            MainWindow.Show();
                            return;
                        }

                        // 동작 3: 첫 번째 실행 - pending에 저장하고 조용히 종료
                        SetRegValue("PendingPath", path);
                        SetRegValue("PendingTime", now.ToString());
                        Shutdown(0);
                        return;
                    }
                }

                // 일반 실행: 저장된 폴더로 GUI 띄우기
                MainWindow = new MainWindow();
                MainWindow.Show();
            }
            catch (Exception ex)
            {
                File.WriteAllText("error.log", $"{DateTime.Now}: {ex}");
                MessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "Fatal Error");
            }
        }

        public static string GetRegValue(string valueName, string defaultValue = "")
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

        public static void SetRegValue(string valueName, string value)
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

        private void HandlePendingCompare(string path)
        {
            string pendingPath = GetRegValue("PendingPath", "");
            long pendingTime = long.TryParse(GetRegValue("PendingTime", "0"), out var t) ? t : 0;
            long now = DateTime.Now.Ticks;
            long timeWindowTicks = TimeSpan.FromSeconds(3).Ticks;

            if (!string.IsNullOrEmpty(pendingPath) &&
                (Directory.Exists(pendingPath) || File.Exists(pendingPath)) &&
                (now - pendingTime) < timeWindowTicks &&
                pendingPath != path)
            {
                SetRegValue("PendingPath", "");
                SetRegValue("PendingTime", "");

                bool bothFiles = File.Exists(pendingPath) && File.Exists(path);
                bool bothDirectories = Directory.Exists(pendingPath) && Directory.Exists(path);
                if (bothFiles || bothDirectories)
                {
                    MainWindow = bothFiles
                        ? new CompareWindow(pendingPath, path)
                        : new MainWindow(pendingPath, path);
                    MainWindow.Show();
                    return;
                }
            }

            SetRegValue("PendingPath", path);
            SetRegValue("PendingTime", now.ToString());
            Shutdown(0);
        }
    }
}


