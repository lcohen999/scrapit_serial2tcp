using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;

namespace SerialToTcp
{
    /// <summary>
    /// Turns the terse exceptions from SerialPort/TcpListener into something a person can act on.
    /// </summary>
    public static class PortErrors
    {
        public static string DescribeSerialOpenError(Exception ex, string comPort)
        {
            bool present = SerialPort.GetPortNames().Contains(comPort, StringComparer.OrdinalIgnoreCase);

            return ex switch
            {
                // "Access to the port 'COM2' is denied." — Windows returns ERROR_ACCESS_DENIED when
                // another handle to the port is already open. It is almost never an actual permissions problem.
                UnauthorizedAccessException =>
                    $"{comPort} is already open in another program. Windows only lets one program use a COM port at a time. " +
                    "Common culprits: another copy of this bridge (check other user sessions), a terminal program " +
                    "(PuTTY, Tera Term, Arduino IDE), vendor/POS/scale software, or a Windows service. " +
                    "To find it: Resource Monitor > CPU > Associated Handles, search for \"Serial\" or \"VCP\". " +
                    "For USB adapters, unplugging and replugging also releases a stuck port.",

                IOException when !present =>
                    $"{comPort} does not exist on this PC right now. If it is a USB adapter it may be unplugged or " +
                    "Windows may have given it a different COM number (check Device Manager > Ports). " +
                    "If it is Bluetooth, the device may be off or unpaired.",

                IOException io => DescribeDriverError(io, comPort),

                ArgumentOutOfRangeException =>
                    $"{comPort} does not support the chosen settings (baud rate etc.): {ex.Message}",

                ArgumentException =>
                    $"\"{comPort}\" is not a valid serial port name.",

                _ => $"{comPort}: {ex.GetType().Name}: {ex.Message}"
            };
        }

        // Windows error code inside an HRESULT like 0x80070002, or -1 if it isn't one.
        private static int Win32Code(Exception ex) =>
            (ex.HResult & 0xFFFF0000) == 0x80070000 ? ex.HResult & 0xFFFF : -1;

        private static string DescribeDriverError(IOException io, string comPort)
        {
            string code = $"(Windows error 0x{io.HResult:X8}: {io.Message.TrimEnd('.')})";
            switch (Win32Code(io))
            {
                case 2: // ERROR_FILE_NOT_FOUND
                case 3: // ERROR_PATH_NOT_FOUND
                    string device = PortHolderFinder.QueryDosDevice(comPort) is string d
                        ? $"Windows maps {comPort} to {d}, but that device isn't answering"
                        : $"there is no device behind the name {comPort}";
                    return $"{comPort} is listed in the registry, but {device} {code}. It's a leftover or dead port name, " +
                           "not a program holding the port. Usual causes: a Bluetooth serial port whose device is off or out of range; " +
                           "a USB adapter that was unplugged or moved to a different USB socket (Windows kept the old COM number); " +
                           "virtual COM software (com0com, modem/VPN or vendor tools) that isn't running; or a built-in port " +
                           "turned off in the BIOS. Check Device Manager > View > Show hidden devices > Ports (COM & LPT): " +
                           "a greyed-out entry is a leftover. Plug the device in, or uninstall the leftover and use the port the device has now.";
                case 31: // ERROR_GEN_FAILURE
                    return $"{comPort}'s driver reports the device isn't working {code}. With USB adapters this is very often " +
                           "a counterfeit Prolific PL2303 chip rejected by the current driver (Device Manager shows code 10), " +
                           "or the device has no power. Check Device Manager > Ports for a warning icon.";
                case 121:  // ERROR_SEM_TIMEOUT
                case 1167: // ERROR_DEVICE_NOT_CONNECTED
                    return $"{comPort} timed out or reports no device connected {code}. For Bluetooth ports, turn the device on " +
                           "and make sure it's paired and in range; for USB adapters, replug it.";
                default:
                    return $"The driver for {comPort} refused to open it {code}. The device may be disconnected, powered off, " +
                           "or its driver may be in a bad state. Try replugging it or check Device Manager for a warning icon.";
            }
        }

        public static string DescribeTcpListenError(Exception ex, int tcpPort)
        {
            if (ex is SocketException se)
            {
                switch (se.SocketErrorCode)
                {
                    case SocketError.AddressAlreadyInUse:
                        return $"TCP port {tcpPort} is already being used by another program. Pick a different TCP port, " +
                               $"or find the owner with: netstat -ano | findstr :{tcpPort}";
                    case SocketError.AccessDenied:
                        return $"Windows denied access to TCP port {tcpPort}. The port is probably reserved " +
                               "(Hyper-V, WSL and Docker reserve port ranges). Check with: " +
                               "netsh int ipv4 show excludedportrange protocol=tcp — then pick a port outside those ranges.";
                }
                return $"Could not listen on TCP port {tcpPort}: {se.Message} (socket error {se.SocketErrorCode}).";
            }
            return $"Could not listen on TCP port {tcpPort}: {ex.Message}";
        }

        /// <summary>A few words for the Status column; the full text goes in the log and tooltip.</summary>
        public static string ShortSerialReason(Exception ex, string comPort) => ex switch
        {
            UnauthorizedAccessException => "COM in use",
            IOException when !SerialPort.GetPortNames().Contains(comPort, StringComparer.OrdinalIgnoreCase) => "COM missing",
            IOException io when Win32Code(io) is 2 or 3 => "COM not found",
            IOException io when Win32Code(io) is 121 or 1167 => "COM not connected",
            _ => "COM error"
        };

        public static string ShortTcpReason(Exception ex) => ex switch
        {
            SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } => "TCP port in use",
            SocketException { SocketErrorCode: SocketError.AccessDenied } => "TCP port reserved",
            _ => "TCP error"
        };
    }
}
