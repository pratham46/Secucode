namespace Secucode.Contracts;

public sealed record MasterDto(int Id, string Name);

public sealed record MasterRequest(string Name);

public sealed record SettingsDto(bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension);
