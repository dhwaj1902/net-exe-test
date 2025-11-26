using System.Text;
using System.Text.RegularExpressions;
using MachineIntegration.Models;
using MachineIntegration.Services;
using MachineIntegration.Utilities;

namespace MachineIntegration.Core;

/// <summary>
/// Main logic for machine communication (similar to Maglumi800TcpBI pattern)
/// Handles ASTM/LIS2-A2 protocol for both Serial and Network connections
/// </summary>
public class MachineLogic : IDisposable
{
    private readonly MachineConfig _config;
    private readonly DatabaseService _dbService;
    private readonly Logger _logger;
    private readonly Logger _traceLogger;
    
    // Connection helpers
    private SerialHelper? _serialHelper;
    private NetworkHelper? _networkHelper;
    
    // Protocol state
    private string _messageBuffer = "";
    private string _messageData = "";
    private string _sendData = "";
    private string _nextMsg = "";
    private bool _isTransfer = false;
    private int _prefix = 1;
    private int _strIndex = 0;
    
    // Events for UI updates
    public event Action<string>? RawDataReceived;      // Raw hex data from machine
    public event Action<string>? ConvertedDataReceived; // Parsed/converted data
    public event Action<string>? DataSent;              // Data sent to machine
    public event Action<string>? StatusChanged;         // Connection status
    public event Action<bool>? DatabaseStatusChanged;   // Database connection status
    
    public bool IsRunning { get; private set; }
    public string MachineName => _config.MachineName;

    public MachineLogic(MachineConfig config, DatabaseService dbService)
    {
        _config = config;
        _dbService = dbService;
        _logger = new Logger(config.MachineName);
        _traceLogger = new Logger("Trace");
    }

    /// <summary>
    /// Start the machine connection
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        StatusChanged?.Invoke($"Starting {_config.MachineName}...");
        
        // Test database connection
        bool dbConnected = await _dbService.TestConnectionAsync();
        DatabaseStatusChanged?.Invoke(dbConnected);
        
        if (_config.Type.ToLower() == "network")
        {
            await StartNetworkAsync(cancellationToken);
        }
        else
        {
            await StartSerialAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Stop the machine connection
    /// </summary>
    public void Stop()
    {
        IsRunning = false;
        _serialHelper?.Close();
        _networkHelper?.Stop();
        StatusChanged?.Invoke("Stopped");
        _logger.Log("Machine stopped");
    }

    #region Network Mode

    private async Task StartNetworkAsync(CancellationToken cancellationToken)
    {
        _networkHelper = new NetworkHelper(_config.MachineName);
        _networkHelper.DataReceived += OnNetworkDataReceived;
        _networkHelper.ClientConnected += ep => StatusChanged?.Invoke($"Client connected: {ep}");
        _networkHelper.ClientDisconnected += () => StatusChanged?.Invoke("Client disconnected");
        _networkHelper.ErrorOccurred += err => StatusChanged?.Invoke($"Error: {err}");

        if (_config.NetworkType.ToLower() == "server")
        {
            StatusChanged?.Invoke($"Listening on port {_config.NetworkPort}...");
            await _networkHelper.StartServerAsync(_config.NetworkPort, cancellationToken);
            _logger.Log($"TCP Server started on port {_config.NetworkPort}");
        }
        else
        {
            StatusChanged?.Invoke($"Connecting to {_config.NetworkIp}:{_config.NetworkPort}...");
            await _networkHelper.ConnectAsync(_config.NetworkIp, _config.NetworkPort, cancellationToken);
        }

        IsRunning = true;
        
        // Keep running
        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            await Task.Delay(100, cancellationToken);
        }
    }

    private void OnNetworkDataReceived(byte[] data)
    {
        try
        {
            string hexData = AstmHelper.ByteArrayToHexString(data);
            
            // Remove trailing zeros
            int zeroIndex = hexData.IndexOf("00");
            if (zeroIndex > 0)
                hexData = hexData.Substring(0, zeroIndex).Trim();

            string asciiData = AstmHelper.HexStringToAscii(hexData.Replace(" ", ""));
            
            // Fire raw data event
            RawDataReceived?.Invoke($"[RX] {hexData}\n      {asciiData}");
            _traceLogger.LogData($"{_config.MachineName}:", asciiData);

            string recMsg = hexData.Replace(" ", "");
            
            // Handle control characters
            if (recMsg == "05") // ENQ
            {
                if (_config.NetworkAck) SendNetworkAck();
                ConvertedDataReceived?.Invoke("[ENQ] Machine starting transmission");
            }
            else if (recMsg == "02") // STX
            {
                if (_config.NetworkAck) SendNetworkAck();
            }
            else if (recMsg == "03") // ETX
            {
                if (_config.NetworkAck) SendNetworkAck();
            }
            else if (recMsg == "04") // EOT
            {
                if (_config.NetworkAck) SendNetworkAck();
                ConvertedDataReceived?.Invoke("[EOT] Machine ended transmission");
            }

            // Handle transfer mode (sending data to machine)
            if (_isTransfer)
            {
                HandleNetworkTransferResponse(recMsg);
            }

            // Accumulate message data
            _messageData = (_messageData.Trim() + " " + hexData.Trim()).Replace("06", "").Trim();

            if (!_messageData.StartsWith("05"))
            {
                _messageData = "";
            }

            // Complete message (ENQ ... EOT)
            if (_messageData.StartsWith("05") && _messageData.EndsWith("04"))
            {
                ProcessCompleteMessage(_messageData);
                _messageData = "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Network data processing error", ex);
        }
    }

    private void HandleNetworkTransferResponse(string recMsg)
    {
        if (!recMsg.Contains("06")) // Not ACK
        {
            SendNetworkMessage(AstmHelper.EOT.ToString());
            DataSent?.Invoke("[EOT] Transfer aborted");
            _isTransfer = false;
            _sendData = "";
            _prefix = 1;
            _nextMsg = "";
            return;
        }

        // ACK received
        if (_nextMsg == "02")
        {
            SendNetworkMessage(AstmHelper.STX.ToString());
            DataSent?.Invoke("[STX] Start of text");
            _nextMsg = "";
        }
        else if (_nextMsg == "04")
        {
            SendNetworkMessage(AstmHelper.EOT.ToString());
            DataSent?.Invoke("[EOT] End of transmission");
            _isTransfer = false;
            _nextMsg = "";
        }
        else if (_nextMsg == "03")
        {
            _nextMsg = "04";
            _prefix = 1;
            _strIndex = 0;
            SendNetworkMessage(AstmHelper.ETX.ToString());
            DataSent?.Invoke("[ETX] End of text");
            _sendData = "";
        }
        else
        {
            // Send all frames
            string[] frames = _sendData.Split('\n');
            foreach (string frame in frames)
            {
                if (!string.IsNullOrEmpty(frame))
                {
                    _strIndex++;
                    SendNetworkMessage(frame + "\n");
                    DataSent?.Invoke($"[Frame {_strIndex}] {frame}");
                }
            }
            _nextMsg = "03";
        }
    }

    private void SendNetworkAck()
    {
        _networkHelper?.SendAck();
        DataSent?.Invoke("[ACK] Acknowledgment sent");
        _traceLogger.LogData("Host:", "ACK");
    }

    private void SendNetworkMessage(string msg)
    {
        _networkHelper?.SendString(msg);
        _traceLogger.LogData("Host:", msg);
    }

    #endregion

    #region Serial Mode

    private async Task StartSerialAsync(CancellationToken cancellationToken)
    {
        _serialHelper = new SerialHelper(_config.MachineName);
        _serialHelper.DataReceived += OnSerialDataReceived;
        _serialHelper.ErrorOccurred += err => StatusChanged?.Invoke($"Error: {err}");

        StatusChanged?.Invoke($"Opening {_config.SerialPort}...");
        
        if (!_serialHelper.Open(_config))
        {
            StatusChanged?.Invoke($"Failed to open {_config.SerialPort}");
            return;
        }

        StatusChanged?.Invoke($"Connected: {_config.SerialPort} @ {_config.SerialBaudrate} baud");
        IsRunning = true;

        // Keep running
        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            await Task.Delay(100, cancellationToken);
        }
    }

    private void OnSerialDataReceived(byte[] data)
    {
        try
        {
            string hexData = AstmHelper.ByteArrayToHexString(data).Trim();
            string asciiData = AstmHelper.HexStringToAscii(hexData.Replace(" ", ""));
            
            RawDataReceived?.Invoke($"[RX] {hexData}\n      {asciiData}");
            _traceLogger.LogData($"{_config.MachineName}:", asciiData);

            _messageBuffer = (_messageBuffer.Trim() + " " + hexData).Trim();

            // ENQ
            if (_messageBuffer == "05")
            {
                _messageData += " " + _messageBuffer;
                _messageBuffer = "";
                SendSerialAck();
                ConvertedDataReceived?.Invoke("[ENQ] Machine starting transmission");
            }
            // STX ... CR LF (data frame)
            else if (_messageBuffer.StartsWith("02") && _messageBuffer.EndsWith("0D 0A"))
            {
                _messageData += " " + _messageBuffer;
                _messageBuffer = "";
                SendSerialAck();
            }
            // EOT
            else if (_messageBuffer.StartsWith("04"))
            {
                _messageData += " " + _messageBuffer;
                _messageBuffer = "";
                ConvertedDataReceived?.Invoke("[EOT] Machine ended transmission");
            }
            // ACK received
            else if (_messageBuffer.Contains("06"))
            {
                _messageBuffer = "";
                _messageData = "";
            }

            _messageData = _messageData.Trim();

            // Handle transfer mode
            if (_isTransfer)
            {
                HandleSerialTransferResponse(hexData);
            }

            if (!_messageData.StartsWith("05"))
            {
                _messageData = "";
                _messageBuffer = "";
            }

            // Complete message
            if (_messageData.StartsWith("05") && _messageData.EndsWith("04"))
            {
                ProcessCompleteMessage(_messageData);
                _messageData = "";
                _messageBuffer = "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Serial data processing error", ex);
        }
    }

    private void HandleSerialTransferResponse(string hexData)
    {
        if (hexData != "06") return; // Not ACK

        string[] frames = _sendData.Split('\n');
        
        if (frames.Length - 1 == _strIndex)
        {
            // All frames sent, send EOT
            _serialHelper?.SendEot();
            DataSent?.Invoke("[EOT] End of transmission");
            _prefix = 1;
            _strIndex = 0;
            _isTransfer = false;
            _sendData = "";
        }
        else
        {
            // Send next frame
            string frameContent = frames[_strIndex];
            _serialHelper?.SendFrame(_prefix, frameContent);
            DataSent?.Invoke($"[Frame {_prefix}] {frameContent}");
            
            _prefix++;
            if (_prefix > 7) _prefix = 1;
            _strIndex++;
        }
    }

    private void SendSerialAck()
    {
        _serialHelper?.SendAck();
        DataSent?.Invoke("[ACK] Acknowledgment sent");
        _traceLogger.LogData("Host:", "ACK");
    }

    #endregion

    #region Message Processing

    private async void ProcessCompleteMessage(string data)
    {
        try
        {
            // Remove ENQ and EOT markers
            data = data.Replace("05", "").Replace("04", "").Trim();
            data = AstmHelper.HexStringToAscii(data.Replace(" ", ""));

            ConvertedDataReceived?.Invoke($"[MESSAGE]\n{data}");

            string[] records = data.Split('\r');
            string labNo = "";
            var readings = new List<MachineReading>();

            foreach (string record in records)
            {
                string cleanRecord = record.Trim();
                if (string.IsNullOrEmpty(cleanRecord)) continue;

                // Parse frames if present (remove frame number and checksum)
                if (cleanRecord.Length > 5 && char.IsDigit(cleanRecord[0]))
                {
                    cleanRecord = cleanRecord.Substring(1);
                    if (cleanRecord.Length > 3)
                        cleanRecord = cleanRecord.Substring(0, cleanRecord.Length - 3);
                }

                if (cleanRecord.StartsWith("H|"))
                {
                    ConvertedDataReceived?.Invoke($"[HEADER] {cleanRecord}");
                }
                else if (cleanRecord.StartsWith("P|"))
                {
                    ConvertedDataReceived?.Invoke($"[PATIENT] {cleanRecord}");
                }
                else if (cleanRecord.StartsWith("O|"))
                {
                    // Order record
                    string[] parts = cleanRecord.Split('|');
                    if (parts.Length > 2)
                    {
                        labNo = parts[2].Split('^')[0].Trim();
                        ConvertedDataReceived?.Invoke($"[ORDER] LabNo: {labNo}");
                    }
                }
                else if (cleanRecord.StartsWith("R|"))
                {
                    // Result record
                    string[] parts = cleanRecord.Split('|');
                    if (parts.Length > 3)
                    {
                        string[] paramParts = parts[2].Split('^');
                        string param = paramParts.Length > 3 ? paramParts[3].Trim() : 
                                       paramParts.Length > 0 ? paramParts[0].Trim() : "";
                        string reading = parts[3].Split('^')[0].Trim();

                        if (!string.IsNullOrEmpty(reading) && reading != "----" && param.Length < 15)
                        {
                            readings.Add(new MachineReading
                            {
                                LabNo = labNo,
                                MachineId = _config.MachineName,
                                MachineParam = $"{_config.MachineName}_{param}",
                                Reading = reading
                            });
                            ConvertedDataReceived?.Invoke($"[RESULT] {param} = {reading}");
                        }
                    }
                }
                else if (cleanRecord.StartsWith("Q|"))
                {
                    // Query record - machine requesting orders
                    string[] parts = cleanRecord.Split('|');
                    if (parts.Length > 2)
                    {
                        string[] queryParts = parts[2].Split('^');
                        string queryLabNo = queryParts.Length > 1 ? queryParts[1].Trim() : queryParts[0].Trim();
                        
                        if (!string.IsNullOrEmpty(queryLabNo))
                        {
                            ConvertedDataReceived?.Invoke($"[QUERY] Machine requesting orders for LabNo: {queryLabNo}");
                            await SendMachineOrdersAsync(queryLabNo);
                        }
                    }
                }
                else if (cleanRecord.StartsWith("L|"))
                {
                    ConvertedDataReceived?.Invoke($"[TERMINATOR] {cleanRecord}");
                }
            }

            // Insert readings to database
            if (readings.Count > 0)
            {
                ConvertedDataReceived?.Invoke($"[DB] Inserting {readings.Count} readings...");
                bool success = await _dbService.InsertMachineDataBatchAsync(readings);
                ConvertedDataReceived?.Invoke(success ? "[DB] Insert successful" : "[DB] Insert failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Message processing error", ex);
            ConvertedDataReceived?.Invoke($"[ERROR] {ex.Message}");
        }
    }

    private async Task SendMachineOrdersAsync(string labNo)
    {
        try
        {
            var orders = await _dbService.GetMachineOrdersAsync(labNo);
            
            StringBuilder sb = new StringBuilder();
            sb.Append($"H|\\^&||PSWD|{_config.MachineName} User|||||Lis||P|E1394-97{DateTime.Now:yyyyMMdd}\r\n");
            sb.Append("P|1\r\n");

            for (int i = 0; i < orders.Count; i++)
            {
                sb.Append($"O|{i + 1}|{labNo}||^^^{orders[i].AssayNo}|R\r\n");
            }

            sb.Append("L|1|N\r");
            _sendData = sb.ToString();

            DataSent?.Invoke($"[ORDERS] Sending {orders.Count} orders for LabNo: {labNo}");
            DataSent?.Invoke(_sendData);

            _prefix = 1;
            _strIndex = 0;
            _isTransfer = true;
            _nextMsg = "02";

            // Send ENQ to start transmission
            if (_config.Type.ToLower() == "network")
            {
                _networkHelper?.SendEnq();
            }
            else
            {
                _serialHelper?.SendEnq();
            }
            DataSent?.Invoke("[ENQ] Starting transmission");
        }
        catch (Exception ex)
        {
            _logger.LogError("Send orders error", ex);
            DataSent?.Invoke($"[ERROR] {ex.Message}");
        }
    }

    #endregion

    public void Dispose()
    {
        Stop();
        _serialHelper?.Dispose();
        _networkHelper?.Dispose();
    }
}

