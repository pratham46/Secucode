namespace Secucode.Api.Common;

public static class TimeExtensions
{
    public static DateTime UtcNow(this TimeProvider time) => time.GetUtcNow().UtcDateTime;
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.UtcNow());
}
