using System.IO.Ports;
using System.Text;
using System.Drawing.Drawing2D;

namespace nRF24Messenger;

public class MessengerForm : Form
{
    private SerialPort? serialPort;
    private readonly StringBuilder receiveBuffer = new();
    private readonly object bufferLock = new();
    private readonly ComboBox portSelector = new();
    private readonly Button refreshButton = new();
    private readonly Button connectButton = new();
    private readonly Button sendButton = new();
    private readonly TextBox messageInput = new();
    private readonly Label statusLabel = new();
    private readonly FlowLayoutPanel chatPanel = new();
    private readonly List<Label> pendingStatuses = new();
    private readonly Color blue = Color.FromArgb(35, 107, 210);
    private readonly Color background = Color.FromArgb(246, 248, 252);

    public MessengerForm()
    {
        Text = "nRF24 Messenger";
        Size = new Size(760, 620);
        MinimumSize = new Size(510, 390);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = background;
        BuildInterface();
        RefreshPorts();
        FormClosing += (_, _) => Disconnect();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14),
            ColumnCount = 1, RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        portSelector.Width = 105;
        portSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        refreshButton.Text = "Refresh";
        refreshButton.AutoSize = true;
        refreshButton.Click += (_, _) => RefreshPorts();
        connectButton.Text = "Connect";
        connectButton.AutoSize = true;
        connectButton.Click += (_, _) => ToggleConnection();
        statusLabel.Text = "Disconnected";
        statusLabel.AutoSize = true;
        statusLabel.Padding = new Padding(8, 7, 0, 0);
        toolbar.Controls.AddRange(new Control[]
            { portSelector, refreshButton, connectButton, statusLabel });
        root.Controls.Add(toolbar, 0, 0);

        chatPanel.Dock = DockStyle.Fill;
        chatPanel.FlowDirection = FlowDirection.TopDown;
        chatPanel.WrapContents = false;
        chatPanel.AutoScroll = true;
        chatPanel.BackColor = Color.White;
        chatPanel.BorderStyle = BorderStyle.FixedSingle;
        chatPanel.Padding = new Padding(10);
        chatPanel.SizeChanged += (_, _) => ResizeBubbles();
        root.Controls.Add(chatPanel, 0, 1);

        var composer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2,
            Padding = new Padding(0, 12, 0, 0)
        };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        messageInput.Dock = DockStyle.Fill;
        messageInput.PlaceholderText = "Message (max 31 ASCII characters)";
        messageInput.Enabled = false;
        messageInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SendMessage();
        };
        sendButton.Text = "Send";
        sendButton.Dock = DockStyle.Fill;
        sendButton.Enabled = false;
        sendButton.BackColor = blue;
        sendButton.ForeColor = Color.White;
        sendButton.FlatStyle = FlatStyle.Flat;
        sendButton.FlatAppearance.BorderSize = 0;
        sendButton.Click += (_, _) => SendMessage();
        composer.Controls.Add(messageInput, 0, 0);
        composer.Controls.Add(sendButton, 1, 0);
        root.Controls.Add(composer, 0, 2);
    }

    private void RefreshPorts()
    {
        string? selected = portSelector.SelectedItem as string;
        portSelector.Items.Clear();
        foreach (string port in SerialPort.GetPortNames().OrderBy(p => p))
            portSelector.Items.Add(port);
        if (selected != null && portSelector.Items.Contains(selected))
            portSelector.SelectedItem = selected;
        else if (portSelector.Items.Count > 0)
            portSelector.SelectedIndex = 0;
    }

    private void ToggleConnection()
    {
        if (serialPort?.IsOpen == true) Disconnect();
        else Connect();
    }

    private void Connect()
    {
        if (portSelector.SelectedItem is not string portName)
        {
            MessageBox.Show("Please select a COM port.");
            return;
        }
        try
        {
            serialPort = new SerialPort(portName, 115200)
            {
                NewLine = "\n", Encoding = Encoding.ASCII, ReadTimeout = 1000
            };
            serialPort.DataReceived += OnDataReceived;
            serialPort.Open();
            statusLabel.Text = $"Connected: {portName}";
            connectButton.Text = "Disconnect";
            messageInput.Enabled = true;
            sendButton.Enabled = true;
            portSelector.Enabled = false;
            refreshButton.Enabled = false;
            AddSystemMessage($"Connected to {portName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to connect:\n{ex.Message}", "Connection Error");
            Disconnect();
        }
    }

    private void Disconnect()
    {
        try
        {
            if (serialPort != null)
            {
                serialPort.DataReceived -= OnDataReceived;
                if (serialPort.IsOpen) serialPort.Close();
                serialPort.Dispose();
                serialPort = null;
            }
        }
        catch (Exception) { /* USB device may already be disconnected. */ }
        lock (bufferLock) receiveBuffer.Clear();
        foreach (Label label in pendingStatuses)
            if (!label.IsDisposed) label.Text = "Status unknown (disconnected)";
        pendingStatuses.Clear();
        statusLabel.Text = "Disconnected";
        connectButton.Text = "Connect";
        messageInput.Enabled = false;
        sendButton.Enabled = false;
        portSelector.Enabled = true;
        refreshButton.Enabled = true;
    }

    private void SendMessage()
    {
        if (serialPort?.IsOpen != true) return;
        string message = messageInput.Text.Trim();
        if (message.Length == 0) return;
        if (message.Length > 31 || message.Any(c => c > 127 || c < 32))
        {
            MessageBox.Show("Use up to 31 printable ASCII characters.", "Invalid message");
            return;
        }
        try
        {
            serialPort.Write(message + "\n");
            Label delivery = AddBubble("You", message, true, "Sent to Arduino; radio status pending");
            pendingStatuses.Add(delivery);
            messageInput.Clear();
            messageInput.Focus();
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Send error: {ex.Message}");
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = (SerialPort)sender;
            string data = port.ReadExisting();
            var lines = new List<string>();
            lock (bufferLock)
            {
                receiveBuffer.Append(data);
                string current = receiveBuffer.ToString();
                int index;
                while ((index = current.IndexOf('\n')) >= 0)
                {
                    lines.Add(current[..index].TrimEnd('\r'));
                    current = current[(index + 1)..];
                }
                receiveBuffer.Clear();
                receiveBuffer.Append(current);
            }
            foreach (string line in lines)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() => HandleSerialLine(line)));
                }
                catch (InvalidOperationException) { return; }
            }
        }
        catch (Exception) { /* Port may have been unplugged. */ }
    }

    private void HandleSerialLine(string line)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(line)) return;
        string trimmed = line.Trim();
        // Only recognise explicit prefixes. Unknown firmware output stays visible
        // as a system event, rather than being mistaken for a chat message.
        if (trimmed.StartsWith("Received: ", StringComparison.OrdinalIgnoreCase))
        {
            AddBubble("Remote", trimmed[10..], false, "Received");
        }
        else if (trimmed.StartsWith("You: ", StringComparison.Ordinal))
        {
            // The Arduino echoes transmitted text. We already show it when
            // written to USB, so do not create a duplicate outgoing bubble.
        }
        else if (trimmed.Equals("Delivered (radio ACK received)", StringComparison.Ordinal))
        {
            UpdateOldestPending("Radio acknowledged");
        }
        else if (trimmed.Equals("Delivery failed - no radio ACK", StringComparison.Ordinal))
        {
            UpdateOldestPending("No radio ACK - delivery failed");
        }
        else if (trimmed.Equals("Message too long (max 31 characters)", StringComparison.Ordinal))
        {
            UpdateOldestPending("Rejected by Arduino - too long");
        }
        else
        {
            AddSystemMessage(trimmed);
        }
    }

    private void UpdateOldestPending(string result)
    {
        if (pendingStatuses.Count == 0)
        {
            AddSystemMessage(result);
            return;
        }
        Label label = pendingStatuses[0];
        pendingStatuses.RemoveAt(0);
        if (!label.IsDisposed) label.Text = result;
    }

    private Label AddBubble(string sender, string message, bool outgoing, string status)
    {
        var wrapper = new Panel { Height = 94, Margin = new Padding(0, 4, 0, 4) };
        wrapper.Tag = outgoing;
        var bubble = new Panel { BackColor = outgoing ? blue : background };
        bubble.Tag = "bubble";
        var name = new Label
        {
            Text = sender, Font = new Font(Font, FontStyle.Bold),
            ForeColor = outgoing ? Color.White : Color.FromArgb(45, 55, 75),
            Location = new Point(12, 8), AutoSize = true
        };
        var body = new Label
        {
            Text = message, AutoSize = false,
            ForeColor = outgoing ? Color.White : Color.Black,
            Location = new Point(12, 31), Height = 32
        };
        var info = new Label
        {
            Text = $"{DateTime.Now:HH:mm:ss}  ·  {status}",
            Font = new Font("Segoe UI", 8),
            ForeColor = outgoing ? Color.FromArgb(228, 237, 255) : Color.DimGray,
            Location = new Point(12, 67), AutoSize = true
        };
        bubble.Controls.AddRange(new Control[] { name, body, info });
        wrapper.Controls.Add(bubble);
        chatPanel.Controls.Add(wrapper);
        PositionBubble(wrapper);
        chatPanel.ScrollControlIntoView(wrapper);
        return info;
    }

    private void AddSystemMessage(string message)
    {
        var label = new Label
        {
            Text = $"{DateTime.Now:HH:mm:ss}  ·  {message}",
            ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleCenter,
            Height = 32, Margin = new Padding(0, 4, 0, 4)
        };
        label.Tag = "system";
        chatPanel.Controls.Add(label);
        ResizeBubbles();
        chatPanel.ScrollControlIntoView(label);
    }

    private void ResizeBubbles()
    {
        int width = Math.Max(200, chatPanel.ClientSize.Width - 34);
        foreach (Control control in chatPanel.Controls)
        {
            control.Width = width;
            if (Equals(control.Tag, "system")) continue;
            PositionBubble((Panel)control);
        }
    }

    private void PositionBubble(Panel wrapper)
    {
        int available = Math.Max(200, chatPanel.ClientSize.Width - 34);
        wrapper.Width = available;
        Panel bubble = (Panel)wrapper.Controls[0];
        int bubbleWidth = Math.Min(440, Math.Max(190, (int)(available * 0.76)));
        bubble.Bounds = new Rectangle(
            Equals(wrapper.Tag, true) ? available - bubbleWidth : 0,
            0, bubbleWidth, 90);
        var body = (Label)bubble.Controls[1];
        body.Width = bubbleWidth - 24;
    }
}
