using System.IO.Ports;
using System.Text;
using MachineIntegration.Models;

namespace MachineIntegration.Utilities;

/// <summary>
/// Serial port utility helper for machine communication
/// </summary>
public class SerialHelper : IDisposable
{
    private SerialPort? _port;
    private readonly Logger _logger;
    
    public event Action<byte[]>? DataReceived;
    public event Action<string>? ErrorOccurred;
    
    public bool IsOpen => _port?.IsOpen ?? false;
    public string PortName => _port?.PortName ?? "";

    public SerialHelper(string logPrefix = "Serial")
    {
        _logger = new Logger(logPrefix);
    }

    /// <summary>
    /// Open serial port with configuration
    /// </summary>
    public bool Open(MachineConfig config)
    {
        try
        {
            _port = new SerialPort
            {
                PortName = config.SerialPort,
                BaudRate = config.SerialBaudrate,
                Parity = GetParity(config.SerialParity),
                DataBits = config.SerialDataBits,
                StopBits = GetStopBits(config.SerialStopBits),
                ReadTimeout = 500,
                WriteTimeout = 500,
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.DataReceived += OnDataReceived;
            _port.ErrorReceived += OnErrorReceived;
            _port.Open();
            
            _logger.Log($"Port opened: {config.SerialPort} @ {config.SerialBaudrate} baud");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to open port", ex);
            ErrorOccurred?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Close the serial port
    /// </summary>
    public void Close()
    {
        if (_port?.IsOpen == true)
        {
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
            _port.Close();
            _port.Dispose();
            _logger.Log("Port closed");
        }
        _port = null;
    }

    /// <summary>
    /// Send raw bytes
    /// </summary>
    public bool SendBytes(byte[] data)
    {
        if (_port?.IsOpen != true) return false;
        
        try
        {
            _port.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Send failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Send hex string (e.g., "05" for ENQ)
    /// </summary>
    public bool SendHex(string hex)
    {
        return SendBytes(AstmHelper.HexStringToByteArray(hex));
    }

    /// <summary>
    /// Send ASCII string
    /// </summary>
    public bool SendString(string data)
    {
        return SendBytes(Encoding.ASCII.GetBytes(data));
    }

    /// <summary>
    /// Send ACK (0x06)
    /// </summary>
    public bool SendAck() => SendBytes(AstmHelper.GetAckBytes());

    /// <summary>
    /// Send ENQ (0x05)
    /// </summary>
    public bool SendEnq() => SendBytes(AstmHelper.GetEnqBytes());

    /// <summary>
    /// Send EOT (0x04)
    /// </summary>
    public bool SendEot() => SendBytes(AstmHelper.GetEotBytes());

    /// <summary>
    /// Send framed message with STX, frame number, ETX, and checksum
    /// </summary>
    public bool SendFrame(int frameNumber, string content)
    {
        string frame = AstmHelper.BuildFrame(frameNumber, content);
        return SendString(frame);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port?.IsOpen != true) return;
        
        try
        {
            int bytesToRead = _port.BytesToRead;
            byte[] buffer = new byte[bytesToRead];
            _port.Read(buffer, 0, bytesToRead);
            DataReceived?.Invoke(buffer);
        }
        catch (Exception ex)
        {
            _logger.LogError("Read error", ex);
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        string error = $"Serial error: {e.EventType}";
        _logger.LogError(error);
        ErrorOccurred?.Invoke(error);
    }

    private static Parity GetParity(string parity) => parity.ToLower() switch
    {
        "none" => Parity.None,
        "odd" => Parity.Odd,
        "even" => Parity.Even,
        "mark" => Parity.Mark,
        "space" => Parity.Space,
        _ => Parity.None
    };

    private static StopBits GetStopBits(int stopBits) => stopBits switch
    {
        0 => StopBits.None,
        1 => StopBits.One,
        2 => StopBits.Two,
        _ => StopBits.One
    };

    public void Dispose()
    {
        Close();
    }

    /// <summary>
    /// Get available serial ports
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }
}

