using System.Text;

namespace MachineIntegration.Utilities;

/// <summary>
/// ASTM/LIS2-A2 Protocol Helper
/// Control characters used in medical device communication
/// </summary>
public static class AstmHelper
{
    // ASTM Control Characters
    public const char ENQ = '\x05';  // Enquiry - Request to send
    public const char STX = '\x02';  // Start of Text
    public const char ETX = '\x03';  // End of Text
    public const char EOT = '\x04';  // End of Transmission
    public const char ACK = '\x06';  // Acknowledgment
    public const char NAK = '\x15';  // Negative Acknowledgment
    public const char CR = '\r';     // Carriage Return
    public const char LF = '\n';     // Line Feed

    public static string ByteArrayToHexString(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    public static byte[] HexStringToByteArray(string hex)
    {
        hex = hex.Replace(" ", "");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    public static string HexStringToAscii(string hex)
    {
        hex = hex.Replace(" ", "");
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < hex.Length; i += 2)
        {
            string hexChar = hex.Substring(i, 2);
            int charCode = Convert.ToInt32(hexChar, 16);
            sb.Append((char)charCode);
        }
        return sb.ToString();
    }

    public static string ComputeChecksum(string frame)
    {
        int sum = 0;
        bool inFrame = false;
        
        foreach (char c in frame)
        {
            if (c == STX)
            {
                inFrame = true;
                continue;
            }
            if (inFrame)
            {
                sum += (int)c;
            }
        }
        
        return (sum % 256).ToString("X2");
    }

    public static string BuildFrame(int frameNumber, string content)
    {
        string frame = $"{STX}{frameNumber}{content}{ETX}";
        string checksum = ComputeChecksum(frame);
        return $"{frame}{checksum}{CR}{LF}";
    }

    public static bool IsEnq(string hexData) => hexData.Trim() == "05";
    public static bool IsStx(string hexData) => hexData.Trim() == "02";
    public static bool IsEtx(string hexData) => hexData.Trim() == "03";
    public static bool IsEot(string hexData) => hexData.Trim() == "04";
    public static bool IsAck(string hexData) => hexData.Trim() == "06";
    public static bool IsNak(string hexData) => hexData.Trim() == "15";

    public static byte[] GetAckBytes() => new byte[] { (byte)ACK };
    public static byte[] GetEnqBytes() => new byte[] { (byte)ENQ };
    public static byte[] GetEotBytes() => new byte[] { (byte)EOT };
    public static byte[] GetStxBytes() => new byte[] { (byte)STX };
    public static byte[] GetEtxBytes() => new byte[] { (byte)ETX };
}

