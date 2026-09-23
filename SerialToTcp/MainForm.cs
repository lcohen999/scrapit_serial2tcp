using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SerialToTcp
{
    public class DataFlowPanel : Panel
    {
        private int _offset;
        private bool _active;
        private readonly Timer _animTimer;
        private int _clientCount;

        public bool Active
        {
            get => _active;
            set { _active = value; Invalidate(); }
        }

        public int ClientCount
        {
            get => _clientCount;
            set { _clientCount = value; Invalidate(); }
        }

        public DataFlowPanel()
        {
            DoubleBuffered = true;
            _animTimer = new Timer { Interval = 80 };
            _animTimer.Tick += (s, e) => { _offset = (_offset + 1) % 20; Invalidate(); };
            _animTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int midY = Height / 2;
            int leftBox = 10;
            int rightBox = Width - 70;
            int boxW = 60;
            int boxH = 36;

            // Serial port box
            using var serialBrush = new SolidBrush(Color.FromArgb(0, 98, 153));
            g.FillRoundedRectangle(serialBrush, leftBox, midY - boxH / 2, boxW, boxH, 6);
            using var textFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var whiteBrush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("COM", textFont, whiteBrush, new RectangleF(leftBox, midY - boxH / 2, boxW, boxH), sf);

            // TCP box
            using var tcpBrush = new SolidBrush(Color.FromArgb(0, 141, 60));
            g.FillRoundedRectangle(tcpBrush, rightBox, midY - boxH / 2, boxW, boxH, 6);
            g.DrawString("TCP", textFont, whiteBrush, new RectangleF(rightBox, midY - boxH / 2, boxW, boxH), sf);

            // Connection line
            int lineStart = leftBox + boxW + 8;
            int lineEnd = rightBox - 8;

            if (!_active)
            {
                using var pen = new Pen(Color.Gray, 2) { DashStyle = DashStyle.Dash };
                g.DrawLine(pen, lineStart, midY, lineEnd, midY);

                using var statusFont = new Font("Segoe UI", 7.5f);
                using var grayBrush = new SolidBrush(Color.Gray);
                g.DrawString("Not Connected", statusFont, grayBrush,
                    (lineStart + lineEnd) / 2, midY + 12, new StringFormat { Alignment = StringAlignment.Center });
            }
            else
            {
                // Animated data packets flowing both directions
                using var pen = new Pen(Color.FromArgb(100, 0, 131, 201), 2);
                g.DrawLine(pen, lineStart, midY - 4, lineEnd, midY - 4);
                g.DrawLine(pen, lineStart, midY + 4, lineEnd, midY + 4);

                // Forward arrows (COM -> TCP) on top line
                for (int x = lineStart; x < lineEnd - 10; x += 20)
                {
                    int px = x + _offset;
                    if (px > lineEnd - 10) continue;
                    float alpha = 200f;
                    using var arrowBrush = new SolidBrush(Color.FromArgb((int)alpha, 0, 131, 201));
                    var pts = new[] {
                        new PointF(px, midY - 8), new PointF(px + 10, midY - 4), new PointF(px, midY)
                    };
                    g.FillPolygon(arrowBrush, pts);
                }

                // Reverse arrows (TCP -> COM) on bottom line
                for (int x = lineEnd; x > lineStart + 10; x -= 20)
                {
                    int px = x - _offset;
                    if (px < lineStart + 10) continue;
                    float alpha = 200f;
                    using var arrowBrush = new SolidBrush(Color.FromArgb((int)alpha, 69, 181, 73));
                    var pts = new[] {
                        new PointF(px, midY + 0), new PointF(px - 10, midY + 4), new PointF(px, midY + 8)
                    };
                    g.FillPolygon(arrowBrush, pts);
                }

                // Status text
                using var statusFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                using var greenBrush = new SolidBrush(Color.FromArgb(0, 141, 60));
                string statusText = _clientCount == 1 ? "1 client connected" : $"{_clientCount} clients connected";
                g.DrawString(statusText, statusFont, greenBrush,
                    (lineStart + lineEnd) / 2, midY + 14, new StringFormat { Alignment = StringAlignment.Center });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer?.Dispose();
            base.Dispose(disposing);
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, int x, int y, int w, int h, int r)
        {
            using var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }

    public class MainForm : Form
    {
        private ComboBox cmbComPort = null!;
        private ComboBox cmbBaudRate = null!;
        private TextBox txtTcpPort = null!;
        private Button btnAdd = null!;
        private Button btnRemove = null!;
        private Button btnStartAll = null!;
        private Button btnStopAll = null!;
        private Button btnRefreshPorts = null!;
        private ListView lvMappings = null!;
        private TextBox txtLog = null!;
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;
        private PictureBox picLogo = null!;
        private DataFlowPanel dataFlowPanel = null!;
        private Timer updateTimer = null!;

        private AppSettings _settings = null!;
        private readonly string? _settingsWarning;
        private readonly Dictionary<PortMapping, SerialTcpBridge> _bridges = new();
        // Full explanation of the last start failure per mapping, shown on double-click / tooltip.
        private readonly Dictionary<PortMapping, string> _errors = new();
        // Short reason of the last failure, so background retries only report when something changes.
        private readonly Dictionary<PortMapping, string> _errorReasons = new();

        // Failed or faulted ports are retried on this interval until Stop All is clicked.
        private const int RetrySeconds = 15;
        private Timer retryTimer = null!;
        private bool _wantRunning;
        private bool _starting;

        private const int ColStatus = 3;
        private const int ColClients = 4;
        private bool _trayHintShown;

        public MainForm()
        {
            _settings = AppSettings.Load(out _settingsWarning);
            InitializeComponent();
            LoadMappings();
        }

        // Auto-start happens here rather than in the constructor: the window handle must exist before
        // bridges can marshal log messages onto the UI thread.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Log($"Settings file: {AppSettings.SettingsPath}");
            if (_settingsWarning != null) Log(_settingsWarning);

            if (_settings.StartMinimized)
                WindowState = FormWindowState.Minimized;

            if (_settings.AutoStart)
                StartAll(interactive: false);
        }

        protected override void WndProc(ref Message m)
        {
            // Another launch of the exe asked us to come forward (see Program.Main).
            if (m.Msg == Program.WM_SHOWME)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private void SaveSettings()
        {
            if (!_settings.Save(out var error))
                Log(error!);
        }

        private void InitializeComponent()
        {
            Text = "ScrapIt Serial-to-TCP Bridge";
            Size = new Size(640, 620);
            MinimumSize = new Size(600, 560);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            Icon = LoadAppIcon(null); // all frames, so Windows picks the right size for title bar and taskbar

            // --- Header panel with logo ---
            var panelHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White };

            picLogo = new PictureBox
            {
                Location = new Point(10, 5),
                Size = new Size(200, 50),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            // Load embedded logo (must copy stream - Image requires it to stay open)
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream("SerialToTcp.logo.png");
                if (stream != null)
                {
                    var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    stream.Dispose();
                    ms.Position = 0;
                    picLogo.Image = Image.FromStream(ms);
                }
            }
            catch { }

            var lblTitle = new Label
            {
                Text = "Serial-to-TCP Bridge",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 34, 57),
                Location = new Point(220, 15),
                AutoSize = true
            };

            panelHeader.Controls.Add(picLogo);
            panelHeader.Controls.Add(lblTitle);

            // --- Data flow animation panel ---
            dataFlowPanel = new DataFlowPanel
            {
                Dock = DockStyle.Top,
                Height = 55,
                BackColor = Color.FromArgb(245, 248, 250)
            };

            // --- Controls panel ---
            var panelTop = new Panel { Dock = DockStyle.Top, Height = 75, Padding = new Padding(8) };

            var lblCom = new Label { Text = "COM Port:", Location = new Point(10, 12), AutoSize = true };
            cmbComPort = new ComboBox { Location = new Point(80, 8), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };

            btnRefreshPorts = new Button { Text = "\u21BB", Location = new Point(175, 7), Width = 28, Height = 24 };
            btnRefreshPorts.Click += (s, e) => RefreshComPorts();

            var lblBaud = new Label { Text = "Baud:", Location = new Point(210, 12), AutoSize = true };
            cmbBaudRate = new ComboBox { Location = new Point(250, 8), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbBaudRate.Items.AddRange(new object[] { "300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" });
            cmbBaudRate.SelectedItem = "9600";

            var lblTcp = new Label { Text = "TCP Port:", Location = new Point(340, 12), AutoSize = true };
            txtTcpPort = new TextBox { Location = new Point(405, 8), Width = 60, Text = "4001" };

            btnAdd = new Button { Text = "Add", Location = new Point(480, 6), Width = 55, Height = 28 };
            btnAdd.Click += BtnAdd_Click;

            btnRemove = new Button { Text = "Remove Selected", Location = new Point(10, 42), Width = 120, Height = 26 };
            btnRemove.Click += BtnRemove_Click;

            btnStartAll = new Button { Text = "Start All", Location = new Point(250, 42), Width = 90, Height = 26, BackColor = Color.FromArgb(200, 240, 200) };
            btnStartAll.Click += (s, e) => StartAll(interactive: true);

            btnStopAll = new Button { Text = "Stop All", Location = new Point(350, 42), Width = 90, Height = 26, BackColor = Color.FromArgb(240, 200, 200) };
            btnStopAll.Click += (s, e) => StopAll();

            var chkAutoStart = new CheckBox { Text = "Auto-start", Location = new Point(460, 44), AutoSize = true };
            chkAutoStart.Checked = _settings.AutoStart;
            chkAutoStart.CheckedChanged += (s, e) => { _settings.AutoStart = chkAutoStart.Checked; SaveSettings(); };

            var chkStartMinimized = new CheckBox { Text = "Start in tray", Location = new Point(140, 44), AutoSize = true };
            chkStartMinimized.Checked = _settings.StartMinimized;
            chkStartMinimized.CheckedChanged += (s, e) => { _settings.StartMinimized = chkStartMinimized.Checked; SaveSettings(); };

            panelTop.Controls.AddRange(new Control[] {
                lblCom, cmbComPort, btnRefreshPorts, lblBaud, cmbBaudRate,
                lblTcp, txtTcpPort, btnAdd, btnRemove, chkStartMinimized, btnStartAll, btnStopAll, chkAutoStart
            });

            // --- Mappings list ---
            lvMappings = new ListView
            {
                Dock = DockStyle.Top,
                Height = 140,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                ShowItemToolTips = true
            };
            lvMappings.DoubleClick += (s, e) => ShowSelectedError();
            lvMappings.Columns.Add("COM Port", 100);
            lvMappings.Columns.Add("Baud Rate", 80);
            lvMappings.Columns.Add("TCP Port", 80);
            lvMappings.Columns.Add("Status", 120);
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

            // Add controls (reverse order for Dock)
            Controls.Add(txtLog);
            Controls.Add(splitter);
            Controls.Add(lvMappings);
            Controls.Add(panelTop);
            Controls.Add(dataFlowPanel);
            Controls.Add(panelHeader);

            // --- System tray ---
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) => ShowFromTray());
            trayMenu.Items.Add("Start All", null, (s, e) => StartAll(interactive: false));
            trayMenu.Items.Add("Stop All", null, (s, e) => StopAll());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, (s, e) => ExitApp());

            trayIcon = new NotifyIcon
            {
                Text = "ScrapIt Serial-to-TCP Bridge",
                ContextMenuStrip = trayMenu,
                Visible = false
            };

            // The tray wants the 16px frame; letting Windows shrink the 256px one looks blurry.
            trayIcon.Icon = LoadAppIcon(SystemInformation.SmallIconSize);

            trayIcon.DoubleClick += (s, e) => ShowFromTray();

            // Update timer for client counts and animation
            updateTimer = new Timer { Interval = 1500 };
            updateTimer.Tick += (s, e) => UpdateStatus();
            updateTimer.Start();

            retryTimer = new Timer { Interval = RetrySeconds * 1000 };
            retryTimer.Tick += (s, e) => { if (_wantRunning) StartAll(interactive: false, isRetry: true); };

            RefreshComPorts();
        }

        // app.ico is embedded in the exe. It used to be loaded from the exe's folder, but the single-file
        // publish bundles it inside the exe, so it was never there and the tray fell back to the generic icon.
        private static Icon LoadAppIcon(Size? size)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SerialToTcp.app.ico");
                if (stream != null)
                    return size is Size sz ? new Icon(stream, sz) : new Icon(stream);
            }
            catch { }
            return SystemIcons.Application;
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
                lvMappings.Items.Add(new ListViewItem(new[] { m.ComPort, m.BaudRate.ToString(), m.TcpPort.ToString(), "Stopped", "0" }));
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            if (cmbComPort.SelectedItem == null)
            {
                MessageBox.Show("Select a COM port. If the list is empty, Windows doesn't see any serial ports — " +
                    "plug in the adapter and click ↻.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtTcpPort.Text, out int tcpPort) || tcpPort < 1 || tcpPort > 65535)
            {
                MessageBox.Show("Enter a valid TCP port (1-65535).", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var comPort = cmbComPort.SelectedItem.ToString()!;
            var baudRate = int.Parse(cmbBaudRate.SelectedItem?.ToString() ?? "9600");

            var clash = _settings.Mappings.FirstOrDefault(m =>
                string.Equals(m.ComPort, comPort, StringComparison.OrdinalIgnoreCase) || m.TcpPort == tcpPort);
            if (clash != null)
            {
                MessageBox.Show($"There is already a mapping {clash.ComPort} <-> TCP:{clash.TcpPort}. " +
                    "Each COM port and each TCP port can only be used once.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mapping = new PortMapping { ComPort = comPort, BaudRate = baudRate, TcpPort = tcpPort };
            _settings.Mappings.Add(mapping);
            SaveSettings();

            lvMappings.Items.Add(new ListViewItem(new[] { comPort, baudRate.ToString(), tcpPort.ToString(), "Stopped", "0" }));

            if (tcpPort < 65535) txtTcpPort.Text = (tcpPort + 1).ToString();
            Log($"Added mapping: {comPort} @ {baudRate} <-> TCP:{tcpPort}");
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (lvMappings.SelectedIndices.Count == 0) return;

            var idx = lvMappings.SelectedIndices[0];
            var mapping = _settings.Mappings[idx];

            if (_bridges.Remove(mapping, out var bridge))
                bridge.Dispose();
            _errors.Remove(mapping);
            _errorReasons.Remove(mapping);

            _settings.Mappings.RemoveAt(idx);
            SaveSettings();
            lvMappings.Items.RemoveAt(idx);

            Log($"Removed mapping: {mapping.ComPort} <-> TCP:{mapping.TcpPort}");
        }

        /// <param name="interactive">True when the user clicked the button: failures are also shown in a dialog.</param>
        /// <param name="isRetry">True for the background retry timer: only report failures whose reason changed.</param>
        private async void StartAll(bool interactive, bool isRetry = false)
        {
            if (_starting) return;
            _starting = true;
            _wantRunning = true;
            retryTimer.Start();
            try
            {
                // Failures that are new (or changed reason) since the last attempt; only these get logged/announced.
                var newFailures = new List<(PortMapping Mapping, BridgeStartException Error)>();

                for (int i = 0; i < _settings.Mappings.Count; i++)
                {
                    var m = _settings.Mappings[i];
                    var item = lvMappings.Items[i];

                    if (_bridges.TryGetValue(m, out var existing))
                    {
                        if (existing.IsRunning && existing.Fault == null)
                            continue;
                        // Faulted (e.g. adapter was unplugged): tear it down and reopen.
                        existing.Dispose();
                        _bridges.Remove(m);
                    }

                    var bridge = new SerialTcpBridge(m.ComPort, m.BaudRate, m.TcpPort);
                    bridge.OnLog += Log;
                    try
                    {
                        bridge.Start();
                        _bridges[m] = bridge;
                        _errors.Remove(m);
                        _errorReasons.Remove(m);
                        SetStatus(item, "Running", null);
                    }
                    catch (BridgeStartException ex)
                    {
                        bridge.Dispose();
                        bool changed = !isRetry || !_errorReasons.TryGetValue(m, out var prev) || prev != ex.ShortReason;
                        _errorReasons[m] = ex.ShortReason;
                        if (changed)
                        {
                            _errors[m] = ex.Message;
                            Log($"Could not start {m.ComPort} <-> TCP:{m.TcpPort}: {ex.Message}");
                            Log($"    (Windows error: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message})");
                            newFailures.Add((m, ex));
                        }
                        SetStatus(item, $"{ex.ShortReason} (retrying)", _errors[m]);
                    }
                }

                if (newFailures.Count == 0) return;

                await IdentifyPortHolders(newFailures
                    .Where(f => f.Error.InnerException is UnauthorizedAccessException)
                    .Select(f => f.Mapping).ToList());

                if (!_wantRunning) return; // Stop All was clicked while we were looking

                var still = newFailures.Where(f => _errors.ContainsKey(f.Mapping)).ToList();
                if (still.Count == 0) return;

                if (interactive)
                {
                    var text = string.Join("\r\n\r\n", still.Select(f =>
                        $"{f.Mapping.ComPort} <-> TCP:{f.Mapping.TcpPort}\r\n{_errors[f.Mapping]}"));
                    MessageBox.Show(text + $"\r\n\r\nThese will be retried automatically every {RetrySeconds} seconds.",
                        "Some ports could not be started", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (trayIcon.Visible || !Visible || WindowState == FormWindowState.Minimized)
                {
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(5000, "Serial-to-TCP Bridge",
                        $"{still.Count} port(s) could not be started — retrying every {RetrySeconds}s. Open the window for details.",
                        ToolTipIcon.Warning);
                }
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>For ports that failed with "access denied", find out which program has them open.</summary>
        private async Task IdentifyPortHolders(List<PortMapping> mappings)
        {
            if (mappings.Count == 0) return;

            var ports = mappings.Select(m => m.ComPort).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Log($"Looking for the program that has {string.Join(", ", ports)} open...");
            var result = await PortHolderFinder.FindAsync(ports, TimeSpan.FromSeconds(20));

            foreach (var m in mappings)
            {
                // Skip mappings that were removed, started, or stopped while the scan ran.
                int idx = _settings.Mappings.IndexOf(m);
                if (idx < 0 || !_errors.TryGetValue(m, out var detail)) continue;

                var who = PortHolderFinder.Explain(result, m.ComPort);
                Log(who);
                _errors[m] = who + "\r\n\r\n" + detail;
                lvMappings.Items[idx].ToolTipText = _errors[m];
            }
        }

        private void StopAll()
        {
            _wantRunning = false;
            retryTimer.Stop();

            foreach (var bridge in _bridges.Values)
                bridge.Dispose();
            _bridges.Clear();
            _errors.Clear();
            _errorReasons.Clear();

            foreach (ListViewItem item in lvMappings.Items)
            {
                SetStatus(item, "Stopped", null);
                item.SubItems[ColClients].Text = "0";
            }
        }

        private static void SetStatus(ListViewItem item, string status, string? tooltip)
        {
            item.SubItems[ColStatus].Text = status;
            item.ToolTipText = tooltip ?? "";
            item.ForeColor = status == "Running" || status == "Stopped" ? SystemColors.WindowText : Color.Firebrick;
        }

        private void ShowSelectedError()
        {
            if (lvMappings.SelectedIndices.Count == 0) return;
            var m = _settings.Mappings[lvMappings.SelectedIndices[0]];

            string? detail = _errors.TryGetValue(m, out var err) ? err
                           : _bridges.TryGetValue(m, out var b) && b.Fault != null ? $"{m.ComPort} {b.Fault}. Reconnecting automatically every {RetrySeconds} seconds."
                           : null;
            if (detail != null)
                MessageBox.Show(detail, $"{m.ComPort} <-> TCP:{m.TcpPort}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void UpdateStatus()
        {
            int totalClients = 0;
            bool anyRunning = false;

            for (int i = 0; i < _settings.Mappings.Count && i < lvMappings.Items.Count; i++)
            {
                var m = _settings.Mappings[i];
                if (!_bridges.TryGetValue(m, out var bridge)) continue;

                var item = lvMappings.Items[i];
                int cc = bridge.ClientCount;
                totalClients += cc;
                item.SubItems[ColClients].Text = cc.ToString();

                if (bridge.Fault != null)
                    SetStatus(item, "Faulted (retrying)", $"{m.ComPort} {bridge.Fault}. Reconnecting automatically every {RetrySeconds} seconds.");
                else if (bridge.IsRunning)
                {
                    SetStatus(item, "Running", null);
                    anyRunning = true;
                }
            }

            dataFlowPanel.Active = anyRunning && totalClients > 0;
            dataFlowPanel.ClientCount = totalClients;
        }

        // Safe to call from any thread (bridges log from socket and serial threads).
        private void Log(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(() => Log(message)); } catch (InvalidOperationException) { }
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
            txtLog.AppendText(line);

            if (txtLog.TextLength > 50000)
            {
                txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 30000);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                trayIcon.Visible = true;
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    trayIcon.ShowBalloonTip(1000, "ScrapIt Serial-to-TCP Bridge", "Running in background. Double-click to restore.", ToolTipIcon.Info);
                }
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            trayIcon.Visible = false;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
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
                foreach (var bridge in _bridges.Values)
                    bridge.Dispose();
                _bridges.Clear();
                updateTimer?.Dispose();
                retryTimer?.Dispose();
                trayIcon?.Dispose();
                trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
