#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpMonitorApp.Models;
using Newtonsoft.Json;

namespace HttpMonitorApp.Services;

public sealed class HttpServerService : IHttpServer, IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    
    public event Func<RequestInfo, Task<string?>>? OnGetRequest;
    public event Func<RequestInfo, Task<string?>>? OnPostRequest;
    public event Action<RequestInfo, int, string?, long>? OnRequestProcessed;
    
    public bool IsRunning { get; private set; }
    
    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener != null)
            await StopAsync();
        
        _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        try
        {
            _listener.Start();
            IsRunning = true;
            
            Console.WriteLine($"[DEBUG] TcpListener started on port {port}");
            
            _ = Task.Run(() => ProcessRequestsAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to start: {ex.Message}");
            throw;
        }
        
        await Task.CompletedTask;
    }
    
    private async Task ProcessRequestsAsync(CancellationToken token)
    {
        if (_listener == null) return;
        
        while (!token.IsCancellationRequested && IsRunning)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Accept error: {ex.Message}");
            }
        }
    }
    
    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                
                // Читаем HTTP-запрос
                var buffer = new byte[8192];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                // Парсим HTTP-запрос
                var lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;
                
                var firstLine = lines[0].Split(' ');
                if (firstLine.Length < 2) return;
                
                var method = firstLine[0];
                var url = firstLine[1];
                
                // Парсим заголовки и тело
                var headers = new Dictionary<string, string?>();
                string? body = null;
                int contentLength = 0;
                int emptyLineIndex = -1;
                
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i]))
                    {
                        emptyLineIndex = i;
                        break;
                    }
                    
                    var colonIndex = lines[i].IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = lines[i].Substring(0, colonIndex);
                        var value = lines[i].Substring(colonIndex + 1).Trim();
                        headers[key] = value;
                        
                        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(value);
                        }
                    }
                }
                
                // Читаем тело
                if (contentLength > 0 && emptyLineIndex > 0 && emptyLineIndex + 1 < lines.Length)
                {
                    body = string.Join("\r\n", lines, emptyLineIndex + 1, lines.Length - emptyLineIndex - 1);
                    if (body.Length > contentLength)
                        body = body.Substring(0, contentLength);
                }
                
                var fullUrl = $"http://localhost:{((IPEndPoint)_listener.LocalEndpoint).Port}{url}";
                
                var requestInfo = new RequestInfo(
                    method,
                    fullUrl,
                    headers,
                    body,
                    startTime
                );
                
                string? responseBody = null;
                int statusCode = 200;
                
                Console.WriteLine($"[DEBUG] HTTP Method received: '{method}'");

                if (method == "GET" && OnGetRequest != null)
                {
                    Console.WriteLine("[DEBUG] Calling OnGetRequest");
                    responseBody = await OnGetRequest(requestInfo);
                }
                else if (method == "POST" && OnPostRequest != null)
                {
                    Console.WriteLine("[DEBUG] Calling OnPostRequest");
                    responseBody = await OnPostRequest(requestInfo);
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Method not handled: '{method}'");
                }
                
                var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Формируем HTTP-ответ
                var response = $"HTTP/1.1 {statusCode} OK\r\n" +
                              "Content-Type: application/json\r\n" +
                              $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\n" +
                              "\r\n" +
                              responseBody;
                
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                
                OnRequestProcessed?.Invoke(requestInfo, statusCode, responseBody, processingTime);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Handle error: {ex.Message}");
        }
    }
    
    public async Task StopAsync()
    {
        IsRunning = false;
        _cts?.Cancel();
        
        if (_listener != null)
        {
            _listener.Stop();
            _listener = null;
        }
        
        await Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();
    }
}