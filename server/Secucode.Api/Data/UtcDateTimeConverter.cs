using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Secucode.Api.Data;

// MySQL DATETIME has no zone; everything we store is UTC, so mark it UTC on the way out.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
