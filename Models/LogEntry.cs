#nullable enable
using System;

namespace HttpMonitorApp.Models;

public record LogEntry(
    string Id,
    string Type,
    string Method,
    string Url,
    int? StatusCode,
    string? RequestBody,
    string? ResponseBody,
    long ProcessingTimeMs,
    DateTime Timestamp
)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}