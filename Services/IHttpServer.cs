#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HttpMonitorApp.Services;

public interface IHttpServer
{
    event Func<Models.RequestInfo, Task<string?>>? OnGetRequest;
    event Func<Models.RequestInfo, Task<string?>>? OnPostRequest;
    
    Task StartAsync(int port, CancellationToken cancellationToken = default);
    Task StopAsync();
    bool IsRunning { get; }
}