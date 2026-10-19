using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PasteHatch.Core;

/// <summary>
/// JSON tab-state persistence. Reads tolerate a missing or corrupt file (the baskets on disk
/// remain the source of truth); writes are atomic so a crash never leaves a half-written file.
/// </summary>
public sealed class TabStateStore : ITabStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public TabStateStore(string stateFilePath) => StateFilePath = stateFilePath;

    public static TabStateStore CreateDefault() => new(PasteHatchPaths.StateFile);

    public string StateFilePath { get; }

    public WorkspaceState Load()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return new WorkspaceState();
            }

            var json = File.ReadAllText(StateFilePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions) ?? new WorkspaceState();
            state.Tabs ??= [];
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new WorkspaceState();
        }
    }

    public void Save(WorkspaceState state)
    {
        var directory = Path.GetDirectoryName(StateFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = StateFilePath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(temporaryPath, json, Encoding.UTF8);
        File.Move(temporaryPath, StateFilePath, overwrite: true);
    }
}
