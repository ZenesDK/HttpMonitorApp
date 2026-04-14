#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
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
    
    public async Task<string> GetAsync(string url)
    {
        // Убираем CancellationToken, используем только Timeout
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    
    public async Task<string> PostAsync(string url, string jsonBody)
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    
    public void Dispose() => _httpClient.Dispose();
}