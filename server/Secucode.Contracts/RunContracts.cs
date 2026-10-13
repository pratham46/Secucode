namespace Secucode.Contracts;

public sealed record RunRequest(string Language, string Code, string? Stdin);

public sealed record RunResult(string Stdout, string Stderr, int ExitCode, bool TimedOut, long DurationMs);
