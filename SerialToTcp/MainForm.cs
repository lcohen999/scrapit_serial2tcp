using System;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

namespace SerialToTcp
{
    public class MainForm : Form
    {
        private ComboBox cmbComPort;
        private ComboBox cmbBaudRate;
        private TextBox txtTcpPort;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnStartAll;
        private Button btnStopAll;
        private Button btnRefreshPorts;
        private ListView lvMappings;
        private TextBox txtLog;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private AppSettings _settings;
        private readonly List<SerialTcpBridge> _bridges = new();

        public MainForm()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            LoadMappings();

            if (_settings.AutoStart)
                StartAll();
        }

        private void InitializeComponent()
        {
            Text = "ScrapIt Serial-to-TCP Bridge";
            Size = new Size(620, 520);
            MinimumSize = new Size(580, 460);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            // --- Top panel: Add mapping controls ---
            var panelTop = new Panel { Dock = DockStyle.Top, Height = 75, Padding = new Padding(8) };

            var lblCom = new Label { Text = "COM Port:", Location = new Point(10, 12), AutoSize = true };
            cmbComPort = new ComboBox { Location = new Point(80, 8), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };

            btnRefreshPorts = new Button { Text = "↻", Location = new Point(175, 7), Width = 28, Height = 24 };
            btnRefreshPorts.Click += (s, e) => RefreshComPorts();

            var lblBaud = new Label { Text = "Baud:", Location = new Point(210, 12), AutoSize = true };
            cmbBaudRate = new ComboBox { Location = new Point(250, 8), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbBaudRate.Items.AddRange(new object[] { "300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" });
            cmbBaudRate.SelectedItem = "9600";

            var lblTcp = new Label { Text = "TCP Port:", Location = new Point(340, 12), AutoSize = true };
            txtTcpPort = new TextBox { Location = new Point(405, 8), Width = 60, Text = "4001" };

            btnAdd = new Button { Text = "Add", Location = new Point(480, 6), Width = 55, Height = 28 };
            btnAdd.Click += BtnAdd_Click;

            // Second row
            btnRemove = new Button { Text = "Remove Selected", Location = new Point(10, 42), Width = 120, Height = 26 };
            btnRemove.Click += BtnRemove_Click;

            btnStartAll = new Button { Text = "Start All", Location = new Point(250, 42), Width = 90, Height = 26, BackColor = Color.FromArgb(200, 240, 200) };
            btnStartAll.Click += (s, e) => StartAll();

            btnStopAll = new Button { Text = "Stop All", Location = new Point(350, 42), Width = 90, Height = 26, BackColor = Color.FromArgb(240, 200, 200) };
            btnStopAll.Click += (s, e) => StopAll();

            var chkAutoStart = new CheckBox { Text = "Auto-start", Location = new Point(460, 44), AutoSize = true };
            chkAutoStart.CheckedChanged += (s, e) => { _settings.AutoStart = chkAutoStart.Checked; _settings.Save(); };

            panelTop.Controls.AddRange(new Control[] {
                lblCom, cmbComPort, btnRefreshPorts, lblBaud, cmbBaudRate,
                lblTcp, txtTcpPort, btnAdd, btnRemove, btnStartAll, btnStopAll, chkAutoStart
            });

            // --- Mappings list ---
            lvMappings = new ListView
            {
                Dock = DockStyle.Top,
                Height = 160,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lvMappings.Columns.Add("COM Port", 100);
            lvMappings.Columns.Add("Baud Rate", 80);
            lvMappings.Columns.Add("TCP Port", 80);
            lvMappings.Columns.Add("Status", 100);
            lvMappings.Columns.Add("Clients", 70);

            // --- Splitter ---
            var splitter = new Splitter { Dock = DockStyle.Top, Height = 5 };

            // --- Log area ---
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9f)
            };

            Controls.Add(txtLog);
            Controls.Add(splitter);
            Controls.Add(lvMappings);
            Controls.Add(panelTop);

            // --- System tray ---
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add("Start All", null, (s, e) => StartAll());
            trayMenu.Items.Add("Stop All", null, (s, e) => StopAll());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApp());

            trayIcon = new NotifyIcon
            {
                Text = "Serial-to-TCP Bridge",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = false
            };
            trayIcon.DoubleClick += (s, e) => ShowFromTray();

            RefreshComPorts();

            // Load auto-start checkbox state
            chkAutoStart.Checked = _settings.AutoStart;
        }

        private void RefreshComPorts()
        {
            cmbComPort.Items.Clear();
            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            cmbComPort.Items.AddRange(ports);
            if (ports.Length > 0)
                cmbComPort.SelectedIndex = 0;
        }

        private void LoadMappings()
        {
            lvMappings.Items.Clear();
            foreach (var m in _settings.Mappings)
            {
                var item = new ListViewItem(new[] { m.ComPort, m.BaudRate.ToString(), m.TcpPort.ToString(), "Stopped", "0" });
                lvMappings.Items.Add(item);
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            if (cmbComPort.SelectedItem == null)
            {
                MessageBox.Show("Select a COM port.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtTcpPort.Text, out int tcpPort) || tcpPort < 1 || tcpPort > 65535)
            {
                MessageBox.Show("Enter a valid TCP port (1-65535).", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var comPort = cmbComPort.SelectedItem.ToString()!;
            var baudRate = int.Parse(cmbBaudRate.SelectedItem?.ToString() ?? "9600");

            // Check for duplicate
            if (_settings.Mappings.Any(m => m.ComPort == comPort || m.TcpPort == tcpPort))
            {
                MessageBox.Show("COM port or TCP port already in use.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mapping = new PortMapping { ComPort = comPort, BaudRate = baudRate, TcpPort = tcpPort };
            _settings.Mappings.Add(mapping);
            _settings.Save();

            var item = new ListViewItem(new[] { comPort, baudRate.ToString(), tcpPort.ToString(), "Stopped", "0" });
            lvMappings.Items.Add(item);

            // Increment default TCP port
            txtTcpPort.Text = (tcpPort + 1).ToString();

            Log($"Added mapping: {comPort} @ {baudRate} <-> TCP:{tcpPort}");
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (lvMappings.SelectedItems.Count == 0) return;

            var idx = lvMappings.SelectedIndices[0];
            var mapping = _settings.Mappings[idx];

            // Stop bridge if running
            var bridge = _bridges.FirstOrDefault(b => b.ComPort == mapping.ComPort && b.TcpPort == mapping.TcpPort);
            if (bridge != null)
            {
                bridge.Stop();
                bridge.Dispose();
                _bridges.Remove(bridge);
            }

            _settings.Mappings.RemoveAt(idx);
            _settings.Save();
            lvMappings.Items.RemoveAt(idx);

            Log($"Removed mapping: {mapping.ComPort} <-> TCP:{mapping.TcpPort}");
        }

        private void StartAll()
        {
            for (int i = 0; i < _settings.Mappings.Count; i++)
            {
                var m = _settings.Mappings[i];
                if (_bridges.Any(b => b.ComPort == m.ComPort && b.IsRunning))
                    continue;

                try
                {
                    var bridge = new SerialTcpBridge(m.ComPort, m.BaudRate, m.TcpPort);
                    bridge.OnLog += msg => BeginInvoke(() => Log(msg));
                    bridge.Start();
                    _bridges.Add(bridge);
                    lvMappings.Items[i].SubItems[3].Text = "Running";
                }
                catch (Exception ex)
                {
                    lvMappings.Items[i].SubItems[3].Text = "Error";
                    Log($"Error starting {m.ComPort}: {ex.Message}");
                }
            }

            // Start a timer to update client counts
            var timer = new Timer { Interval = 2000 };
            timer.Tick += (s, e) => UpdateClientCounts();
            timer.Start();
        }

        private void StopAll()
        {
            foreach (var bridge in _bridges)
            {
                bridge.Stop();
                bridge.Dispose();
            }
            _bridges.Clear();

            for (int i = 0; i < lvMappings.Items.Count; i++)
            {
                lvMappings.Items[i].SubItems[3].Text = "Stopped";
                lvMappings.Items[i].SubItems[4].Text = "0";
            }
        }

        private void UpdateClientCounts()
        {
            for (int i = 0; i < _settings.Mappings.Count && i < lvMappings.Items.Count; i++)
            {
                var m = _settings.Mappings[i];
                var bridge = _bridges.FirstOrDefault(b => b.ComPort == m.ComPort && b.TcpPort == m.TcpPort);
                if (bridge != null)
                {
                    lvMappings.Items[i].SubItems[4].Text = bridge.ClientCount.ToString();
                    lvMappings.Items[i].SubItems[3].Text = bridge.IsRunning ? "Running" : "Stopped";
                }
            }
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => Log(message));
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
            txtLog.AppendText(line);

            // Keep log from getting too large
            if (txtLog.TextLength > 50000)
            {
                txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 30000);
                txtLog.SelectionStart = txtLog.TextLength;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                trayIcon.Visible = true;
                trayIcon.ShowBalloonTip(1000, "Serial-to-TCP Bridge", "Running in background. Double-click to restore.", ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            trayIcon.Visible = false;
            BringToFront();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                return;
            }
            base.OnFormClosing(e);
        }

        private void ExitApp()
        {
            StopAll();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAll();
                trayIcon?.Dispose();
                trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
