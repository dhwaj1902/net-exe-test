using System.Text.Json.Serialization;

namespace MachineIntegration.Models;

public class MachineConfig
{
    [JsonPropertyName("user")]
    public string User { get; set; } = "root";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("database")]
    public string Database { get; set; } = "flabs_db";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("db_port")]
    public int DbPort { get; set; } = 3306;

    [JsonPropertyName("machine_name")]
    public string MachineName { get; set; } = "";

    [JsonPropertyName("run_port")]
    public int RunPort { get; set; } = 5100;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "serial"; // "serial" or "network"

    [JsonPropertyName("network_type")]
    public string NetworkType { get; set; } = "server"; // "server" or "client"

    [JsonPropertyName("network_ip")]
    public string NetworkIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("network_port")]
    public int NetworkPort { get; set; } = 5200;

    [JsonPropertyName("network_ack")]
    public bool NetworkAck { get; set; } = false;

    [JsonPropertyName("serial_port")]
    public string SerialPort { get; set; } = "COM1";

    [JsonPropertyName("serial_baudrate")]
    public int SerialBaudrate { get; set; } = 9600;

    [JsonPropertyName("serial_parity")]
    public string SerialParity { get; set; } = "none";

    [JsonPropertyName("serial_data_bits")]
    public int SerialDataBits { get; set; } = 8;

    [JsonPropertyName("serial_stop_bits")]
    public int SerialStopBits { get; set; } = 1;

    public string GetConnectionString()
    {
        // Added connection timeout, pooling, and other connection options
        return $"Server={Host};Port={DbPort};Database={Database};User={User};Password={Password};" +
               "Connection Timeout=10;Default Command Timeout=30;Pooling=true;Min Pool Size=1;Max Pool Size=20;" +
               "AllowUserVariables=true;UseCompression=false;";
    }
}
