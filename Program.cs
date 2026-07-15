using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace InfoPCTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "InfoPC Tray";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem startupItem;
    private InfoForm? infoForm;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostra informazioni", null, async (_, _) => await ShowInformationAsync());
        menu.Items.Add("Copia negli appunti", null, async (_, _) => await CopyInformationAsync());
        menu.Items.Add("Aggiorna", null, async (_, _) => await RefreshInformationAsync());
        menu.Items.Add(new ToolStripSeparator());

        startupItem = new ToolStripMenuItem("Avvia automaticamente con Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        startupItem.CheckedChanged += (_, _) => SetStartup(startupItem.Checked);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitApplication());

        trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = AppName,
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                await ShowInformationAsync();
        };
        trayIcon.ShowBalloonTip(2500, AppName,
            "Il programma e' attivo. Clicca l'icona IP vicino all'orologio.",
            ToolTipIcon.Info);
    }

    private async Task ShowInformationAsync()
    {
        infoForm ??= new InfoForm();
        infoForm.Show();
        infoForm.Activate();
        infoForm.SetLoading();
        infoForm.SetText(await ComputerInfo.GetReportAsync());
    }

    private async Task CopyInformationAsync()
    {
        var text = await ComputerInfo.GetReportAsync();
        Clipboard.SetText(text);
        trayIcon.ShowBalloonTip(1800, AppName, "Informazioni copiate negli appunti.", ToolTipIcon.Info);
    }

    private async Task RefreshInformationAsync()
    {
        if (infoForm is { Visible: true })
        {
            infoForm.SetLoading();
            infoForm.SetText(await ComputerInfo.GetReportAsync());
        }
        else
        {
            await ShowInformationAsync();
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) is string;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(AppName, false);
    }

    private void ExitApplication()
    {
        trayIcon.Visible = false;
        infoForm?.CloseForExit();
        trayIcon.Dispose();
        ExitThread();
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString("IP", font, Brushes.White, new RectangleF(0, 0, 32, 31), format);
        return Icon.FromHandle(bitmap.GetHicon()).Clone() as Icon ?? SystemIcons.Information;
    }
}

internal sealed class InfoForm : Form
{
    private readonly RichTextBox output;
    private bool exiting;

    public InfoForm()
    {
        Text = "InfoPC Tray - Informazioni di rete";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(616, 486);
        Size = new Size(757, 630);
        ShowIcon = true;

        var title = new Label
        {
            Text = "INFORMAZIONI DEL COMPUTER E DELLA RETE",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold)
        };

        output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10.5f),
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
            DetectUrls = false
        };
        var outputHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(11, 11, 0, 0)
        };
        outputHost.Controls.Add(output);
        var copyButton = new Button { Text = "Copia tutto", AutoSize = true };
        copyButton.Click += (_, _) => Clipboard.SetText(output.Text);
        var closeButton = new Button { Text = "Chiudi finestra", AutoSize = true };
        closeButton.Click += (_, _) => Hide();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 230,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);
        var signature = new Label
        {
            Text = "Fabio Barbon & Roberto Bertella Software (2026)  -  Versione 1.0",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            ForeColor = Color.DimGray,
            BackColor = Color.FromArgb(245, 245, 245),
            Font = new Font("Segoe UI", 9, FontStyle.Regular)
        };
        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.FromArgb(245, 245, 245)
        };
        bottomBar.Controls.Add(signature);
        bottomBar.Controls.Add(buttons);
        Controls.Add(outputHost);
        Controls.Add(bottomBar);
        Controls.Add(title);
        FormClosing += (_, e) =>
        {
            if (!exiting)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void SetLoading()
    {
        output.Clear();
        output.SelectionColor = Color.DimGray;
        output.AppendText("Lettura delle informazioni in corso...");
    }

    public void SetText(string text)
    {
        output.Clear();
        output.Text = text;
        output.SelectAll();
        output.SelectionFont = new Font("Consolas", 10.5f, FontStyle.Regular);
        output.SelectionColor = Color.FromArgb(35, 35, 35);
        HighlightLine("Nome PC", Color.FromArgb(0, 90, 170));
        HighlightAllLines("Indirizzo IP", Color.FromArgb(0, 90, 170), FontStyle.Bold);
        HighlightAllLines("DETTAGLI SCHEDE DI RETE", Color.FromArgb(35, 35, 35), FontStyle.Bold | FontStyle.Underline);
        HighlightAllLines("PORTE LOCALI IN ASCOLTO", Color.FromArgb(35, 35, 35), FontStyle.Bold | FontStyle.Underline);
        output.Select(0, 0);
    }

    private void HighlightLine(string label, Color color)
    {
        var start = output.Text.IndexOf(label, StringComparison.Ordinal);
        if (start < 0) return;
        var end = output.Text.IndexOf('\n', start);
        if (end < 0) end = output.Text.Length;
        output.Select(start, end - start);
        output.SelectionFont = new Font("Consolas", 10.5f, FontStyle.Bold);
        output.SelectionColor = color;
    }

    private void HighlightAllLines(string label, Color color, FontStyle style)
    {
        var searchFrom = 0;
        while (searchFrom < output.Text.Length)
        {
            var start = output.Text.IndexOf(label, searchFrom, StringComparison.Ordinal);
            if (start < 0) break;
            var end = output.Text.IndexOf('\n', start);
            if (end < 0) end = output.Text.Length;
            output.Select(start, end - start);
            output.SelectionFont = new Font("Consolas", 10.5f, style);
            output.SelectionColor = color;
            searchFrom = end + 1;
        }
    }

    public void CloseForExit() { exiting = true; Close(); }
}

internal static class ComputerInfo
{
    public static async Task<string> GetReportAsync()
    {
        var publicIpTask = GetPublicIpAsync();
        var adapters = GetNetworkAdapters();
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpPorts = properties.GetActiveTcpListeners().Select(x => x.Port).Distinct().Order().ToArray();
        var udpPorts = properties.GetActiveUdpListeners().Select(x => x.Port).Distinct().Order().ToArray();
        var publicIp = await publicIpTask;

        var report = new System.Text.StringBuilder();
        report.AppendLine(FormatLine("Nome PC", Environment.MachineName));
        report.AppendLine(FormatLine("Utente", Environment.UserName));
        report.AppendLine(FormatLine("IP pubblico", publicIp));
        report.AppendLine(FormatLine("Aggiornato", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")));

        report.AppendLine();
        report.AppendLine("DETTAGLI SCHEDE DI RETE".PadRight(82));
        if (adapters.Count == 0)
        {
            report.AppendLine("Nessuna scheda di rete attiva con indirizzo IPv4.");
        }
        else
        {
            foreach (var adapter in adapters)
            {
                report.AppendLine($"[{adapter.Name}]");
                report.AppendLine(FormatLine("Descrizione", adapter.Description));
                report.AppendLine(FormatLine("DHCP", adapter.DhcpEnabled ? "Automatico" : "Manuale"));
                report.AppendLine(FormatLine("Indirizzo IP", string.Join(", ", adapter.IpAddresses)));
                report.AppendLine(FormatLine("Gateway", adapter.Gateways.Count == 0 ? "non disponibile" : string.Join(", ", adapter.Gateways)));
                report.AppendLine(FormatLine("DNS", adapter.DnsServers.Count == 0 ? "non disponibile" : string.Join(", ", adapter.DnsServers)));
                report.AppendLine(FormatLine("MAC address", adapter.MacAddress));
                report.AppendLine();
            }
        }

        report.AppendLine("PORTE LOCALI IN ASCOLTO".PadRight(82));
        report.AppendLine("Le porte indicate sono aperte sul PC, ma non necessariamente su Internet.");
        report.AppendLine();
        report.AppendLine($"TCP ({tcpPorts.Length}):");
        report.AppendLine(FormatPorts(tcpPorts));
        report.AppendLine();
        report.AppendLine($"UDP ({udpPorts.Length}):");
        report.Append(FormatPorts(udpPorts));
        return report.ToString();
    }

    private static List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Select(n =>
            {
                var properties = n.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                var addresses = properties.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !a.Address.ToString().StartsWith("169.254."))
                    .Select(a => a.Address.ToString()).Distinct().Order().ToList();
                return new NetworkAdapterInfo(
                    n.Name,
                    n.Description,
                    ipv4?.IsDhcpEnabled == true,
                    FormatMacAddress(n.GetPhysicalAddress()),
                    addresses,
                    properties.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                        .Select(g => g.Address.ToString()).Distinct().ToList(),
                    properties.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                        .Select(d => d.ToString()).Distinct().ToList());
            })
            .Where(a => a.IpAddresses.Count > 0)
            .OrderBy(a => a.Name)
            .ToList();
    }

    private static string FormatMacAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "non disponibile" : string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string FormatLine(string label, string value) => $"{label.PadRight(16)}: {value}";

    private static async Task<string> GetPublicIpAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("InfoPC-Tray/1.0");
            var value = (await client.GetStringAsync("https://api.ipify.org")).Trim();
            return IPAddress.TryParse(value, out _) ? value : "risposta non valida";
        }
        catch
        {
            return "non disponibile (controllare Internet)";
        }
    }

    private static string FormatPorts(int[] ports)
    {
        if (ports.Length == 0) return "Nessuna";
        const int perLine = 12;
        return string.Join("\r\n", ports.Chunk(perLine).Select(chunk => string.Join(", ", chunk)));
    }

    private sealed record NetworkAdapterInfo(
        string Name,
        string Description,
        bool DhcpEnabled,
        string MacAddress,
        List<string> IpAddresses,
        List<string> Gateways,
        List<string> DnsServers);
}
