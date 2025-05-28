using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpProxy
{
    /// <summary>
    /// TCP Proxy application that forwards TCP traffic between a local endpoint and a remote endpoint.
    /// Supports multiple concurrent sessions and handles connection instability.
    /// </summary>
    class Program
    {
        // Concurrent dictionary to track active sessions
        private static readonly ConcurrentDictionary<string, ProxySession> ActiveSessions = new();
        
        // Verbosity level for logging
        private static VerbosityLevel _verbosityLevel = VerbosityLevel.Standard;
        
        // Cancellation token source for graceful shutdown
        private static readonly CancellationTokenSource CancellationSource = new();

        static async Task Main(string[] args)
        {
            try
            {
                // Parse command line arguments
                if (!ParseArguments(args, out IPEndPoint localEndpoint, out IPEndPoint remoteEndpoint))
                {
                    return;
                }

                // Register console cancellation (Ctrl+C)
                Console.CancelKeyPress += (_, e) =>
                {
                    Log(VerbosityLevel.Standard, "Shutting down proxy server...");
                    CancellationSource.Cancel();
                    e.Cancel = true;
                };

                // Start the proxy server
                await RunProxyServer(localEndpoint, remoteEndpoint, CancellationSource.Token);
            }
            catch (Exception ex)
            {
                Log(VerbosityLevel.Standard, $"Fatal error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Parses command line arguments to extract local and remote endpoints and verbosity level.
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <param name="localEndpoint">Output parameter for local endpoint to listen on</param>
        /// <param name="remoteEndpoint">Output parameter for remote endpoint to forward to</param>
        /// <returns>True if arguments were parsed successfully, false otherwise</returns>
        private static bool ParseArguments(string[] args, out IPEndPoint localEndpoint, out IPEndPoint remoteEndpoint)
        {
            localEndpoint = null;
            remoteEndpoint = null;

            if (args.Length < 4)
            {
                PrintUsage();
                return false;
            }

            try
            {
                // Parse local endpoint
                if (!TryParseEndpoint(args[0], args[1], out localEndpoint))
                {
                    Log(VerbosityLevel.Standard, "Invalid local endpoint format.");
                    PrintUsage();
                    return false;
                }

                // Parse remote endpoint
                if (!TryParseEndpoint(args[2], args[3], out remoteEndpoint))
                {
                    Log(VerbosityLevel.Standard, "Invalid remote endpoint format.");
                    PrintUsage();
                    return false;
                }

                // Parse verbosity level (optional)
                if (args.Length > 4 && int.TryParse(args[4], out int verbosity))
                {
                    _verbosityLevel = (VerbosityLevel)verbosity;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log(VerbosityLevel.Standard, $"Error parsing arguments: {ex.Message}");
                PrintUsage();
                return false;
            }
        }

        /// <summary>
        /// Attempts to parse an IP address/hostname and port into an IPEndPoint.
        /// </summary>
        /// <param name="ipOrHost">IP address or hostname</param>
        /// <param name="portStr">Port number as string</param>
        /// <param name="endpoint">Output IPEndPoint if successful</param>
        /// <returns>True if parsing was successful, false otherwise</returns>
        private static bool TryParseEndpoint(string ipOrHost, string portStr, out IPEndPoint endpoint)
        {
            endpoint = null;

            if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
            {
                return false;
            }

            try
            {
                // Try to parse as IP address first
                if (IPAddress.TryParse(ipOrHost, out IPAddress ipAddress))
                {
                    endpoint = new IPEndPoint(ipAddress, port);
                    return true;
                }

                // If not an IP, try to resolve as hostname
                IPHostEntry hostEntry = Dns.GetHostEntry(ipOrHost);
                if (hostEntry.AddressList.Length > 0)
                {
                    endpoint = new IPEndPoint(hostEntry.AddressList[0], port);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Prints usage information to the console.
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("Usage: TcpProxy <local_ip> <local_port> <remote_ip_or_host> <remote_port> [verbosity_level]");
            Console.WriteLine("  local_ip        - IP address to listen on (use 0.0.0.0 for all interfaces)");
            Console.WriteLine("  local_port      - Port to listen on");
            Console.WriteLine("  remote_ip_or_host - Remote IP address or hostname to forward traffic to");
            Console.WriteLine("  remote_port     - Remote port to forward traffic to");
            Console.WriteLine("  verbosity_level - (Optional) Logging verbosity level:");
            Console.WriteLine("                    0 = Quiet (no output)");
            Console.WriteLine("                    1 = Standard (connection events, default)");
            Console.WriteLine("                    2 = Verbose (data transfer statistics)");
            Console.WriteLine("                    3 = Debug (hex dumps of data)");
        }

        /// <summary>
        /// Runs the TCP proxy server, listening for incoming connections and forwarding them.
        /// </summary>
        /// <param name="localEndpoint">Local endpoint to listen on</param>
        /// <param name="remoteEndpoint">Remote endpoint to forward to</param>
        /// <param name="cancellationToken">Cancellation token for shutdown</param>
        private static async Task RunProxyServer(IPEndPoint localEndpoint, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
        {
            // Create and configure the TCP listener
            TcpListener listener = new TcpListener(localEndpoint);
            listener.Start();

            Log(VerbosityLevel.Standard, $"TCP Proxy started. Listening on {localEndpoint}, forwarding to {remoteEndpoint}");
            Log(VerbosityLevel.Standard, $"Verbosity level: {_verbosityLevel}");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Wait for a client connection with cancellation support
                    TcpClient clientConnection;
                    try
                    {
                        // Use AcceptTcpClientAsync with cancellation token
                        var acceptTask = listener.AcceptTcpClientAsync();
                        var completedTask = await Task.WhenAny(acceptTask, Task.Delay(-1, cancellationToken));
                        
                        if (completedTask == acceptTask)
                        {
                            clientConnection = await acceptTask;
                        }
                        else
                        {
                            // Cancellation was requested
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log(VerbosityLevel.Standard, $"Error accepting client connection: {ex.Message}");
                        continue;
                    }

                    // Handle the client connection in a separate task
                    _ = HandleClientConnectionAsync(clientConnection, remoteEndpoint, cancellationToken);
                }
            }
            finally
            {
                // Clean up resources
                listener.Stop();
                
                // Close all active sessions
                foreach (var session in ActiveSessions.Values)
                {
                    session.Close();
                }
                
                Log(VerbosityLevel.Standard, "TCP Proxy stopped.");
            }
        }

        /// <summary>
        /// Handles a client connection by establishing a connection to the remote endpoint and forwarding data.
        /// </summary>
        /// <param name="clientConnection">The client TCP connection</param>
        /// <param name="remoteEndpoint">The remote endpoint to connect to</param>
        /// <param name="cancellationToken">Cancellation token for shutdown</param>
        private static async Task HandleClientConnectionAsync(TcpClient clientConnection, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
        {
            string sessionId = Guid.NewGuid().ToString("N");
            var clientEndpoint = (IPEndPoint)clientConnection.Client.RemoteEndPoint;
            
            var session = new ProxySession(sessionId, clientConnection, clientEndpoint, remoteEndpoint);
            ActiveSessions.TryAdd(sessionId, session);
            
            Log(VerbosityLevel.Standard, $"New connection from {clientEndpoint}. Session ID: {sessionId}. Active sessions: {ActiveSessions.Count}");

            try
            {
                // Connect to the remote endpoint
                TcpClient remoteConnection = new TcpClient();
                try
                {
                    await remoteConnection.ConnectAsync(remoteEndpoint.Address, remoteEndpoint.Port, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log(VerbosityLevel.Standard, $"Failed to connect to remote endpoint {remoteEndpoint}: {ex.Message}. Session ID: {sessionId}");
                    clientConnection.Close();
                    ActiveSessions.TryRemove(sessionId, out _);
                    Log(VerbosityLevel.Standard, $"Connection closed. Session ID: {sessionId}. Active sessions: {ActiveSessions.Count}. Reason: Remote connection failed");
                    return;
                }

                session.SetRemoteConnection(remoteConnection);

                // Create tasks for forwarding data in both directions
                var clientToRemoteTask = ForwardDataAsync(
                    clientConnection, 
                    remoteConnection, 
                    "client → remote", 
                    sessionId, 
                    cancellationToken);
                
                var remoteToClientTask = ForwardDataAsync(
                    remoteConnection, 
                    clientConnection, 
                    "remote → client", 
                    sessionId, 
                    cancellationToken);

                // Wait for either direction to complete (or error)
                await Task.WhenAny(clientToRemoteTask, remoteToClientTask);

                // Close both connections
                clientConnection.Close();
                remoteConnection.Close();
            }
            catch (Exception ex)
            {
                Log(VerbosityLevel.Standard, $"Error in session {sessionId}: {ex.Message}");
            }
            finally
            {
                // Clean up the session
                session.Close();
                ActiveSessions.TryRemove(sessionId, out _);
                Log(VerbosityLevel.Standard, $"Connection closed. Session ID: {sessionId}. Active sessions: {ActiveSessions.Count}. Reason: Session completed");
            }
        }

        /// <summary>
        /// Forwards data from one TCP connection to another.
        /// </summary>
        /// <param name="source">Source TCP connection</param>
        /// <param name="destination">Destination TCP connection</param>
        /// <param name="direction">Direction label for logging</param>
        /// <param name="sessionId">Session identifier for logging</param>
        /// <param name="cancellationToken">Cancellation token for shutdown</param>
        private static async Task ForwardDataAsync(TcpClient source, TcpClient destination, string direction, string sessionId, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            long totalBytes = 0;
            
            try
            {
                NetworkStream sourceStream = source.GetStream();
                NetworkStream destinationStream = destination.GetStream();

                while (!cancellationToken.IsCancellationRequested && source.Connected && destination.Connected)
                {
                    int bytesRead;
                    
                    try
                    {
                        // Read with cancellation support
                        bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log(VerbosityLevel.Standard, $"Error reading from {direction}: {ex.Message}. Session ID: {sessionId}");
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // End of stream
                        break;
                    }

                    try
                    {
                        // Write with cancellation support
                        await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        await destinationStream.FlushAsync(cancellationToken);
                        
                        totalBytes += bytesRead;
                        
                        // Log data transfer statistics
                        if (_verbosityLevel >= VerbosityLevel.Verbose)
                        {
                            Log(VerbosityLevel.Verbose, $"Transferred {bytesRead} bytes ({totalBytes} total) {direction}. Session ID: {sessionId}");
                        }
                        
                        // Log hex dump of data
                        if (_verbosityLevel >= VerbosityLevel.Debug)
                        {
                            Log(VerbosityLevel.Debug, $"Data {direction} (hex): {BitConverter.ToString(buffer, 0, bytesRead)}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log(VerbosityLevel.Standard, $"Error writing to {direction}: {ex.Message}. Session ID: {sessionId}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(VerbosityLevel.Standard, $"Error in {direction}: {ex.Message}. Session ID: {sessionId}");
            }
            
            Log(VerbosityLevel.Verbose, $"Data forwarding stopped for {direction}. Total bytes: {totalBytes}. Session ID: {sessionId}");
        }

        /// <summary>
        /// Logs a message based on the current verbosity level.
        /// </summary>
        /// <param name="level">Minimum verbosity level required to display this message</param>
        /// <param name="message">Message to log</param>
        private static void Log(VerbosityLevel level, string message)
        {
            if (_verbosityLevel >= level)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            }
        }
    }

    /// <summary>
    /// Represents a proxy session between a client and a remote server.
    /// </summary>
    class ProxySession
    {
        public string Id { get; }
        public TcpClient ClientConnection { get; }
        public TcpClient RemoteConnection { get; private set; }
        public IPEndPoint ClientEndpoint { get; }
        public IPEndPoint RemoteEndpoint { get; }
        public DateTime StartTime { get; }

        public ProxySession(string id, TcpClient clientConnection, IPEndPoint clientEndpoint, IPEndPoint remoteEndpoint)
        {
            Id = id;
            ClientConnection = clientConnection;
            ClientEndpoint = clientEndpoint;
            RemoteEndpoint = remoteEndpoint;
            StartTime = DateTime.Now;
        }

        public void SetRemoteConnection(TcpClient remoteConnection)
        {
            RemoteConnection = remoteConnection;
        }

        public void Close()
        {
            try
            {
                ClientConnection?.Close();
            }
            catch { /* Ignore errors during cleanup */ }

            try
            {
                RemoteConnection?.Close();
            }
            catch { /* Ignore errors during cleanup */ }
        }
    }

    /// <summary>
    /// Defines verbosity levels for logging.
    /// </summary>
    enum VerbosityLevel
    {
        Quiet = 0,
        Standard = 1,
        Verbose = 2,
        Debug = 3
    }
}
