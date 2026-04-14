#nullable enable
using System;
using System.Collections.Generic;

namespace HttpMonitorApp.Models;

public record RequestInfo(
    string Method,
    string Url,
    Dictionary<string, string?> Headers,
    string? Body,
    DateTime Timestamp
);