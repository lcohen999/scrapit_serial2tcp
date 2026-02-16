using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SerialToTcp
{
    public class SerialTcpBridge : IDisposable
    {
        private SerialPort? _serialPort;
        private TcpListener? _tcpListener;
        private readonly List<TcpClient> _clients = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private bool _running;

        public string ComPort { get; }
        public int BaudRate { get; }
        public int TcpPort { get; }
        public bool IsRunning => _running;

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

            _cts = new CancellationTokenSource();

            _serialPort = new SerialPort(ComPort, BaudRate, Parity.None, 8, StopBits.One);
            _serialPort.ReadTimeout = 500;
            _serialPort.WriteTimeout = 500;
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();

            _tcpListener = new TcpListener(IPAddress.Any, TcpPort);
            _tcpListener.Start();
            _running = true;

            Task.Run(() => AcceptClientsAsync(_cts.Token));

            OnLog?.Invoke($"Started: {ComPort} @ {BaudRate} baud <-> TCP port {TcpPort}");
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                    lock (_lock) _clients.Add(client);
                    OnLog?.Invoke($"Client connected: {client.Client.RemoteEndPoint}");
                    _ = Task.Run(() => ReadFromClientAsync(client, ct));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task ReadFromClientAsync(TcpClient client, CancellationToken ct)
        {
            var buffer = new byte[4096];
            try
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break;
                    if (_serialPort?.IsOpen == true)
                        _serialPort.Write(buffer, 0, bytesRead);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                RemoveClient(client);
                OnLog?.Invoke("Client disconnected");
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0) return;

                var buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);

                lock (_lock)
                {
                    var dead = new List<TcpClient>();
                    foreach (var client in _clients)
                    {
                        try
                        {
                            if (client.Connected)
                                client.GetStream().Write(buffer, 0, buffer.Length);
                            else
                                dead.Add(client);
                        }
                        catch
                        {
                            dead.Add(client);
                        }
                    }
                    foreach (var dc in dead)
                    {
                        _clients.Remove(dc);
                        dc.Dispose();
                    }
                }
            }
            catch (Exception) { }
        }

        private void RemoveClient(TcpClient client)
        {
            lock (_lock)
            {
                _clients.Remove(client);
                try { client.Dispose(); } catch { }
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            _cts?.Cancel();

            lock (_lock)
            {
                foreach (var client in _clients)
                    try { client.Dispose(); } catch { }
                _clients.Clear();
            }

            try { _tcpListener?.Stop(); } catch { }

            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                try { _serialPort.Dispose(); } catch { }
            }

            OnLog?.Invoke($"Stopped: {ComPort} <-> TCP port {TcpPort}");
        }

        public int ClientCount
        {
            get { lock (_lock) return _clients.Count; }
        }

        public void Dispose()
        {
            if (_running) Stop();
            _cts?.Dispose();
        }
    }
}
