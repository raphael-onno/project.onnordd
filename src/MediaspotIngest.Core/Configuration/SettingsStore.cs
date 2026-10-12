using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaspotIngest.Core.Configuration;

public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public const string AppFolderName = "onnoRDD";

    /// <summary>The folder the application used before it was renamed onnoRDD.</summary>
    private const string PreviousAppFolderName = "MediaspotIngest";

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string DefaultFilePath { get; } = Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>
    /// First start under the new name: copies the previous folder (settings, local batches) and points the local
    /// store at the copy. The previous folder is left in place, as a backup.
    /// </summary>
    public static void MigrateFromPreviousName()
    {
        var previous = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), PreviousAppFolderName);
        if (Directory.Exists(AppDataDirectory) || !Directory.Exists(previous))
            return;

        foreach (var directory in Directory.EnumerateDirectories(previous, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(AppDataDirectory, Path.GetRelativePath(previous, directory)));
        Directory.CreateDirectory(AppDataDirectory);
        foreach (var file in Directory.EnumerateFiles(previous, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(AppDataDirectory, Path.GetRelativePath(previous, file)));

        var store = new SettingsStore(DefaultFilePath);
        var settings = store.Load();
        if (settings.Store.LocalDirectory.StartsWith(previous, StringComparison.OrdinalIgnoreCase))
        {
            settings.Store.LocalDirectory = AppDataDirectory + settings.Store.LocalDirectory[previous.Length..];
            store.Save(settings);
        }
    }

    public string FilePath { get; } = filePath;

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        using var stream = File.OpenRead(FilePath);
        return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Write then swap, so a crash mid-write never leaves a truncated settings file.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
