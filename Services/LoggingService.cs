#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HttpMonitorApp.Models;
using Newtonsoft.Json;

namespace HttpMonitorApp.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();
    
    public void AddLog(LogEntry entry)
    {
        lock (_lock)
        {
            _logs.Insert(0, entry);
            if (_logs.Count > 1000)
                _logs.RemoveRange(1000, _logs.Count - 1000);
        }
    }
    
    public IReadOnlyList<LogEntry> GetLogs()
    {
        lock (_lock)
        {
            return _logs.ToList();
        }
    }
    
    public async Task SaveToFileAsync(string filePath)
    {
        var logs = GetLogs();
        var json = JsonConvert.SerializeObject(logs, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json);
    }
    
    public Statistics GetStatistics()
    {
        lock (_lock)
        {
            if (_logs.Count == 0)
                return new Statistics();
            
            var stats = new Statistics
            {
                TotalRequests = _logs.Count,
                GetRequests = _logs.Count(l => l.Method == "GET"),
                PostRequests = _logs.Count(l => l.Method == "POST"),
                AverageProcessingTimeMs = _logs.Average(l => l.ProcessingTimeMs)
            };
            
            foreach (var log in _logs.Where(l => l.Type == "INCOMING"))
            {
                var minute = new DateTime(log.Timestamp.Year, log.Timestamp.Month, log.Timestamp.Day, 
                    log.Timestamp.Hour, log.Timestamp.Minute, 0);
                
                if (!stats.RequestsPerMinute.ContainsKey(minute))
                    stats.RequestsPerMinute[minute] = 0;
                
                stats.RequestsPerMinute[minute]++;
            }
            
            return stats;
        }
    }
    
    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
    }
}