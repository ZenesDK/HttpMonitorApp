#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitorApp.Services;

// --- ЕДИНСТВЕННОЕ ОПРЕДЕЛЕНИЕ HttpResponse ---
public readonly struct HttpResponse
{
    public int StatusCode { get; }
    public string? Body { get; }

    public HttpResponse(int statusCode, string? body = null)
    {
        StatusCode = statusCode;
        Body = body;
    }
}
// --- КОНЕЦ ОПРЕДЕЛЕНИЯ HttpResponse ---

public interface IHttpServer
{
    // --- ИЗМЕНЕНО: Сигнатуры событий ---
    event Func<Models.RequestInfo, Task<HttpResponse>>? OnGetRequest;
    event Func<Models.RequestInfo, Task<HttpResponse>>? OnPostRequest;
    // --- КОНЕЦ ИЗМЕНЕНИЯ ---

    Task StartAsync(int port, CancellationToken cancellationToken = default);
    Task StopAsync();
    bool IsRunning { get; }
}