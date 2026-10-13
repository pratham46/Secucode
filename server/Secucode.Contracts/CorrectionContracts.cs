namespace Secucode.Contracts;

public sealed record ResetAttemptRequest(string Reason, int? ExtraMinutes);

public sealed record ReopenAttemptRequest(string Reason, int ExtraMinutes);

public sealed record ExtendAttemptRequest(int Minutes, string Reason);

public sealed record ExtendExamRequest(int Minutes, string Reason, bool ExtendWindow);

public sealed record ExtendExamResult(int AttemptsExtended, DateTime EndsAt);
