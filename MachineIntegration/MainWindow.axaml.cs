using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MachineIntegration.Core;
using MachineIntegration.Models;
using MachineIntegration.Services;

namespace MachineIntegration;

public partial class MainWindow : Window
{
    private MachineConfig? _config;
    private MachineLogic? _machineLogic;
    private DatabaseService? _dbService;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _timestampTimer;

    public MainWindow()
    {
        InitializeComponent();
        
        // Update timestamp every second
        _timestampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timestampTimer.Tick += (s, e) => TimestampText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _timestampTimer.Start();
        
        // Load config on startup
        LoadConfig();
        
        // Test database connection on startup
        _ = TestDatabaseConnectionAsync();
    }

    private void LoadConfig()
    {
        try
        {
            string configFile = GetConfigFilePath();
            
            if (!File.Exists(configFile))
            {
                AppendConverted($"[WARN] Config file not found: {configFile}");
                AppendConverted("[INFO] Using default configuration");
                _config = new MachineConfig();
                CreateDefaultConfig(configFile);
            }
            else
            {
                string json = File.ReadAllText(configFile);
                _config = JsonSerializer.Deserialize<MachineConfig>(json) ?? new MachineConfig();
                AppendConverted($"[INFO] Config loaded: {configFile}");
            }

            // Update UI with config info
            MachineNameText.Text = $"Machine: {_config.MachineName}";
            ConnectionTypeText.Text = $"Type: {_config.Type.ToUpper()}";

            _dbService = new DatabaseService(_config);
            
            // Show database config
            AppendConverted($"[INFO] Database: {_config.Host}:{_config.DbPort}/{_config.Database}");
            
            if (_config.Type.ToLower() == "network")
            {
                ConnectionInfoText.Text = _config.NetworkType.ToLower() == "server" 
                    ? $"Port: {_config.NetworkPort}" 
                    : $"IP: {_config.NetworkIp}:{_config.NetworkPort}";
            }
            else
            {
                ConnectionInfoText.Text = $"Port: {_config.SerialPort} @ {_config.SerialBaudrate}";
            }
        }
        catch (Exception ex)
        {
            AppendConverted($"[ERROR] Failed to load config: {ex.Message}");
            _config = new MachineConfig();
            _dbService = new DatabaseService(_config);
        }
    }

    private async Task TestDatabaseConnectionAsync()
    {
        if (_dbService == null) return;
        
        AppendConverted("[INFO] Testing database connection...");
        bool connected = await _dbService.TestConnectionAsync();
        
        Dispatcher.UIThread.Post(() =>
        {
            UpdateDbStatus(connected, _dbService.LastError);
            
            if (connected)
            {
                AppendConverted($"[INFO] Database connected: {_dbService.Host}/{_dbService.Database}");
            }
            else
            {
                AppendConverted($"[ERROR] Database connection failed: {_dbService.LastError}");
                AppendConverted("[HINT] Check host, port, credentials in config.json");
            }
        });
    }

    private string GetConfigFilePath()
    {
        // Check command line args first
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            return args[1];
        }
        
        // Check current directory
        string localConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (File.Exists(localConfig))
        {
            return localConfig;
        }
        
        return "config.json";
    }

    private void CreateDefaultConfig(string path)
    {
        var defaultConfig = new MachineConfig
        {
            MachineName = "DefaultMachine",
            Type = "network",
            NetworkType = "server",
            NetworkPort = 5200,
            Host = "localhost",
            Database = "flabs_db",
            User = "root",
            Password = ""
        };
        
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, options));
    }

    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_config == null || _dbService == null) return;

        try
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            
            _cts = new CancellationTokenSource();
            _machineLogic = new MachineLogic(_config, _dbService);
            
            // Wire up events
            _machineLogic.RawDataReceived += data => Dispatcher.UIThread.Post(() => AppendRaw(data));
            _machineLogic.ConvertedDataReceived += data => Dispatcher.UIThread.Post(() => AppendConverted(data));
            _machineLogic.DataSent += data => Dispatcher.UIThread.Post(() => AppendSent(data));
            _machineLogic.StatusChanged += status => Dispatcher.UIThread.Post(() => UpdateStatus(status));
            _machineLogic.DatabaseStatusChanged += connected => Dispatcher.UIThread.Post(() => UpdateDbStatus(connected, _dbService.LastError));

            AppendConverted($"[INFO] Starting {_config.MachineName}...");
            
            await _machineLogic.StartAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendConverted("[INFO] Machine stopped");
        }
        catch (Exception ex)
        {
            AppendConverted($"[ERROR] {ex.Message}");
            UpdateStatus($"Error: {ex.Message}");
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _machineLogic?.Stop();
        UpdateStatus("Stopped");
        UpdateConnectionIndicator(false);
        
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        
        AppendConverted("[INFO] Machine stopped by user");
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        RawDataBox.Text = "";
        ConvertedDataBox.Text = "";
        SentDataBox.Text = "";
    }

    private void AppendRaw(string text)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        RawDataBox.Text += $"[{timestamp}] {text}\n";
        ScrollToEnd(RawDataBox);
    }

    private void AppendConverted(string text)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        ConvertedDataBox.Text += $"[{timestamp}] {text}\n";
        ScrollToEnd(ConvertedDataBox);
    }

    private void AppendSent(string text)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        SentDataBox.Text += $"[{timestamp}] {text}\n";
        ScrollToEnd(SentDataBox);
    }

    private void ScrollToEnd(TextBox textBox)
    {
        // Auto-scroll to bottom
        textBox.CaretIndex = textBox.Text?.Length ?? 0;
    }

    private void UpdateStatus(string status)
    {
        StatusText.Text = $"Status: {status}";
        
        bool isConnected = status.Contains("Connected") || 
                          status.Contains("Listening") || 
                          status.Contains("Client connected");
        UpdateConnectionIndicator(isConnected);
    }

    private void UpdateConnectionIndicator(bool connected)
    {
        ConnectionIndicator.Fill = connected 
            ? new SolidColorBrush(Color.Parse("#3fb950")) 
            : new SolidColorBrush(Color.Parse("#f85149"));
    }

    private void UpdateDbStatus(bool connected, string error = "")
    {
        DbStatusIndicator.Fill = connected 
            ? new SolidColorBrush(Color.Parse("#3fb950")) 
            : new SolidColorBrush(Color.Parse("#f85149"));
        
        if (connected)
        {
            DbStatusText.Text = "DB: Connected";
        }
        else
        {
            // Show shortened error in status bar
            string shortError = string.IsNullOrEmpty(error) ? "Disconnected" : 
                               error.Length > 30 ? error.Substring(0, 30) + "..." : error;
            DbStatusText.Text = $"DB: {shortError}";
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _cts?.Cancel();
        _machineLogic?.Dispose();
        _timestampTimer.Stop();
        base.OnClosing(e);
    }
}
