#nullable enable
using System.Net.Http; // <-- Добавлено
using System.Threading.Tasks;

namespace HttpMonitorApp.Services;

// HttpResponse больше НЕ определяется здесь

public interface IHttpClient
{
    // --- ИЗМЕНЕНО: Добавлены методы для получения полного ответа ---
    Task<HttpResponseMessage> GetAsyncRaw(string url);
    Task<HttpResponseMessage> PostAsyncRaw(string url, string jsonBody);
    // --- КОНЕЦ ИЗМЕНЕНИЯ ---

    // --- Можно оставить старые для совместимости ---
    Task<string> GetAsync(string url);
    Task<string> PostAsync(string jsonBody, string url); // <-- Исправлен порядок параметров
}