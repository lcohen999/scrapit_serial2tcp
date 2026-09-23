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

                IOException io =>
                    $"The driver for {comPort} refused to open it ({io.Message.TrimEnd('.')}, code 0x{io.HResult:X8}). " +
                    "The device may be disconnected, powered off, or its driver may be in a bad state — " +
                    "try unplugging/replugging it or checking Device Manager for a warning icon.",

                ArgumentOutOfRangeException =>
                    $"{comPort} does not support the chosen settings (baud rate etc.): {ex.Message}",

                ArgumentException =>
                    $"\"{comPort}\" is not a valid serial port name.",

                _ => $"{comPort}: {ex.GetType().Name}: {ex.Message}"
            };
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
