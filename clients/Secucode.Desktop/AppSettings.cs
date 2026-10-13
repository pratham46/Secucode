using System.IO;
using System.Text.Json;
using Secucode.Contracts;

namespace Secucode.Desktop;

public enum ThemeChoice { System, Light, Dark }

// Per-user settings in %LocalAppData%\Secucode\settings.json. A missing or broken file means defaults.
public sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:8080/";
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    public Uri BaseUri => new(ApiBaseUrl.EndsWith('/') ? ApiBaseUrl : ApiBaseUrl + "/");

    // Settable so tests can point it at a temp folder.
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Secucode");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json.Options) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Options));
    }
}
