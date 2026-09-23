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

Only one copy runs per PC. Launching it again brings the existing window forward.

Settings are stored in `%AppData%\ScrapIt\SerialToTcp\settings.json`. Older versions saved next to the exe; that file is imported automatically.

## Troubleshooting

If a port fails to start, its Status column shows the reason. Hover over the row or double-click it for the full explanation, which is also written to the log.

| Status | Meaning |
|---|---|
| COM in use | Windows reported "Access to the port is denied". This means another program already has the port open, not a permissions problem. Close the other program (terminal software, vendor utility, another copy of this bridge in a different user session) or unplug and replug the USB adapter. |
| COM missing | The port doesn't exist right now. The adapter may be unplugged or renumbered (check Device Manager > Ports). |
| COM not found | Error 0x80070002. Windows lists the port, but no device answers on it. Usually a Bluetooth port with the device off, a leftover COM number from a USB adapter that was moved or removed, or virtual COM software that isn't running. Check Device Manager > View > Show hidden devices > Ports: greyed-out entries are leftovers. |
| TCP port in use | Another program is listening on that TCP port (`netstat -ano \| findstr :4001`). |
| TCP port reserved | Hyper-V/WSL/Docker reserved the port (`netsh int ipv4 show excludedportrange protocol=tcp`). |
| Faulted | The device failed after starting (usually unplugged). Click **Start All** to reconnect. |

**Security note:** the bridge listens on all network interfaces with no authentication. Anyone who can reach the TCP port can read from and write to the serial device, so limit access with Windows Firewall.

## Building

Requires .NET 8 SDK.

```bash
cd SerialToTcp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output will be in `SerialToTcp/bin/Release/net8.0-windows/win-x64/publish/`

## Installer

Install [Inno Setup](https://jrsoftware.org/isinfo.php), then compile `installer.iss`.
