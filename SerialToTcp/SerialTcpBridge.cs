using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SerialToTcp
{
    /// <summary>Thrown by <see cref="SerialTcpBridge.Start"/> with a user-facing explanation.</summary>
    public class BridgeStartException : Exception
    {
        public string ShortReason { get; }

        public BridgeStartException(string shortReason, string message, Exception inner) : base(message, inner)
        {
            ShortReason = shortReason;
        }
    }

    public class SerialTcpBridge : IDisposable
    {
        // A client that stops reading for this long is dropped, so it can't stall the serial port for everyone else.
        private const int ClientSendTimeoutMs = 5000;

        private SerialPort? _serialPort;
        private TcpListener? _tcpListener;
        private readonly List<TcpClient> _clients = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private volatile bool _running;
        private volatile string? _fault;

        public string ComPort { get; }
        public int BaudRate { get; }
        public int TcpPort { get; }
        public bool IsRunning => _running;

        /// <summary>Set when the serial device fails after a successful start (e.g. USB adapter unplugged).</summary>
        public string? Fault => _fault;

        public event Action<string>? OnLog;

        public SerialTcpBridge(string comPort, int baudRate, int tcpPort)
        {
            ComPort = comPort;
            BaudRate = baudRate;
            TcpPort = tcpPort;
        }

        public void Start()
        {
            if (_running) return;
            _fault = null;

            var serial = new SerialPort(ComPort, BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            try
            {
                serial.Open();
            }
            catch (Exception ex)
            {
                serial.Dispose();
                throw new BridgeStartException(PortErrors.ShortSerialReason(ex, ComPort),
                    PortErrors.DescribeSerialOpenError(ex, ComPort), ex);
            }

            var listener = new TcpListener(IPAddress.Any, TcpPort);
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                // Release the COM port too, otherwise it stays locked and the next attempt reports "access denied".
                try { serial.Close(); } catch { }
                serial.Dispose();
                throw new BridgeStartException(PortErrors.ShortTcpReason(ex),
                    PortErrors.DescribeTcpListenError(ex, TcpPort), ex);
            }

            _serialPort = serial;
            _tcpListener = listener;
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.ErrorReceived += SerialPort_ErrorReceived;
            _cts = new CancellationTokenSource();
            _running = true;

            var ct = _cts.Token;
            Task.Run(() => AcceptClientsAsync(listener, ct));

            OnLog?.Invoke($"Started: {ComPort} @ {BaudRate} baud <-> TCP port {TcpPort}");
        }

        private async Task AcceptClientsAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"TCP {TcpPort}: accept error: {ex.Message}");
                    await Task.Delay(500, CancellationToken.None);
                    continue;
                }

                client.NoDelay = true;
                client.SendTimeout = ClientSendTimeoutMs;
                // Keepalive lets us notice clients whose network vanished without a clean disconnect.
                try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }

                var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                lock (_lock) _clients.Add(client);
                OnLog?.Invoke($"{ComPort}: client connected from {endpoint}");
                _ = Task.Run(() => ReadFromClientAsync(client, endpoint, ct));
            }
        }

        private async Task ReadFromClientAsync(TcpClient client, string endpoint, CancellationToken ct)
        {
            var buffer = new byte[4096];
            string reason = "closed by client";
            try
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(), ct);
                    if (bytesRead == 0) break;

                    var serial = _serialPort;
                    if (serial == null || !serial.IsOpen) continue;
                    try
                    {
                        serial.Write(buffer, 0, bytesRead);
                    }
                    catch (TimeoutException)
                    {
                        // The device isn't accepting data (flow control / buffer full). Drop this chunk, keep the client.
                        OnLog?.Invoke($"{ComPort}: write timed out, {bytesRead} byte(s) from {endpoint} dropped");
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                        SetFault($"write failed: {ex.Message}");
                        reason = "serial port failed";
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { reason = "bridge stopped"; }
            catch (ObjectDisposedException) { reason = "bridge stopped"; }
            catch (IOException ex) { reason = ex.InnerException?.Message ?? ex.Message; }
            catch (Exception ex) { reason = ex.Message; }
            finally
            {
                if (RemoveClient(client))
                    OnLog?.Invoke($"{ComPort}: client {endpoint} disconnected ({reason})");
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var serial = _serialPort;
            if (serial == null || !serial.IsOpen) return;

            byte[] buffer;
            try
            {
                int bytesToRead = serial.BytesToRead;
                if (bytesToRead <= 0) return;

                buffer = new byte[bytesToRead];
                int n = serial.Read(buffer, 0, bytesToRead);
                if (n < bytesToRead) Array.Resize(ref buffer, n);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                SetFault($"read failed: {ex.Message}");
                return;
            }
            catch (TimeoutException) { return; }

            lock (_lock)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    var client = _clients[i];
                    try
                    {
                        client.GetStream().Write(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex)
                    {
                        string endpoint = "client";
                        try { endpoint = client.Client.RemoteEndPoint?.ToString() ?? endpoint; } catch { }
                        _clients.RemoveAt(i);
                        try { client.Dispose(); } catch { }
                        OnLog?.Invoke($"{ComPort}: dropped {endpoint} ({(ex.InnerException ?? ex).Message})");
                    }
                }
            }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            string what = e.EventType switch
            {
                SerialError.Frame => "framing error (baud rate or data bits probably don't match the device)",
                SerialError.Overrun => "overrun (data arrived faster than it could be read; some bytes lost)",
                SerialError.RXOver => "receive buffer overflow (some bytes lost)",
                SerialError.RXParity => "parity error (parity setting probably doesn't match the device)",
                SerialError.TXFull => "transmit buffer full",
                _ => e.EventType.ToString()
            };
            OnLog?.Invoke($"{ComPort}: {what}");
        }

        private void SetFault(string message)
        {
            if (_fault != null) return; // log once
            _fault = message;
            OnLog?.Invoke($"{ComPort}: {message}. The device may have been unplugged — it will be reconnected automatically.");
        }

        private bool RemoveClient(TcpClient client)
        {
            bool removed;
            lock (_lock) removed = _clients.Remove(client);
            try { client.Dispose(); } catch { }
            return removed;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            _cts?.Cancel();

            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;

            lock (_lock)
            {
                foreach (var client in _clients)
                    try { client.Dispose(); } catch { }
                _clients.Clear();
            }

            var serial = _serialPort;
            _serialPort = null;
            if (serial != null)
            {
                serial.DataReceived -= SerialPort_DataReceived;
                serial.ErrorReceived -= SerialPort_ErrorReceived;
                try { if (serial.IsOpen) serial.Close(); } catch { }
                try { serial.Dispose(); } catch { }
            }

            _cts?.Dispose();
            _cts = null;

            OnLog?.Invoke($"Stopped: {ComPort} <-> TCP port {TcpPort}");
        }

        public int ClientCount
        {
            get { lock (_lock) return _clients.Count; }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
