namespace Secucode.Api.Features.Runner;

public sealed class RunnerOptions
{
    public string Mode { get; set; } = "Local"; // "Local" or "Docker"
    public string? DockerEndpoint { get; set; }  // null = Docker's default for this OS
    public int CompileTimeoutSeconds { get; set; } = 10;
    public int RunTimeoutSeconds { get; set; } = 5;
    public int MaxOutputBytes { get; set; } = 65536;
    public int MaxCodeBytes { get; set; } = 65536;

    // Adding a language is configuration only (spec §10). Keys are case-insensitive.
    public Dictionary<string, LanguageOptions> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Python"] = new() { Image = "python:3.12-slim", FileName = "main.py", Run = "python main.py" },
        ["JavaScript"] = new() { Image = "node:22-slim", FileName = "main.js", Run = "node main.js" },
        ["Java"] = new() { Image = "eclipse-temurin:21-jdk", FileName = "Main.java", Run = "java Main.java" },
        ["C++"] = new()
        {
            Image = "gcc:14", FileName = "main.cpp", Compile = "g++ -O2 -o main main.cpp", Run = "./main",
            LocalCompile = "g++ -O2 -o main.exe main.cpp", LocalRun = "./main.exe",
        },
    };
}

public sealed class LanguageOptions
{
    public string Image { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Compile { get; set; }
    public string Run { get; set; } = "";
    public string? LocalCompile { get; set; } // override for Local mode (e.g. Windows needs main.exe)
    public string? LocalRun { get; set; }
}
