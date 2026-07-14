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
    private readonly TextBox output;
    private bool exiting;

    public InfoForm()
    {
        Text = "InfoPC Tray - Informazioni di rete";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 480);
        Size = new Size(760, 650);
        ShowIcon = true;
        output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var copyButton = new Button { Text = "Copia tutto", AutoSize = true };
        copyButton.Click += (_, _) => Clipboard.SetText(output.Text);
        var closeButton = new Button { Text = "Chiudi finestra", AutoSize = true };
        closeButton.Click += (_, _) => Hide();
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);
        Controls.Add(output);
        Controls.Add(buttons);
        FormClosing += (_, e) =>
        {
            if (!exiting)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void SetLoading() => output.Text = "Lettura delle informazioni in corso...";
    public void SetText(string text) => output.Text = text;
    public void CloseForExit() { exiting = true; Close(); }
}

internal static class ComputerInfo
{
    public static async Task<string> GetReportAsync()
    {
        var publicIpTask = GetPublicIpAsync();
        var localIps = GetLocalIps();
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpPorts = properties.GetActiveTcpListeners().Select(x => x.Port).Distinct().Order().ToArray();
        var udpPorts = properties.GetActiveUdpListeners().Select(x => x.Port).Distinct().Order().ToArray();
        var publicIp = await publicIpTask;

        return $"Nome PC: {Environment.MachineName}\r\n" +
               $"Utente: {Environment.UserName}\r\n" +
               $"IP locale: {(localIps.Count == 0 ? "non disponibile" : string.Join(", ", localIps))}\r\n" +
               $"IP pubblico: {publicIp}\r\n" +
               $"Aggiornato: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n\r\n" +
               "PORTE LOCALI IN ASCOLTO\r\n" +
               "Queste porte sono aperte sul PC, ma non sono necessariamente esposte su Internet.\r\n\r\n" +
               $"TCP ({tcpPorts.Length}):\r\n{FormatPorts(tcpPorts)}\r\n\r\n" +
               $"UDP ({udpPorts.Length}):\r\n{FormatPorts(udpPorts)}";
    }

    private static List<string> GetLocalIps()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString())
            .Distinct()
            .Order()
            .ToList();
    }

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
}
