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
        ((HttpServerService)_server).OnRequestProcessed += LogIncomingRequest;
        
        ServerPort = 8080;
        ClientUrl = "http://127.0.0.1:8080";
        ClientRequestBody = "{\"message\": \"test\"}";
        ClientMethod = "GET";
        IsGetSelected = true;
        
        StartServerCommand = new AsyncRelayCommand(StartServerAsync);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync);
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
        SaveLogsCommand = new AsyncRelayCommand(SaveLogsAsync);
        FilterLogsCommand = new RelayCommand(() => UpdateLogs());
        
        UpdateStatistics();
        
        var timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (s, e) => UpdateStatistics();
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
    
    private string _serverStatus = "Stopped";
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
    
    private string _filterType = "ALL";
    public string FilterType
    {
        get => _filterType;
        set
        {
            if (SetProperty(ref _filterType, value))
            {
                Console.WriteLine($"[DEBUG] FilterType changed to: {value}");
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
            ServerStatus = $"Running on port {ServerPort}";
            ClientResponse = $"Server started on port {ServerPort}";
            
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
            ClientResponse = $"Server Error: {ex.Message}";
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
        Dispatcher.UIThread.Post(() => UpdateLogs());
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
                ClientResponse = "Error: Server is not running. Click 'Start Server' first.";
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
                    ClientResponse = "Error: Request body is required for POST";
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
            
            ClientResponse = $"HTTP Error: {ex.Message}";
            
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
            ClientResponse = $"Timeout Error (30 seconds): {ex.Message}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
            Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");            
            ClientResponse = $"Error: {ex.Message}";
        }
        // ===== ДИАГНОСТИКА КОНЕЦ =====
        Console.WriteLine($"[DEBUG] SendRequestAsync END");
        // ============================
    }
    
    private async Task SaveLogsAsync()
    {
        var filePath = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await _logger.SaveToFileAsync(filePath);
        ClientResponse = $"Logs saved to {filePath}";
    }
    
    private void UpdateLogs()
    {
        var allLogs = _logger.GetLogs();
        
        Console.WriteLine($"[DEBUG] UpdateLogs called. FilterType = {FilterType}");
        Console.WriteLine($"[DEBUG] Total logs in logger: {allLogs.Count}");
        
        foreach (var log in allLogs)
        {
            Console.WriteLine($"[DEBUG] Log: Type={log.Type}, Method={log.Method}, Url={log.Url}");
        }
        
        IEnumerable<LogEntry> filtered;
        
        switch (FilterType)
        {
            case "GET":
                filtered = allLogs.Where(l => l.Method == "GET");
                Console.WriteLine($"[DEBUG] Filtering by GET");
                break;
            case "POST":
                filtered = allLogs.Where(l => l.Method == "POST");
                Console.WriteLine($"[DEBUG] Filtering by POST");
                break;
            default:
                filtered = allLogs;
                Console.WriteLine($"[DEBUG] No filter (ALL)");
                break;
        }
        
        var filteredList = filtered.ToList();
        Console.WriteLine($"[DEBUG] Filtered count: {filteredList.Count}");
        
        Logs.Clear();
        foreach (var log in filteredList)
        {
            Logs.Add(log);
            Console.WriteLine($"[DEBUG] Added log to UI: {log.Method} - {log.Url}");
        }
    }
    
    private void UpdateStatistics()
    {
        var stats = _logger.GetStatistics();
        
        StatisticsText = 
            $"Total: {stats.TotalRequests} | " +
            $"GET: {stats.GetRequests} | " +
            $"POST: {stats.PostRequests} | " +
            $"Avg Time: {stats.AverageProcessingTimeMs:F1}ms";
    }
}