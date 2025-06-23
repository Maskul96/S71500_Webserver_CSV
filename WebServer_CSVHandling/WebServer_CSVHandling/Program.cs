using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
    });

    private static readonly string _webServerUrl = "https://192.168.2.1";
    private static readonly string _listUrl = $"{_webServerUrl}/DataLogs?Action=LIST";
    private static readonly string _localFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CSV");
    private static readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
    private static readonly Dictionary<string, DateTime> _downloadedFiles = new();

    private static System.Timers.Timer _timer;

    static async Task Main(string[] args)
    {
        Directory.CreateDirectory(_localFolder);
        File.AppendAllText(_logFile, $"\n--- START {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n");

        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += async (s, e) => await CheckForNewFilesAsync();
        _timer.Start();

        Log("Monitoring Siemens S7-1500 DataLogs...");
        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }

    private static async Task CheckForNewFilesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_listUrl);
            ThrowIfHttpError(response);

            string body = await response.Content.ReadAsStringAsync();
            var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var filePath in lines)
            {
                string fileName = Path.GetFileName(filePath);
                string localPath = Path.Combine(_localFolder, fileName);

                if (File.Exists(localPath))
                {
                    if (!_downloadedFiles.ContainsKey(filePath))
                    {
                        _downloadedFiles[filePath] = File.GetCreationTime(localPath); // Ustaw datę z pliku
                        Log($"[Pominięto – już istnieje] {fileName}");
                    }
                    continue;
                }

                string fileUrl = _webServerUrl + filePath;

                try
                {
                    var fileResponse = await _httpClient.GetAsync(fileUrl);
                    ThrowIfHttpError(fileResponse);

                    var fileBytes = await fileResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, fileBytes);

                    _downloadedFiles[filePath] = DateTime.Now;
                    Log($"[Pobrano] {fileName}");
                }
                catch (Exception ex)
                {
                    Log($"[Błąd pobierania pliku] {fileName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[Błąd ogólny] {ex.Message}");
        }
    }

    private static void ThrowIfHttpError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new Exception("Błąd 401: Unauthorized (brak autoryzacji)");
            case HttpStatusCode.Forbidden:
                throw new Exception("Błąd 403: Forbidden (brak dostępu)");
            case HttpStatusCode.NotFound:
                throw new Exception("Błąd 404: Nie znaleziono zasobu");
            default:
                throw new Exception($"Błąd HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
    }

    private static void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
        catch
        {
            Console.WriteLine("[Błąd zapisu logu do pliku]");
        }
    }
}
