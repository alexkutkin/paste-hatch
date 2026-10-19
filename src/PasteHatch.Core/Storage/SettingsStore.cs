using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PasteHatch.Core;

/// <summary>
/// Stores <see cref="AppSettings"/> in <c>%LOCALAPPDATA%\PasteHatch\settings.json</c>.
/// Kept separate from the tab state so a corrupt settings file can never affect baskets.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SettingsStore(string settingsFilePath) => SettingsFilePath = settingsFilePath;

    public static SettingsStore CreateDefault() => new(PasteHatchPaths.SettingsFile);

    public string SettingsFilePath { get; }

    /// <summary>True when the user has never saved settings (very first launch).</summary>
    public bool Exists => File.Exists(SettingsFilePath);

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = SettingsFilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        File.Move(temporaryPath, SettingsFilePath, overwrite: true);
    }
}
