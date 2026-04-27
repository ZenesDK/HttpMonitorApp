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
using System.Net; // Для HttpStatusCode
using Newtonsoft.Json.Linq; // Для JObject.Parse и JToken

namespace HttpMonitorApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IHttpServer _server;
    private readonly IHttpClient _client;
    private readonly ILoggingService _logger;
    private CancellationTokenSource? _serverCts;

    public MainWindowViewModel()
    {
        // --- ВАЖНО: Помните, что для полноценного DI нужно изменить Program.cs и App.axaml.cs ---
        // Пока что зависимости создаются напрямую, как и раньше.
        _server = new HttpServerService();
        _client = new HttpClientService();
        _logger = new LoggingService();

        // --- ИЗМЕНЕНО: Подписка на события ---
        _server.OnGetRequest += HandleGetRequest;
        _server.OnPostRequest += HandlePostRequest;
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---

        if (_server is HttpServerService httpServer)
        {
            httpServer.OnRequestProcessed += LogIncomingRequest;
        }

        ServerPort = 8080;
        ClientUrl = "http://127.0.0.1:8080";
        ClientRequestBody = "{\"message\": \"test\"}";
        ClientMethod = "GET";
        IsGetSelected = true;
        IsFilterAll = true;
        _filterType = "ALL";
        // --- ИЗМЕНЕНО: Установка по умолчанию для пиковой нагрузки ---
        ShowPeakPerMinute = true; // По умолчанию показываем минуты
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---

        StartServerCommand = new AsyncRelayCommand(StartServerAsync);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync);
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
        SaveLogsCommand = new AsyncRelayCommand(SaveLogsAsync);
        FilterLogsCommand = new RelayCommand(() => UpdateLogs());
        // --- НОВОЕ: Команда для переключения фильтра по статусу ---
        ToggleStatusFilterCommand = new RelayCommand(() => IsFilterByStatus = !IsFilterByStatus);
        // --- КОНЕЦ НОВОГО ---

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

    // Свойства для фильтрации по методу
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

    // --- НОВОЕ: Свойства для фильтрации по статусу ---
    private bool _isFilterByStatus = false;
    public bool IsFilterByStatus
    {
        get => _isFilterByStatus;
        set => SetProperty(ref _isFilterByStatus, value);
    }

    private string _selectedStatusCodeFilter = "ALL"; // "ALL", "200", "400", "500", etc.
    public string SelectedStatusCodeFilter
    {
        get => _selectedStatusCodeFilter;
        set
        {
            if (SetProperty(ref _selectedStatusCodeFilter, value))
            {
                UpdateLogs(); // Обновляем логи при изменении фильтра
            }
        }
    }

    private ObservableCollection<string> _availableStatusCodes = new()
    {
        "ALL", "200", "201", "400", "404", "405", "429", "500", "502", "503"
    };
    public ObservableCollection<string> AvailableStatusCodes => _availableStatusCodes;
    // --- КОНЕЦ НОВОГО ---

    // --- НОВОЕ: Свойства для выбора типа нагрузки ---
    private bool _showPeakPerMinute = true; // По умолчанию показываем минуты
    public bool ShowPeakPerMinute
    {
        get => _showPeakPerMinute;
        set
        {
            if (SetProperty(ref _showPeakPerMinute, value))
            {
                UpdatePeakLoad(); // Обновляем график при смене типа
            }
        }
    }

    private bool _showPeakPerHour;
    public bool ShowPeakPerHour
    {
        get => _showPeakPerHour;
        set
        {
            if (SetProperty(ref _showPeakPerHour, value))
            {
                UpdatePeakLoad(); // Обновляем график при смене типа
            }
        }
    }
    // --- КОНЕЦ НОВОГО ---

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
    // --- НОВОЕ: Команда для переключения фильтра по статусу ---
    public RelayCommand ToggleStatusFilterCommand { get; }
    // --- КОНЕЦ НОВОГО ---

    // --- ИЗМЕНЁННЫЙ HandleGetRequest ---
    private Task<HttpResponse> HandleGetRequest(RequestInfo request)
    {
        // Парсим URL для проверки параметров
        var uri = new System.Uri(request.Url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        // Пример: если в URL есть параметр error и он равен 400, возвращаем 400 Bad Request
        var errorCodeParam = query["error"];
        if (int.TryParse(errorCodeParam, out int errorCode) && errorCode >= 400 && errorCode < 600)
        {
            string errorMessage = errorCode switch
            {
                400 => "{\"error\":\"Bad Request\", \"code\": 400}",
                404 => "{\"error\":\"Not Found\", \"code\": 404}",
                429 => "{\"error\":\"Too Many Requests\", \"code\": 429}",
                500 => "{\"error\":\"Internal Server Error\", \"code\": 500}",
                502 => "{\"error\":\"Bad Gateway\", \"code\": 502}",
                503 => "{\"error\":\"Service Unavailable\", \"code\": 503}",
                _ => "{\"error\":\"An error occurred\", \"code\": " + errorCode + "}"
            };
            return Task.FromResult(new HttpResponse(errorCode, errorMessage));
        }

        // Если всё хорошо, возвращаем 200 OK
        var stats = _logger.GetStatistics();
        var response = new
        {
            status = "ok",
            uptime_seconds = Environment.TickCount64 / 1000, // Используем TickCount64 для лучшей точности
            total_requests = stats.TotalRequests,
            get_requests = stats.GetRequests,
            post_requests = stats.PostRequests,
            avg_processing_ms = stats.AverageProcessingTimeMs
        };

        var json = System.Text.Json.JsonSerializer.Serialize(response);
        return Task.FromResult(new HttpResponse(200, json));
    }
    // --- КОНЕЦ HandleGetRequest ---

    // --- ИЗМЕНЁННЫЙ HandlePostRequest ---
    private Task<HttpResponse> HandlePostRequest(RequestInfo request)
    {
        // Проверяем, есть ли тело
        if (string.IsNullOrEmpty(request.Body))
        {
            return Task.FromResult(new HttpResponse(400, "{\"error\":\"Request body is required for POST requests\", \"code\": 400}"));
        }

        // Пробуем десериализовать JSON
        try
        {
            // Простая проверка: если в теле есть ключ "fail" со значением true, возвращаем 500
            var bodyObj = JObject.Parse(request.Body); // <-- Используем JObject из Newtonsoft.Json.Linq
            if (bodyObj["fail"]?.Value<bool>() == true) // <-- Используем ?. и Value<bool>()
            {
                 return Task.FromResult(new HttpResponse(500, "{\"error\":\"Server processed request but encountered an internal failure\", \"code\": 500}"));
            }

            // Если всё хорошо, возвращаем 201 Created
            var messageId = Guid.NewGuid().ToString();
            var response = new { id = messageId, received = true, body = request.Body };

            var json = System.Text.Json.JsonSerializer.Serialize(response);
            return Task.FromResult(new HttpResponse(201, json));
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            // Некорректный JSON
            return Task.FromResult(new HttpResponse(400, "{\"error\":\"Invalid JSON format in request body\", \"code\": 400}"));
        }
        catch (Exception)
        {
             // Любая другая ошибка при обработке
             return Task.FromResult(new HttpResponse(500, "{\"error\":\"Internal Server Error during POST processing\", \"code\": 500}"));
        }
    }
    // --- КОНЕЦ HandlePostRequest ---


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
                200, // Логируем статус запуска
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
        ServerStatus = "Остановлен";
    }

    // --- ИЗМЕНЁННЫЙ SendRequestAsync (startTime moved to beginning) ---
    private async Task SendRequestAsync()
    {
        // ВАЖНО: startTime объявлен здесь, в начале метода, перед любыми await
        var startTime = DateTime.Now;

        Console.WriteLine($"[DEBUG] SendRequestAsync START ");
        Console.WriteLine($"[DEBUG] ClientMethod = {ClientMethod} ");
        Console.WriteLine($"[DEBUG] ClientUrl = {ClientUrl} ");
        Console.WriteLine($"[DEBUG] IsServerRunning = {IsServerRunning} ");


        try
        {
            bool isLocalRequest = ClientUrl.Contains("localhost") || ClientUrl.Contains("127.0.0.1");

            if (isLocalRequest && !IsServerRunning)
            {
                ClientResponse = "Ошибка: Сервер не запущен. Нажмите 'Запустить сервер'.";
                return;
            }


            string responseContent;
            HttpResponseMessage httpResponseMsg; // Для получения статуса от клиента

            Console.WriteLine($"[DEBUG] About to send {ClientMethod} request to {ClientUrl}");

            if (ClientMethod == "GET")
            {
                httpResponseMsg = await _client.GetAsyncRaw(ClientUrl); // <-- ИСПОЛЬЗУЕМ Raw
                responseContent = await httpResponseMsg.Content.ReadAsStringAsync();
            }
            else // POST
            {
                if (string.IsNullOrWhiteSpace(ClientRequestBody))
                {
                    ClientResponse = "Ошибка: Тело запроса обязательно для POST";
                    return;
                }
                httpResponseMsg = await _client.PostAsyncRaw(ClientUrl, ClientRequestBody); // <-- ИСПОЛЬЗУЕМ Raw
                responseContent = await httpResponseMsg.Content.ReadAsStringAsync();
            }

            Console.WriteLine($"[DEBUG] Response received, length: {responseContent?.Length ?? 0}");
            Console.WriteLine($"[DEBUG] Response status code: {httpResponseMsg.StatusCode}"); // <-- Показываем код
            Console.WriteLine($"[DEBUG] Response content: {responseContent?.Substring(0, Math.Min(200, responseContent?.Length ?? 0))}");

            var processingTime = (long)(DateTime.Now - startTime).TotalMilliseconds; // <-- Используем startTime

            // --- ОБНОВЛЕНО: Показываем статус и тело ---
            ClientResponse = $"{httpResponseMsg.StatusCode} ({(int)httpResponseMsg.StatusCode}): {responseContent}";

            // --- ОБНОВЛЕНО: Логируем статус, полученный от сервера ---
            var entry = new LogEntry(
                Guid.NewGuid().ToString(),
                "OUTGOING",
                ClientMethod,
                ClientUrl,
                (int)httpResponseMsg.StatusCode, // <-- Логируем статус, полученный от сервера
                ClientMethod == "POST" ? ClientRequestBody : null,
                responseContent,
                processingTime,
                DateTime.Now
            );

            Console.WriteLine($"[DEBUG] Adding OUTGOING log: Method={ClientMethod}, Url={ClientUrl}, StatusCode={(int)httpResponseMsg.StatusCode}");

            _logger.AddLog(entry);
            Dispatcher.UIThread.Post(() => UpdateLogs());
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[DEBUG] HttpRequestException: {ex.Message}");
            ClientResponse = $"Ошибка HTTP: {ex.Message}";
            // Логика для добавления ошибки в лог... (например, если соединение разорвалось до получения ответа)
            var entry = new LogEntry(
                Guid.NewGuid().ToString(),
                "OUTGOING_ERROR",
                ClientMethod,
                ClientUrl,
                0, // или специальный код для сетевой ошибки
                ClientMethod == "POST" ? ClientRequestBody : null,
                ex.Message,
                (long)(DateTime.Now - startTime).TotalMilliseconds, // <-- Используем startTime
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
        Console.WriteLine($"[DEBUG] SendRequestAsync END");
    }
    // --- КОНЕЦ SendRequestAsync ---


    private async Task SaveLogsAsync()
    {
        var filePath = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await _logger.SaveToFileAsync(filePath);
        ClientResponse = $"Логи сохранены в {filePath}";
    }

    private void LogIncomingRequest(RequestInfo info, int statusCode, string? responseBody, long processingTime)
    {
        Console.WriteLine($"[DEBUG] LogIncomingRequest CALLED ");
        Console.WriteLine($"[DEBUG] info.Method = {info.Method} ");
        Console.WriteLine($"[DEBUG] info.Url = {info.Url} ");
        Console.WriteLine($"[DEBUG] statusCode = {statusCode} "); // <-- Теперь будет правильный код

        var entry = new LogEntry(
            Guid.NewGuid().ToString(),
            "INCOMING",
            info.Method,
            info.Url,
            statusCode, // <-- Теперь будет правильный код
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

    // --- ИЗМЕНЁННЫЙ UpdateLogs ---
    private void UpdateLogs()
    {
        var allLogs = _logger.GetLogs();

        Console.WriteLine($"[DEBUG] ===== UpdateLogs START =====");
        Console.WriteLine($"[DEBUG] Current FilterType = '{FilterType}'");
        Console.WriteLine($"[DEBUG] Current SelectedStatusCodeFilter = '{SelectedStatusCodeFilter}'");
        Console.WriteLine($"[DEBUG] IsFilterByStatus = '{IsFilterByStatus}'");
        Console.WriteLine($"[DEBUG] Total logs count = {allLogs.Count}");

        List<LogEntry> filtered;

        // Сначала фильтруем по методу (как раньше)
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
            Console.WriteLine($"[DEBUG] No filter (ALL) by Method, showing {filtered.Count} logs");
        }

        // --- НОВОЕ: Затем фильтруем по статус-коду ---
        if (IsFilterByStatus && SelectedStatusCodeFilter != "ALL")
        {
            if (int.TryParse(SelectedStatusCodeFilter, out int targetStatusCode))
            {
                filtered = filtered.Where(l => l.StatusCode == targetStatusCode).ToList();
                Console.WriteLine($"[DEBUG] Filtering by StatusCode {targetStatusCode}, found {filtered.Count} logs");
            }
            else
            {
                 // Если SelectedStatusCodeFilter не "ALL" и не число, фильтруем по "ALL" (или можно просто вывести лог)
                 Console.WriteLine($"[DEBUG] SelectedStatusCodeFilter '{SelectedStatusCodeFilter}' is not numeric, showing ALL filtered by method.");
            }
        }
        else if (IsFilterByStatus) // SelectedStatusCodeFilter == "ALL"
        {
             Console.WriteLine($"[DEBUG] SelectedStatusCodeFilter is ALL, showing all filtered by method.");
        }
        else // !IsFilterByStatus
        {
             Console.WriteLine($"[DEBUG] IsFilterByStatus is false, skipping StatusCode filter.");
        }
        // --- КОНЕЦ НОВОГО ---

        Logs.Clear();
        foreach (var log in filtered)
        {
            Logs.Add(log);
            Console.WriteLine($"[DEBUG] Added to UI: Method='{log.Method}', StatusCode='{log.StatusCode}', Url='{log.Url}'");
        }

        Console.WriteLine($"[DEBUG] UI Logs count after update: {Logs.Count}");
        Console.WriteLine($"[DEBUG] ===== UpdateLogs END =====");
    }
    // --- КОНЕЦ UpdateLogs ---

    // --- ИЗМЕНЁННЫЙ UpdateStatistics ---
    private void UpdateStatistics()
    {
        var stats = _logger.GetStatistics();

        StatisticsText =
            $"Всего: {stats.TotalRequests} |  " +
            $"GET: {stats.GetRequests} |  " +
            $"POST: {stats.PostRequests} |  " +
            $"Среднее время: {stats.AverageProcessingTimeMs:F1}ms ";
    }
    // --- КОНЕЦ UpdateStatistics ---

    // --- ИЗМЕНЁННЫЙ UpdatePeakLoad ---
    private void UpdatePeakLoad()
    {
        var stats = _logger.GetStatistics();

        PeakLoadData.Clear();

        // Выбираем, какие данные использовать
        var dataToUse = ShowPeakPerMinute ? stats.RequestsPerMinute : stats.RequestsPerHour;
        var timeFormat = ShowPeakPerMinute ? "HH:mm:ss" : "dd.MM.yyyy HH:00"; // Формат времени

        if (dataToUse.Count == 0)
        {
            PeakLoadData.Add("Данных пока нет");
            return;
        }

        foreach (var period in dataToUse.OrderByDescending(x => x.Value).Take(10))
        {
            PeakLoadData.Add($"{period.Key.ToString(timeFormat)} - {period.Value} запросов/{(ShowPeakPerMinute ? "мин" : "час")}");
        }
    }
    // --- КОНЕЦ UpdatePeakLoad ---
}