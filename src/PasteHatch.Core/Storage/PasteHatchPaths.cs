namespace PasteHatch.Core;

/// <summary>
/// All application paths. The user's local application data folder is resolved through
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/>,
/// so baskets always live under <c>%LOCALAPPDATA%\PasteHatch\Baskets</c>.
/// </summary>
public static class PasteHatchPaths
{
    public const string AppFolderName = "PasteHatch";
    public const string BasketsFolderName = "Baskets";
    public const string StateFileName = "state.json";
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// Optional data-root override used by the UI smoke tests so the app can run against an
    /// isolated folder inside the repository. Normal runs leave it unset and use %LOCALAPPDATA%.
    /// </summary>
    public const string DataRootEnvironmentVariable = "PASTEHATCH_DATA_ROOT";

    public static string AppDataRoot
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath.Trim());
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }
    }

    public static string BasketsRoot => Path.Combine(AppDataRoot, BasketsFolderName);

    public static string StateFile => Path.Combine(AppDataRoot, StateFileName);

    public static string SettingsFile => Path.Combine(AppDataRoot, SettingsFileName);
}
