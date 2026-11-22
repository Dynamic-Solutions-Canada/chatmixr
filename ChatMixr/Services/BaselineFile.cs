using System.IO;
using System.Text.Json;
using WinUIAppKit;
using WinUIAppKit.Settings;

namespace ChatMixr.Services;

/// <summary>
/// Keeps the apps' volumes from before ChatMixr scaled them (<see cref="Models.SessionVolumeTracker"/>'s
/// baselines) in <c>volume-baselines.json</c> beside the settings, so a crash or a forced exit does not
/// lose them. It is working state, not a setting: Reset settings leaves it alone, and a clean exit
/// empties it once every app has been put back. An unreadable file is treated as empty.
/// </summary>
internal sealed class BaselineFile
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly string _path;

    /// <param name="dataFolderName">The app's data folder under %AppData%.</param>
    public BaselineFile(string dataFolderName)
    {
        _path = Path.Combine(AppEnvironment.DataDirectory(dataFolderName), "volume-baselines.json");
    }

    /// <summary>The saved baselines, or an empty set when there is no readable file.</summary>
    public Dictionary<string, float> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new();
            }

            var saved = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(_path));
            Logger.Info("Loaded {Count} saved app volumes from the last run", saved?.Count ?? 0);
            return saved ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Warn(ex, "Saved app volumes unreadable; starting without them");
            return new();
        }
    }

    /// <summary>Write the baselines atomically, or delete the file when there are none. Never throws.</summary>
    public void Save(IReadOnlyDictionary<string, float> baselines)
    {
        try
        {
            if (baselines.Count == 0)
            {
                File.Delete(_path);
                return;
            }

            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(baselines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn(ex, "Saving app volumes failed");
        }
    }
}
