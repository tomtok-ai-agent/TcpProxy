using System;
using System.Net;
using System.Text;
using System.Threading;

namespace TcpProxy
{
    static class Logger
    {
        // Current logging level (0=quiet, 1=standard, 2=verbose, 3=debug)
        public static int Level { get; set; } = 1;
        
        // Lock object for thread-safe console writing
        private static readonly object consoleLock = new object();

        // Log new connection event (level 1+)
        public static void LogConnection(EndPoint clientEndPoint, EndPoint targetEndPoint, long activeCount)
        {
            if (Level < 1) return;
            lock (consoleLock)
            {
                Console.WriteLine($"[+] New connection: {clientEndPoint} -> {targetEndPoint}. Active connections: {activeCount}");
            }
        }

        // Log disconnection event with reason (level 1+)
        public static void LogDisconnection(EndPoint clientEndPoint, EndPoint targetEndPoint, string reason, long activeCount)
        {
            if (Level < 1) return;
            lock (consoleLock)
            {
                Console.WriteLine($"[-] Connection closed: {clientEndPoint} -> {targetEndPoint}. Reason: {reason}. Active connections: {activeCount}");
            }
        }

        // Log data transfer (level 2 = bytes count, level 3 = also hex dump)
        public static void LogTransfer(string direction, int byteCount, byte[] buffer, int offset, int count)
        {
            if (Level < 2) return;
            lock (consoleLock)
            {
                Console.WriteLine($"{direction}: {byteCount} bytes");
                if (Level >= 3 && count > 0)
                {
                    // Print hex dump of the data for debug level
                    Console.WriteLine(HexDump(buffer, offset, count));
                }
            }
        }

        // Produce a hex dump string of a byte array (16 bytes per line, with ASCII)
        private static string HexDump(byte[] buffer, int offset, int count)
        {
            const int bytesPerLine = 16;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i += bytesPerLine)
            {
                // Address offset
                sb.AppendFormat("{0:X8}  ", i);
                int lineBytes = Math.Min(bytesPerLine, count - i);
                // Hex bytes
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (j < lineBytes)
                        sb.AppendFormat("{0:X2} ", buffer[offset + i + j]);
                    else
                        sb.Append("   ");  // padding for missing bytes
                    if (j == 7) sb.Append(" "); // extra space in middle
                }
                sb.Append(" ");
                // ASCII representation
                for (int j = 0; j < lineBytes; j++)
                {
                    byte b = buffer[offset + i + j];
                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                    sb.Append(c);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
