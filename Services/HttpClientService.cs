#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HttpMonitorApp.Services;

public sealed class HttpClientService : IHttpClient, IDisposable
{
    private readonly System.Net.Http.HttpClient _httpClient;

    public HttpClientService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        _httpClient = new System.Net.Http.HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    // --- ИЗМЕНЕНО: Добавлены методы, возвращающие HttpResponseMessage ---
    public async Task<HttpResponseMessage> GetAsyncRaw(string url)
    {
        var response = await _httpClient.GetAsync(url);
        return response;
    }

    public async Task<HttpResponseMessage> PostAsyncRaw(string url, string jsonBody)
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        return response;
    }
    // --- КОНЕЦ ИЗМЕНЕНИЯ ---

    // --- Оставлены старые методы для совместимости (если используются где-то ещё) ---
    public async Task<string> GetAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PostAsync(string url, string jsonBody) // <-- Исправлен порядок параметров
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    // --- КОНЕЦ СТАРЫХ МЕТОДОВ ---

    public void Dispose() => _httpClient.Dispose();
}