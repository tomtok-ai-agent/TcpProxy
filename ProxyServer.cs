using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TcpProxy
{
    class ProxyServer
    {
        private readonly IPEndPoint listenEndPoint;
        private readonly string targetHost;
        private readonly int targetPort;
        private Socket listenerSocket;
        public static long activeConnections = 0;  // number of active proxy sessions

        public ProxyServer(IPEndPoint listenEndPoint, string targetHost, int targetPort)
        {
            this.listenEndPoint = listenEndPoint;
            this.targetHost = targetHost;
            this.targetPort = targetPort;
        }

        // Start listening for incoming connections (async loop)
        public async Task StartAsync()
        {
            // Initialize and bind the listening socket
            listenerSocket = new Socket(listenEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenerSocket.Bind(listenEndPoint);
            listenerSocket.Listen(100);  // backlog of 100

            Console.WriteLine($"Proxy listening on {listenEndPoint.Address}:{listenEndPoint.Port}, forwarding to {targetHost}:{targetPort}");

            // Accept incoming connections in a loop
            while (true)
            {
                // Wait for a client connection (async)
                Socket clientSocket = await listenerSocket.AcceptAsync();
                // Handle each client connection without blocking the accept loop
                _ = HandleClientAsync(clientSocket);
            }
        }

        // Handle a single client connection by connecting to target server and relaying data
        private async Task HandleClientAsync(Socket clientSocket)
        {
            // Prepare a socket for connecting to the target server
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            bool targetConnected = false;
            try
            {
                // Resolve target host to IP (if needed)
                IPAddress targetIP;
                if (!IPAddress.TryParse(targetHost, out targetIP))
                {
                    IPHostEntry hostEntry = await Dns.GetHostEntryAsync(targetHost);
                    if (hostEntry.AddressList.Length == 0)
                        throw new SocketException((int)SocketError.HostNotFound);
                    // Use the first resolved IPv4 or if none, the first address
                    targetIP = Array.Find(hostEntry.AddressList, ip => ip.AddressFamily == AddressFamily.InterNetwork) 
                               ?? hostEntry.AddressList[0];
                }
                // Connect to target server (async)
                await serverSocket.ConnectAsync(new IPEndPoint(targetIP, targetPort));
                targetConnected = true;
            }
            catch (Exception ex)
            {
                // Failed to connect to target
                clientSocket.Close();
                string errorReason;
                if (ex is SocketException sockEx)
                    errorReason = sockEx.SocketErrorCode.ToString();
                else
                    errorReason = ex.Message;
                // Log disconnection (target connection failure)
                Logger.LogDisconnection(clientSocket.RemoteEndPoint, new IPEndPoint(IPAddress.Parse(targetHost), targetPort), 
                                       $"Failed to connect to target: {errorReason}", activeConnections);
                return;
            }

            // Successfully connected to both client and target server
            long connectionId = Interlocked.Increment(ref activeConnections);
            
            // Log new connection
            Logger.LogConnection(clientSocket.RemoteEndPoint, serverSocket.RemoteEndPoint, connectionId);

            // Create a proxy connection to handle data transfer
            var proxyConnection = new ProxyConnection(clientSocket, serverSocket);
            
            // Start bidirectional data transfer
            await proxyConnection.StartAsync();
            
            // Connection closed, decrement active count
            Interlocked.Decrement(ref activeConnections);
        }
    }
}
