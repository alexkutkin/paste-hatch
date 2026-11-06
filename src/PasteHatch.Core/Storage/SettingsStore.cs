namespace PasteHatch.Core;

/// <summary>
/// Kept separate from tab state so a corrupt settings file can never affect baskets.
/// </summary>
public sealed class SettingsStore
{
    private readonly JsonFile<AppSettings> _file;

    public SettingsStore(string settingsFilePath)
    {
        SettingsFilePath = settingsFilePath;
        _file = new JsonFile<AppSettings>(settingsFilePath);
    }

    public static SettingsStore CreateDefault() => new(PasteHatchPaths.SettingsFile);

    public string SettingsFilePath { get; }

    public bool Exists => File.Exists(SettingsFilePath);

    public AppSettings Load() => _file.Load();

    public void Save(AppSettings settings) => _file.Save(settings);
}
