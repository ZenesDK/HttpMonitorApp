#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HttpMonitorApp.Models;
using HttpMonitorApp.Services;

namespace HttpMonitorApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IHttpServer _server;
    private readonly IHttpClient _client;
    private readonly ILoggingService _logger;
    private CancellationTokenSource? _serverCts;
    
    public MainWindowViewModel()
    {
        _server = new HttpServerService();
        _client = new HttpClientService();
        _logger = new LoggingService();
        
        _server.OnGetRequest += HandleGetRequest;
        _server.OnPostRequest += HandlePostRequest;
        
        // ===== ТОЛЬКО ОДНА ПОДПИСКА =====
        if (_server is HttpServerService httpServer)
        {
            httpServer.OnRequestProcessed += LogIncomingRequest;
        }
        // ================================
        
        ServerPort = 8080;
        ClientUrl = "http://127.0.0.1:8080";
        ClientRequestBody = "{\"message\": \"test\"}";
        ClientMethod = "GET";
        IsGetSelected = true;
        IsFilterAll = true;
        _filterType = "ALL";
        
        StartServerCommand = new AsyncRelayCommand(StartServerAsync);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync);
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
        SaveLogsCommand = new AsyncRelayCommand(SaveLogsAsync);
        FilterLogsCommand = new RelayCommand(() => UpdateLogs());
        
        UpdateStatistics();
        UpdatePeakLoad();
        
        var timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (s, e) => 
        {
            UpdateStatistics();
            UpdatePeakLoad();
        };
        timer.Start();
    }
    
    // Server Properties
    private int _serverPort;
    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value);
    }
    
    private bool _isServerRunning;
    public bool IsServerRunning
    {
        get => _isServerRunning;
        set => SetProperty(ref _isServerRunning, value);
    }
    
    private string _serverStatus = "Остановлен";
    public string ServerStatus
    {
        get => _serverStatus;
        set => SetProperty(ref _serverStatus, value);
    }
    
    // Client Properties
    private string _clientUrl = string.Empty;
    public string ClientUrl
    {
        get => _clientUrl;
        set => SetProperty(ref _clientUrl, value);
    }
    
    private string _clientMethod = "GET";
    public string ClientMethod
    {
        get => _clientMethod;
        set => SetProperty(ref _clientMethod, value);
    }
    
    private bool _isGetSelected = true;
    public bool IsGetSelected
    {
        get => _isGetSelected;
        set 
        { 
            if (SetProperty(ref _isGetSelected, value) && value)
                ClientMethod = "GET";
        }
    }

    private bool _isPostSelected;
    public bool IsPostSelected
    {
        get => _isPostSelected;
        set 
        { 
            if (SetProperty(ref _isPostSelected, value) && value)
                ClientMethod = "POST";
        }
    }

    private string _clientRequestBody = string.Empty;
    public string ClientRequestBody
    {
        get => _clientRequestBody;
        set => SetProperty(ref _clientRequestBody, value);
    }
    
    private string _clientResponse = string.Empty;
    public string ClientResponse
    {
        get => _clientResponse;
        set => SetProperty(ref _clientResponse, value);
    }
    
    // Logs & Statistics
    private ObservableCollection<LogEntry> _logs = new();
    public ObservableCollection<LogEntry> Logs
    {
        get => _logs;
        set => SetProperty(ref _logs, value);
    }
    
    private ObservableCollection<string> _peakLoadData = new();
    public ObservableCollection<string> PeakLoadData
    {
        get => _peakLoadData;
        set => SetProperty(ref _peakLoadData, value);
    }

    // Свойства для фильтрации
    private bool _isFilterAll = true;
    public bool IsFilterAll
    {
        get => _isFilterAll;
        set
        {
            Console.WriteLine($"[DEBUG] IsFilterAll SET to: {value}");
            if (SetProperty(ref _isFilterAll, value) && value)
            {
                Console.WriteLine($"[DEBUG] IsFilterAll = true, setting FilterType to ALL");
                FilterType = "ALL";
            }
        }
    }

    private bool _isFilterGet;
    public bool IsFilterGet
    {
        get => _isFilterGet;
        set
        {
            Console.WriteLine($"[DEBUG] IsFilterGet SET to: {value}");
            if (SetProperty(ref _isFilterGet, value) && value)
            {
                Console.WriteLine($"[DEBUG] IsFilterGet = true, setting FilterType to GET");
                FilterType = "GET";
            }
        }
    }

    private bool _isFilterPost;
    public bool IsFilterPost
    {
        get => _isFilterPost;
        set
        {
            Console.WriteLine($"[DEBUG] IsFilterPost SET to: {value}");
            if (SetProperty(ref _isFilterPost, value) && value)
            {
                Console.WriteLine($"[DEBUG] IsFilterPost = true, setting FilterType to POST");
                FilterType = "POST";
            }
        }
    }

    private string _filterType = "ALL";
    public string FilterType
    {
        get => _filterType;
        set
        {
            Console.WriteLine($"[DEBUG] FilterType SET called with value: {value}");
            if (SetProperty(ref _filterType, value))
            {
                Console.WriteLine($"[DEBUG] FilterType changed to: {_filterType}");
                UpdateLogs();
            }
        }
    }
    
    private string _statisticsText = string.Empty;
    public string StatisticsText
    {
        get => _statisticsText;
        set => SetProperty(ref _statisticsText, value);
    }
    
    // Commands
    public AsyncRelayCommand StartServerCommand { get; }
    public AsyncRelayCommand StopServerCommand { get; }
    public AsyncRelayCommand SendRequestCommand { get; }
    public AsyncRelayCommand SaveLogsCommand { get; }
    public RelayCommand FilterLogsCommand { get; }
    
    private async Task StartServerAsync()
    {
        try
        {
            _serverCts = new CancellationTokenSource();
            await _server.StartAsync(ServerPort, _serverCts.Token);
            IsServerRunning = true;
            ServerStatus = $"Запущен на порту {ServerPort}";
            ClientResponse = $"Сервер запущен на порту {ServerPort}";
            
            _logger.AddLog(new LogEntry(
                Guid.NewGuid().ToString(),
                "SYSTEM",
                "SERVER",
                $"http://127.0.0.1:{ServerPort}",
                200,
                null,
                "Server started",
                0,
                DateTime.Now
            ));
            UpdateLogs();
        }
        catch (Exception ex)
        {
            ClientResponse = $"Ошибка сервера: {ex.Message}";
        }
    }
    
    private async Task StopServerAsync()
    {
        _serverCts?.Cancel();
        await _server.StopAsync();
        IsServerRunning = false;
        ServerStatus = "Stopped";
    }
    
    private Task<string?> HandleGetRequest(RequestInfo request)
    {
        var stats = _logger.GetStatistics();
        var response = new
        {
            status = "ok",
            uptime_seconds = Environment.TickCount / 1000,
            total_requests = stats.TotalRequests,
            get_requests = stats.GetRequests,
            post_requests = stats.PostRequests,
            avg_processing_ms = stats.AverageProcessingTimeMs
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        return Task.FromResult<string?>(json);
    }
    
    private Task<string?> HandlePostRequest(RequestInfo request)
    {
        var messageId = Guid.NewGuid().ToString();
        var response = new { id = messageId, received = true, body = request.Body };
        
        var json = System.Text.Json.JsonSerializer.Serialize(response);
        return Task.FromResult<string?>(json);
    }
    
    private void LogIncomingRequest(RequestInfo info, int statusCode, string? responseBody, long processingTime)
    {
        // ===== ДИАГНОСТИКА =====
        Console.WriteLine($"[DEBUG] LogIncomingRequest CALLED");
        Console.WriteLine($"[DEBUG] info.Method = {info.Method}");
        Console.WriteLine($"[DEBUG] info.Url = {info.Url}");
        Console.WriteLine($"[DEBUG] statusCode = {statusCode}");
        // =======================

        var entry = new LogEntry(
            Guid.NewGuid().ToString(),
            "INCOMING",
            info.Method,
            info.Url,
            statusCode,
            info.Body,
            responseBody,
            processingTime,
            DateTime.Now
        );
        
        _logger.AddLog(entry);
        Dispatcher.UIThread.Post(() => 
        {
            UpdateLogs();
            UpdatePeakLoad();
        }); 
    }
    
    private async Task SendRequestAsync()
    {
        // ===== ДИАГНОСТИКА НАЧАЛО =====
        Console.WriteLine($"[DEBUG] SendRequestAsync START");
        Console.WriteLine($"[DEBUG] ClientMethod = {ClientMethod}");
        Console.WriteLine($"[DEBUG] ClientUrl = {ClientUrl}");
        Console.WriteLine($"[DEBUG] IsServerRunning = {IsServerRunning}");
        // ===== ДИАГНОСТИКА КОНЕЦ =====
        
        try
        {
            bool isLocalRequest = ClientUrl.Contains("localhost") || ClientUrl.Contains("127.0.0.1");
            
            if (isLocalRequest && !IsServerRunning)
            {
                ClientResponse = "Ошибка: Сервер не запущен. Нажмите 'Запустить сервер'.";
                return;
            }
            
            var startTime = DateTime.Now;
            string response;
            
            // ===== ДИАГНОСТИКА ПЕРЕД ЗАПРОСОМ =====
            Console.WriteLine($"[DEBUG] About to send {ClientMethod} request to {ClientUrl}");
            // ====================================

            if (ClientMethod == "GET")
            {
                response = await _client.GetAsync(ClientUrl);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ClientRequestBody))
                {
                    ClientResponse = "Ошибка: Тело запроса обязательно для POST";
                    return;
                }
                response = await _client.PostAsync(ClientUrl, ClientRequestBody);
            }
            
            // ===== ДИАГНОСТИКА ПОСЛЕ ЗАПРОСА =====
            Console.WriteLine($"[DEBUG] Response received, length: {response?.Length ?? 0}");
            Console.WriteLine($"[DEBUG] Response content: {response?.Substring(0, Math.Min(200, response?.Length ?? 0))}");
            // ====================================

            var processingTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
            
            ClientResponse = response ?? string.Empty;
            
            var entry = new LogEntry(
                Guid.NewGuid().ToString(),
                "OUTGOING",
                ClientMethod,
                ClientUrl,
                200,
                ClientMethod == "POST" ? ClientRequestBody : null,
                response,
                processingTime,
                DateTime.Now
            );
            
            // ===== ДИАГНОСТИКА ЛОГА =====
            Console.WriteLine($"[DEBUG] Adding OUTGOING log: Method={ClientMethod}, Url={ClientUrl}");
            // ============================

            _logger.AddLog(entry);
            Dispatcher.UIThread.Post(() => UpdateLogs());
        }
        catch (HttpRequestException ex)
        {   
            // ===== ДИАГНОСТИКА ОШИБКИ =====
            Console.WriteLine($"[DEBUG] HttpRequestException: {ex.Message}");
            // ============================            
            
            ClientResponse = $"Ошибка HTTP: {ex.Message}";
            
            var entry = new LogEntry(
                Guid.NewGuid().ToString(),
                "ERROR",
                ClientMethod,
                ClientUrl,
                null,
                ClientMethod == "POST" ? ClientRequestBody : null,
                ex.Message,
                0,
                DateTime.Now
            );
            _logger.AddLog(entry);
            Dispatcher.UIThread.Post(() => UpdateLogs());
        }
        catch (TaskCanceledException ex)
        {
            Console.WriteLine($"[DEBUG] TaskCanceledException: {ex.Message}");
            ClientResponse = $"Ошибка таймаута (30 секунд): {ex.Message}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
            Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");            
            ClientResponse = $"Ошибка: {ex.Message}";
        }
        // ===== ДИАГНОСТИКА КОНЕЦ =====
        Console.WriteLine($"[DEBUG] SendRequestAsync END");
        // ============================
    }
    
    private async Task SaveLogsAsync()
    {
        var filePath = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await _logger.SaveToFileAsync(filePath);
        ClientResponse = $"Логи сохранены в {filePath}";
    }
    
    private void UpdateLogs()
    {
        var allLogs = _logger.GetLogs();
        
        Console.WriteLine($"[DEBUG] ===== UpdateLogs START =====");
        Console.WriteLine($"[DEBUG] Current FilterType = '{FilterType}'");
        Console.WriteLine($"[DEBUG] Total logs count = {allLogs.Count}");
        
        // Выводим все логи
        foreach (var log in allLogs)
        {
            Console.WriteLine($"[DEBUG] Log: Method='{log.Method}', Type='{log.Type}', Url='{log.Url}'");
        }
        
        // Фильтрация
        List<LogEntry> filtered;
        
        if (FilterType == "GET")
        {
            filtered = allLogs.Where(l => l.Method == "GET").ToList();
            Console.WriteLine($"[DEBUG] Filtering by GET, found {filtered.Count} logs");
        }
        else if (FilterType == "POST")
        {
            filtered = allLogs.Where(l => l.Method == "POST").ToList();
            Console.WriteLine($"[DEBUG] Filtering by POST, found {filtered.Count} logs");
        }
        else
        {
            filtered = allLogs.ToList();
            Console.WriteLine($"[DEBUG] No filter (ALL), showing {filtered.Count} logs");
        }
        
        // Очищаем и добавляем
        Logs.Clear();
        foreach (var log in filtered)
        {
            Logs.Add(log);
            Console.WriteLine($"[DEBUG] Added to UI: Method='{log.Method}', Url='{log.Url}'");
        }
        
        Console.WriteLine($"[DEBUG] UI Logs count after update: {Logs.Count}");
        Console.WriteLine($"[DEBUG] ===== UpdateLogs END =====");
    }
    
    private void UpdateStatistics()
    {
        var stats = _logger.GetStatistics();
        
        StatisticsText = 
            $"Всего: {stats.TotalRequests} | " +
            $"GET: {stats.GetRequests} | " +
            $"POST: {stats.PostRequests} | " +
            $"Среднее время: {stats.AverageProcessingTimeMs:F1}ms";
    }

    private void UpdatePeakLoad()
    {
        var stats = _logger.GetStatistics();
        
        PeakLoadData.Clear();
        
        if (stats.RequestsPerMinute.Count == 0)
        {
            PeakLoadData.Add("Данных пока нет");
            return;
        }
        
        foreach (var minute in stats.RequestsPerMinute.OrderByDescending(x => x.Value).Take(10))
        {
            PeakLoadData.Add($"{minute.Key:HH:mm:ss} - {minute.Value} запросов/мин");
        }
    }    
}