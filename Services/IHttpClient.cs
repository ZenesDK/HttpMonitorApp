#nullable enable
using System.Threading.Tasks;

namespace HttpMonitorApp.Services;

public interface IHttpClient
{
    Task<string> GetAsync(string url);
    Task<string> PostAsync(string url, string jsonBody);
}