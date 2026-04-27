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

// --- HttpResponse уже определён в IHttpServer.cs, НЕ ОБЪЯВЛЯЙТЕ ЕЁ ЗДЕСЬ СНОВА ---
// public readonly struct HttpResponse { ... } // <-- УДАЛИТЕ ЭТОТ БЛОК, ЕСЛИ СКОПИРОВАЛИ СЮДА

public sealed class HttpServerService : IHttpServer, IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    // --- ИЗМЕНЕНО: Сигнатуры событий ---
    public event Func<RequestInfo, Task<HttpResponse>>? OnGetRequest;
    public event Func<RequestInfo, Task<HttpResponse>>? OnPostRequest;
    // --- КОНЕЦ ИЗМЕНЕНИЯ ---

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
        string? responseBody = null;
        int finalStatusCode = 500; // По умолчанию Internal Server Error
        string? errorDetails = null; // Для логирования ошибок

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;

                var buffer = new byte[8192];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                var lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;

                var firstLine = lines[0].Split(' ');
                if (firstLine.Length < 2) return;

                var method = firstLine[0];
                var url = firstLine[1];

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
                        var key = lines[i].Substring(0, colonIndex).Trim();
                        var value = lines[i].Substring(colonIndex + 1).Trim();
                        headers[key] = value;

                        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.TryParse(value, out var parsedLength) ? parsedLength : 0;
                        }
                    }
                }

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

                Console.WriteLine($"[DEBUG] HTTP Method received: '{method}'");

                if (method == "GET" && OnGetRequest != null)
                {
                    Console.WriteLine("[DEBUG] Calling OnGetRequest");
                    var response = await OnGetRequest(requestInfo); // <-- Вызов обновлённого обработчика
                    responseBody = response.Body; // <-- Получение тела из HttpResponse
                    finalStatusCode = response.StatusCode; // <-- Получение статуса из HttpResponse
                }
                else if (method == "POST" && OnPostRequest != null)
                {
                    Console.WriteLine("[DEBUG] Calling OnPostRequest");
                    var response = await OnPostRequest(requestInfo); // <-- Вызов обновлённого обработчика
                    responseBody = response.Body; // <-- Получение тела из HttpResponse
                    finalStatusCode = response.StatusCode; // <-- Получение статуса из HttpResponse
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Method not handled or no handlers: '{method}'");
                    finalStatusCode = 405; // Method Not Allowed
                    responseBody = "{\"error\":\"Method Not Allowed\"}";
                }

                var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                // --- ИЗМЕНЕНО: Используем finalStatusCode ---
                var responseString = $"HTTP/1.1 {finalStatusCode} {GetStatusDescription(finalStatusCode)}\r\n" +
                                   "Content-Type: application/json\r\n" +
                                   $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody ?? "")}\r\n" +
                                   "\r\n" +
                                   (responseBody ?? "");
                // --- КОНЕЦ ИЗМЕНЕНИЯ ---

                var responseBytes = Encoding.UTF8.GetBytes(responseString);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);

                // --- ИЗМЕНЕНО: Передаём finalStatusCode ---
                OnRequestProcessed?.Invoke(requestInfo, finalStatusCode, responseBody, processingTime);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Handle error: {ex.Message}");
            // Если ошибка произошла внутри блока try, но до отправки ответа,
            // возможно, стоит отправить 500, но только если ответ ещё не отправлен.
            // Для упрощения, просто логируем и позволяем методу завершиться.
            errorDetails = ex.Message;
        }
    }

    // --- НОВОЕ: Вспомогательный метод для получения описания статуса ---
    private static string GetStatusDescription(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Unknown Status Code"
        };
    }
    // --- КОНЕЦ НОВОГО ---


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