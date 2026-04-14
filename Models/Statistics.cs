#nullable enable
using System;
using System.Collections.Generic;

namespace HttpMonitorApp.Models;

public class Statistics
{
    public int TotalRequests { get; set; }
    public int GetRequests { get; set; }
    public int PostRequests { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public Dictionary<DateTime, int> RequestsPerMinute { get; set; } = new();
}