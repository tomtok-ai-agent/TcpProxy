using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TcpProxy
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Validate and parse command-line arguments
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: TcpProxy <listen_ip> <listen_port> <target_host> <target_port> <log_level>");
                return;
            }

            // Parse listening IP address
            IPAddress listenIP;
            if (!IPAddress.TryParse(args[0], out listenIP))
            {
                Console.WriteLine("Invalid listen IP address.");
                return;
            }
            // Parse listening port
            if (!int.TryParse(args[1], out int listenPort) || listenPort < 1 || listenPort > 65535)
            {
                Console.WriteLine("Invalid listen port.");
                return;
            }
            // Target host (can be domain name or IP)
            string targetHost = args[2];
            // Target port
            if (!int.TryParse(args[3], out int targetPort) || targetPort < 1 || targetPort > 65535)
            {
                Console.WriteLine("Invalid target port.");
                return;
            }
            // Logging level
            if (!int.TryParse(args[4], out int logLevel) || logLevel < 0 || logLevel > 3)
            {
                Console.WriteLine("Invalid log level (0-3).");
                return;
            }

            // Initialize logging level
            Logger.Level = logLevel;

            // Create and start the proxy server
            ProxyServer server = new ProxyServer(new IPEndPoint(listenIP, listenPort), targetHost, targetPort);
            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Proxy server error: {ex.Message}");
            }
        }
    }
}
