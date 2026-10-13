namespace Secucode.Desktop.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _original = AppSettings.DataDir;
    private readonly string _dir = Directory.CreateTempSubdirectory("secucode_settings_").FullName;

    public AppSettingsTests() => AppSettings.DataDir = _dir;

    public void Dispose()
    {
        AppSettings.DataDir = _original;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_or_broken_file_gives_defaults()
    {
        Assert.Equal("http://localhost:8080/", AppSettings.Load().ApiBaseUrl);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        Assert.Equal(ThemeChoice.System, AppSettings.Load().Theme);
    }

    [Fact]
    public void Saved_settings_load_back_and_the_base_uri_ends_with_a_slash()
    {
        new AppSettings { ApiBaseUrl = "http://exam-server:9000", Theme = ThemeChoice.Dark }.Save();

        var loaded = AppSettings.Load();

        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal(new Uri("http://exam-server:9000/"), loaded.BaseUri);
    }
}
