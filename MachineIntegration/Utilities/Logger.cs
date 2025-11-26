namespace MachineIntegration.Utilities;

public class Logger
{
    private readonly string _logDirectory;
    private readonly string _prefix;

    public Logger(string prefix = "General")
    {
        _prefix = prefix;
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Log(string message)
    {
        try
        {
            string fileName = $"{_prefix}_{DateTime.Now:yyyy-MM-dd}.log";
            string filePath = Path.Combine(_logDirectory, fileName);
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            
            // Only write to file, no console output
            File.AppendAllText(filePath, logEntry + Environment.NewLine);
        }
        catch
        {
            // Silently ignore logging errors
        }
    }

    public void LogData(string direction, string data)
    {
        Log($"{direction,-15} {data}");
    }

    public void LogError(string message, Exception? ex = null)
    {
        string errorMsg = ex != null ? $"{message}: {ex.Message}" : message;
        Log($"[ERROR] {errorMsg}");
    }
}
