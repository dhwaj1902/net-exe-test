using System.Net;
using System.Net.Sockets;
using System.Text;
using MachineIntegration.Models;

namespace MachineIntegration.Utilities;

/// <summary>
/// TCP Network utility helper for machine communication
/// </summary>
public class NetworkHelper : IDisposable
{
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly Logger _logger;
    private readonly ASCIIEncoding _encoder = new();
    
    public event Action<byte[]>? DataReceived;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? ClientConnected;
    public event Action? ClientDisconnected;
    
    public bool IsConnected => _client?.Connected ?? false;
    public bool IsListening => _listener != null;
    public string RemoteEndPoint => _client?.Client?.RemoteEndPoint?.ToString() ?? "";

    public NetworkHelper(string logPrefix = "Network")
    {
        _logger = new Logger(logPrefix);
    }

    /// <summary>
    /// Start TCP server (listen for connections)
    /// </summary>
    public async Task<bool> StartServerAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            
            _logger.Log($"Server started on port {port}");
            
            // Accept clients in background
            _ = AcceptClientsAsync(_cts.Token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to start server", ex);
            ErrorOccurred?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Connect to TCP server (client mode)
    /// </summary>
    public async Task<bool> ConnectAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port, cancellationToken);
            _stream = _client.GetStream();
            
            _logger.Log($"Connected to {ip}:{port}");
            ClientConnected?.Invoke($"{ip}:{port}");
            
            // Start reading in background
            _ = ReadDataAsync(_cts.Token);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to connect", ex);
            ErrorOccurred?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Stop server and close all connections
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        _listener?.Stop();
        
        _stream = null;
        _client = null;
        _listener = null;
        
        _logger.Log("Connection closed");
    }

    /// <summary>
    /// Send raw bytes
    /// </summary>
    public bool SendBytes(byte[] data)
    {
        if (_stream == null) return false;
        
        try
        {
            _stream.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Send failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Send ASCII string
    /// </summary>
    public bool SendString(string data)
    {
        return SendBytes(_encoder.GetBytes(data));
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
    /// Send STX (0x02)
    /// </summary>
    public bool SendStx() => SendBytes(AstmHelper.GetStxBytes());

    /// <summary>
    /// Send ETX (0x03)
    /// </summary>
    public bool SendEtx() => SendBytes(AstmHelper.GetEtxBytes());

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _stream = _client.GetStream();
                
                string endpoint = _client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                _logger.Log($"Client connected: {endpoint}");
                ClientConnected?.Invoke(endpoint);
                
                await ReadDataAsync(cancellationToken);
                
                ClientDisconnected?.Invoke();
                _logger.Log("Client disconnected");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Accept error", ex);
            }
        }
    }

    private async Task ReadDataAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        
        while (!cancellationToken.IsCancellationRequested && _stream != null && _client?.Connected == true)
        {
            try
            {
                int bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                
                if (bytesRead == 0) break;
                
                byte[] data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                DataReceived?.Invoke(data);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Read error", ex);
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

