using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpProxy
{
    class ProxyConnection
    {
        private readonly Socket clientSocket;
        private readonly Socket serverSocket;
        private readonly EndPoint clientEndPoint;
        private readonly EndPoint serverEndPoint;
        private int isClosed = 0;  // atomic flag to prevent double-closing

        public ProxyConnection(Socket clientSocket, Socket serverSocket)
        {
            this.clientSocket = clientSocket;
            this.serverSocket = serverSocket;
            this.clientEndPoint = clientSocket.RemoteEndPoint;
            this.serverEndPoint = serverSocket.RemoteEndPoint;
        }

        // Start bidirectional data transfer
        public async Task StartAsync()
        {
            // Create two tasks for bidirectional data transfer
            Task clientToServerTask = ForwardDataAsync(clientSocket, serverSocket, "C->S");
            Task serverToClientTask = ForwardDataAsync(serverSocket, clientSocket, "S->C");

            // Wait for either direction to complete (or fail)
            await Task.WhenAny(clientToServerTask, serverToClientTask);

            // Close the connection (if not already closed)
            CloseConnection("Connection closed by peer");
        }

        // Forward data from source socket to destination socket
        private async Task ForwardDataAsync(Socket source, Socket destination, string direction)
        {
            byte[] buffer = new byte[8192];  // 8KB buffer
            try
            {
                while (true)
                {
                    // Read data from source
                    int bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        // Connection closed gracefully
                        CloseConnection($"{(direction == "C->S" ? "Client" : "Server")} disconnected");
                        break;
                    }

                    // Log the data transfer
                    Logger.LogTransfer(direction, bytesRead, buffer, 0, bytesRead);

                    // Send data to destination
                    await destination.SendAsync(buffer.AsMemory(0, bytesRead), SocketFlags.None);
                }
            }
            catch (Exception ex)
            {
                // Connection error
                string errorReason;
                if (ex is SocketException sockEx)
                    errorReason = sockEx.SocketErrorCode.ToString();
                else
                    errorReason = ex.Message;

                CloseConnection($"Socket error: {errorReason}");
            }
        }

        // Close both sockets and log the disconnection (thread-safe)
        private void CloseConnection(string reason)
        {
            // Only the first thread to call this will proceed
            if (Interlocked.Exchange(ref isClosed, 1) == 0)
            {
                try
                {
                    // Shutdown both sockets if connected
                    if (clientSocket.Connected)
                    {
                        clientSocket.Shutdown(SocketShutdown.Both);
                        clientSocket.Close();
                    }
                }
                catch { /* Ignore errors during shutdown */ }

                try
                {
                    if (serverSocket.Connected)
                    {
                        serverSocket.Shutdown(SocketShutdown.Both);
                        serverSocket.Close();
                    }
                }
                catch { /* Ignore errors during shutdown */ }

                // Log disconnection with the reason
                Logger.LogDisconnection(clientEndPoint, serverEndPoint, reason, 
                                       Interlocked.Read(ref ProxyServer.activeConnections));
            }
        }
    }
}
