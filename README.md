# ScrapIt Serial-to-TCP Bridge

A simple Windows application that bridges COM/serial ports to TCP connections. Connect to a remote PC's serial port over the network via TCP.

## Features

- Map any COM port to a TCP port (default: 4001)
- Multiple simultaneous port mappings
- Bidirectional data streaming (serial <-> TCP)
- Multiple TCP clients per serial port
- Minimizes to system tray
- Auto-saves settings

## Usage

1. Select a COM port and baud rate
2. Set the TCP port (default 4001)
3. Click **Add** then **Start All**
4. From any remote PC: `telnet <this-pc-ip> 4001`

## Building

Requires .NET 8 SDK.

```bash
cd SerialToTcp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output will be in `SerialToTcp/bin/Release/net8.0-windows/win-x64/publish/`

## Installer

Install [Inno Setup](https://jrsoftware.org/isinfo.php), then compile `installer.iss`.
