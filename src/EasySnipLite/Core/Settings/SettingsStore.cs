using System.IO;
using System.Text.Json;

namespace EasySnipLite.Core.Settings;

/// <summary>settings.json 读写：原子写（临时文件 + 替换）；损坏/缺失回退默认值。</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EasySnipLite", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions);
            return s?.Normalize() ?? new Settings();
        }
        catch (JsonException) { return new Settings(); }
        catch (IOException) { return new Settings(); }
        catch (UnauthorizedAccessException) { return new Settings(); }
    }

    public static void Save(string path, Settings settings)
    {
        var dir = Path.GetDirectoryName(path)!;
        if (dir.Length == 0) dir = "."; // 裸文件名防御（T4-M1）
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, path, overwrite: true); // 同卷 rename，原子替换
    }
}
