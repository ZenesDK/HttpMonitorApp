#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using HttpMonitorApp.Models;

namespace HttpMonitorApp.Services;

public interface ILoggingService
{
    void AddLog(LogEntry entry);
    IReadOnlyList<LogEntry> GetLogs();
    Task SaveToFileAsync(string filePath);
    Statistics GetStatistics();
    void ClearLogs();
}