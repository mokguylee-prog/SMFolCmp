using Microsoft.Win32;

namespace SMFolCmp.Services.Config;

public sealed class ConfigurationService
{
    private const string REG_PATH = @"Software\SMFolCmp";

    public string GetValue(string valueName, string defaultValue = "")
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

    public void SetValue(string valueName, string value)
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

    public MainWindowConfig LoadMainWindowConfig()
    {
        return new MainWindowConfig
        {
            LeftFolder = GetValue("LeftFolder", ""),
            RightFolder = GetValue("RightFolder", ""),
            ExcludeFilePatterns = GetValue("ExcludeFilePatterns", ""),
            ExcludeFolderPatterns = GetValue("ExcludeFolderPatterns", "")
        };
    }

    public void SaveMainWindowConfig(MainWindowConfig cfg)
    {
        SetValue("LeftFolder", cfg.LeftFolder ?? "");
        SetValue("RightFolder", cfg.RightFolder ?? "");
        SetValue("ExcludeFilePatterns", cfg.ExcludeFilePatterns ?? "");
        SetValue("ExcludeFolderPatterns", cfg.ExcludeFolderPatterns ?? "");
    }
}

public sealed record MainWindowConfig
{
    public string LeftFolder { get; init; } = "";
    public string RightFolder { get; init; } = "";
    public string ExcludeFilePatterns { get; init; } = "";
    public string ExcludeFolderPatterns { get; init; } = "";
}
