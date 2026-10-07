namespace Secucode.Api.Common;

// Thrown anywhere in a request; ErrorHandling turns it into an RFC 7807 response.
public sealed class ApiException(int status, string title, string? detail = null, IDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException NotFound(string what) => new(404, "Not found", $"{what} was not found.");
    public static ApiException Forbidden(string detail) => new(403, "Forbidden", detail);
    public static ApiException Conflict(string detail) => new(409, "Conflict", detail);
    public static ApiException Unauthorized(string detail) => new(401, "Unauthorized", detail);
    public static ApiException TooManyRequests(string detail) => new(429, "Too many requests", detail);

    public static ApiException Validation(string field, string message) =>
        new(400, "Validation failed", "One or more fields are invalid.", new Dictionary<string, string[]> { [field] = [message] });
}
