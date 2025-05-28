# TCP Proxy

A command-line TCP proxy application written in C# that forwards TCP traffic between a local endpoint and a remote endpoint.

## Features

- Forwards TCP traffic between a local endpoint and a remote endpoint
- Supports multiple concurrent sessions
- Handles connection instability (disconnections, reconnections)
- Detailed logging with configurable verbosity levels
- Cross-platform (runs on Windows, Linux, macOS)

## Requirements

- .NET 9.0 SDK or Runtime

## Usage

```
TcpProxy <local_ip> <local_port> <remote_ip_or_host> <remote_port> [verbosity_level]
```

### Parameters

- `local_ip` - IP address to listen on (use 0.0.0.0 for all interfaces)
- `local_port` - Port to listen on
- `remote_ip_or_host` - Remote IP address or hostname to forward traffic to
- `remote_port` - Remote port to forward traffic to
- `verbosity_level` - (Optional) Logging verbosity level:
  - 0 = Quiet (no output)
  - 1 = Standard (connection events, default)
  - 2 = Verbose (data transfer statistics)
  - 3 = Debug (hex dumps of data)

### Examples

Listen on all interfaces on port 8080 and forward to example.com:80:
```
dotnet run -- 0.0.0.0 8080 example.com 80
```

Listen on localhost port 3306 and forward to a remote MySQL server with verbose logging:
```
dotnet run -- 127.0.0.1 3306 db.example.com 3306 2
```

## Building from Source

1. Clone the repository
2. Navigate to the project directory
3. Build the project:
   ```
   dotnet build
   ```
4. Run the application:
   ```
   dotnet run -- <arguments>
   ```

## License

This project is open source and available under the MIT License.
