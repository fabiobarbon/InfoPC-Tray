using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Management;
using LibreHardwareMonitor.Hardware;
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
    private readonly RichTextBox networkOutput;
    private readonly RichTextBox hardwareOutput;
    private readonly TabControl pages;
    private readonly HardwareInfoProvider hardwareProvider;
    private readonly System.Windows.Forms.Timer hardwareTimer;
    private bool exiting;

    public InfoForm()
    {
        Text = "InfoPC Tray - Informazioni di sistema";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(616, 486);
        Size = new Size(757, 630);
        ShowIcon = true;

        var title = new Label
        {
            Text = "INFORMAZIONI DEL COMPUTER, DELLA RETE E DELL'HARDWARE",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold)
        };

        networkOutput = CreateOutputBox();
        hardwareOutput = CreateOutputBox();
        pages = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        pages.TabPages.Add(CreatePage("Rete", networkOutput));
        pages.TabPages.Add(CreatePage("Hardware in tempo reale", hardwareOutput));

        var copyButton = new Button { Text = "Copia tutto", AutoSize = true };
        copyButton.Click += (_, _) => Clipboard.SetText(pages.SelectedIndex == 0 ? networkOutput.Text : hardwareOutput.Text);
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
        Controls.Add(pages);
        Controls.Add(bottomBar);
        Controls.Add(title);

        hardwareProvider = new HardwareInfoProvider();
        hardwareTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        hardwareTimer.Tick += (_, _) => RefreshHardware();
        hardwareTimer.Start();
        RefreshHardware();

        FormClosing += (_, e) =>
        {
            if (!exiting)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    private static RichTextBox CreateOutputBox() => new()
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

    private static TabPage CreatePage(string title, RichTextBox box)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(11, 11, 0, 0) };
        page.Controls.Add(box);
        return page;
    }

    public void SetLoading()
    {
        networkOutput.Clear();
        networkOutput.SelectionColor = Color.DimGray;
        networkOutput.AppendText("Lettura delle informazioni in corso...");
    }

    public void SetText(string text)
    {
        SetBoxText(networkOutput, text);
        HighlightLine(networkOutput, "Nome PC", Color.FromArgb(0, 90, 170));
        HighlightAllLines(networkOutput, "Indirizzo IP", Color.FromArgb(0, 90, 170), FontStyle.Bold);
        HighlightAllLines(networkOutput, "DETTAGLI SCHEDE DI RETE", Color.FromArgb(35, 35, 35), FontStyle.Bold | FontStyle.Underline);
        HighlightAllLines(networkOutput, "PORTE LOCALI IN ASCOLTO", Color.FromArgb(35, 35, 35), FontStyle.Bold | FontStyle.Underline);
        networkOutput.Select(0, 0);
    }

    private void RefreshHardware()
    {
        var text = hardwareProvider.GetReport();
        SetBoxText(hardwareOutput, text);
        foreach (var section in new[] { "CPU", "GPU", "NPU", "MEMORIA RAM", "DISCHI" })
            HighlightAllLines(hardwareOutput, section.PadRight(82), Color.FromArgb(35, 35, 35), FontStyle.Bold | FontStyle.Underline);
        HighlightAllLines(hardwareOutput, "Temperatura", Color.FromArgb(0, 120, 60), FontStyle.Bold);
        hardwareOutput.Select(0, 0);
    }

    private static void SetBoxText(RichTextBox box, string text)
    {
        var firstVisible = box.GetCharIndexFromPosition(new Point(1, 1));
        box.Clear();
        box.Text = text;
        box.SelectAll();
        box.SelectionFont = new Font("Consolas", 10.5f, FontStyle.Regular);
        box.SelectionColor = Color.FromArgb(35, 35, 35);
        box.Select(Math.Min(firstVisible, box.TextLength), 0);
        box.ScrollToCaret();
    }

    private static void HighlightLine(RichTextBox box, string label, Color color)
    {
        var start = box.Text.IndexOf(label, StringComparison.Ordinal);
        if (start < 0) return;
        var end = box.Text.IndexOf('\n', start);
        if (end < 0) end = box.Text.Length;
        box.Select(start, end - start);
        box.SelectionFont = new Font("Consolas", 10.5f, FontStyle.Bold);
        box.SelectionColor = color;
    }

    private static void HighlightAllLines(RichTextBox box, string label, Color color, FontStyle style)
    {
        var searchFrom = 0;
        while (searchFrom < box.Text.Length)
        {
            var start = box.Text.IndexOf(label, searchFrom, StringComparison.Ordinal);
            if (start < 0) break;
            var end = box.Text.IndexOf('\n', start);
            if (end < 0) end = box.Text.Length;
            box.Select(start, end - start);
            box.SelectionFont = new Font("Consolas", 10.5f, style);
            box.SelectionColor = color;
            searchFrom = end + 1;
        }
    }

    public void CloseForExit()
    {
        exiting = true;
        hardwareTimer.Stop();
        hardwareTimer.Dispose();
        hardwareProvider.Dispose();
        Close();
    }
}

internal sealed class HardwareInfoProvider : IDisposable
{
    private readonly Computer computer;
    private readonly List<RamModuleInfo> ramModules;
    private readonly List<DiskInfo> disks;
    private readonly List<string> npuDevices;
    private readonly string? initializationError;

    public HardwareInfoProvider()
    {
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true
            };
            computer.Open();
        }
        catch (Exception ex)
        {
            computer = new Computer();
            initializationError = ex.Message;
        }

        ramModules = ReadRamModules();
        disks = ReadDisks();
        npuDevices = ReadNpuDevices();
    }

    public string GetReport()
    {
        try
        {
            UpdateHardware();
            var allHardware = FlattenHardware().ToList();
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Dati aggiornati in tempo reale ogni 2 secondi{new string(' ', 20)}{DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(initializationError))
                report.AppendLine($"Sensori hardware non inizializzati: {initializationError}");
            report.AppendLine();

            AppendHardwareSection(report, "CPU", allHardware.Where(h => h.HardwareType == HardwareType.Cpu));
            AppendHardwareSection(report, "GPU", allHardware.Where(h =>
                h.HardwareType == HardwareType.GpuAmd ||
                h.HardwareType == HardwareType.GpuIntel ||
                h.HardwareType == HardwareType.GpuNvidia));
            AppendNpuSection(report, allHardware);
            AppendRamSection(report, allHardware);
            AppendDiskSection(report, allHardware);
            return report.ToString();
        }
        catch (Exception ex)
        {
            return $"Impossibile leggere i dati hardware.\r\n\r\nDettaglio: {ex.Message}";
        }
    }

    private void UpdateHardware()
    {
        foreach (var hardware in computer.Hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Update();
        }
    }

    private IEnumerable<IHardware> FlattenHardware()
    {
        foreach (var hardware in computer.Hardware)
        {
            yield return hardware;
            foreach (var subHardware in hardware.SubHardware)
                yield return subHardware;
        }
    }

    private static void AppendHardwareSection(System.Text.StringBuilder report, string title, IEnumerable<IHardware> items)
    {
        report.AppendLine(title.PadRight(82));
        var hardwareItems = items.ToList();
        if (hardwareItems.Count == 0)
        {
            report.AppendLine("Non rilevata oppure sensori non disponibili.");
            report.AppendLine();
            return;
        }

        foreach (var hardware in hardwareItems)
        {
            report.AppendLine(FormatLine("Descrizione", hardware.Name));
            report.AppendLine(FormatLine("Temperatura", FormatTemperature(hardware)));
            var load = hardware.Sensors
                .Where(s => s.SensorType == SensorType.Load && s.Value.HasValue)
                .OrderByDescending(s => s.Value)
                .FirstOrDefault();
            if (load is not null)
                report.AppendLine(FormatLine("Utilizzo", $"{load.Value:0.0}% ({load.Name})"));
            report.AppendLine();
        }
    }

    private void AppendNpuSection(System.Text.StringBuilder report, List<IHardware> hardware)
    {
        report.AppendLine("NPU".PadRight(82));
        var sensorNpus = hardware.Where(h =>
            h.Name.Contains("NPU", StringComparison.OrdinalIgnoreCase) ||
            h.Name.Contains("Neural", StringComparison.OrdinalIgnoreCase)).ToList();
        var names = npuDevices.Concat(sensorNpus.Select(h => h.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            report.AppendLine("Non presente oppure non rilevabile da Windows.");
        }
        else
        {
            foreach (var name in names)
            {
                report.AppendLine(FormatLine("Descrizione", name));
                var match = sensorNpus.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                report.AppendLine(FormatLine("Temperatura", match is null ? "non disponibile" : FormatTemperature(match)));
            }
        }
        report.AppendLine();
    }

    private void AppendRamSection(System.Text.StringBuilder report, List<IHardware> hardware)
    {
        report.AppendLine("MEMORIA RAM".PadRight(82));
        var totalBytes = ramModules.Sum(m => m.CapacityBytes);
        var freeBytes = ReadFreeMemoryBytes();
        report.AppendLine(FormatLine("Quantita'", ramModules.Count == 0 ? "moduli non rilevati" : $"{ramModules.Count} moduli"));
        report.AppendLine(FormatLine("Totale", totalBytes == 0 ? "non disponibile" : FormatBytes(totalBytes)));
        report.AppendLine(FormatLine("Disponibile", freeBytes == 0 ? "non disponibile" : FormatBytes(freeBytes)));

        var memoryHardware = hardware.Where(h => h.HardwareType == HardwareType.Memory).ToList();
        var memoryTemperature = memoryHardware.SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .OrderByDescending(s => s.Value).FirstOrDefault();
        report.AppendLine(FormatLine("Temperatura", memoryTemperature is null ? "non disponibile" : $"{memoryTemperature.Value:0.0} °C ({memoryTemperature.Name})"));

        for (var i = 0; i < ramModules.Count; i++)
        {
            var module = ramModules[i];
            var description = $"{FormatBytes(module.CapacityBytes)} {module.Type}";
            if (module.SpeedMhz > 0) description += $" {module.SpeedMhz} MHz";
            if (!string.IsNullOrWhiteSpace(module.Manufacturer)) description += $" - {module.Manufacturer}";
            report.AppendLine(FormatLine($"Modulo {i + 1}", description));
        }
        report.AppendLine();
    }

    private void AppendDiskSection(System.Text.StringBuilder report, List<IHardware> hardware)
    {
        report.AppendLine("DISCHI".PadRight(82));
        var storageSensors = hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
        if (disks.Count == 0 && storageSensors.Count == 0)
        {
            report.AppendLine("Nessun disco rilevato.");
            return;
        }

        var diskList = disks.Count > 0
            ? disks
            : storageSensors.Select(h => new DiskInfo(h.Name, 0, "tipo non disponibile", "non disponibile")).ToList();

        for (var i = 0; i < diskList.Count; i++)
        {
            var disk = diskList[i];
            var sensor = FindStorageHardware(disk.Name, storageSensors, i);
            report.AppendLine(FormatLine($"Disco {i + 1}", disk.Name));
            report.AppendLine(FormatLine("Capacita'", disk.SizeBytes == 0 ? "non disponibile" : FormatBytes(disk.SizeBytes)));
            report.AppendLine(FormatLine("Tipo", disk.Type));
            report.AppendLine(FormatLine("Stato", disk.Health));
            report.AppendLine(FormatLine("Temperatura", sensor is null ? "non disponibile" : FormatTemperature(sensor)));
            report.AppendLine();
        }
    }

    private static IHardware? FindStorageHardware(string diskName, List<IHardware> sensors, int index)
    {
        var words = diskName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).ToArray();
        var match = sensors.FirstOrDefault(h => words.Any(w => h.Name.Contains(w, StringComparison.OrdinalIgnoreCase)));
        return match ?? (index < sensors.Count ? sensors[index] : null);
    }

    private static string FormatTemperature(IHardware hardware)
    {
        var temperatures = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .OrderByDescending(s => s.Value).ToList();
        if (temperatures.Count == 0) return "non disponibile";
        var sensor = temperatures[0];
        return $"{sensor.Value:0.0} °C ({sensor.Name})";
    }

    private static List<RamModuleInfo> ReadRamModules()
    {
        var result = new List<RamModuleInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity, SMBIOSMemoryType, Speed, Manufacturer FROM Win32_PhysicalMemory");
            foreach (ManagementObject item in searcher.Get())
            {
                result.Add(new RamModuleInfo(
                    ToUInt64(item["Capacity"]),
                    FormatRamType(ToUInt32(item["SMBIOSMemoryType"])),
                    ToUInt32(item["Speed"]),
                    item["Manufacturer"]?.ToString()?.Trim() ?? ""));
            }
        }
        catch { }
        return result;
    }

    private static ulong ReadFreeMemoryBytes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject item in searcher.Get())
                return ToUInt64(item["FreePhysicalMemory"]) * 1024UL;
        }
        catch { }
        return 0;
    }

    private static List<string> ReadNpuDevices()
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity");
            foreach (ManagementObject item in searcher.Get())
            {
                var name = item["Name"]?.ToString() ?? "";
                if (name.Contains("NPU", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Neural Processing", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("AI Boost", StringComparison.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<DiskInfo> ReadDisks()
    {
        var result = new List<DiskInfo>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, Size, MediaType, HealthStatus FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject item in searcher.Get())
            {
                result.Add(new DiskInfo(
                    item["FriendlyName"]?.ToString()?.Trim() ?? "Disco fisico",
                    ToUInt64(item["Size"]),
                    FormatDiskType(ToUInt32(item["MediaType"])),
                    FormatDiskHealth(ToUInt32(item["HealthStatus"]))));
            }
        }
        catch { }

        if (result.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, Size, MediaType, Status FROM Win32_DiskDrive");
                foreach (ManagementObject item in searcher.Get())
                {
                    var name = item["Model"]?.ToString()?.Trim() ?? "Disco fisico";
                    var media = item["MediaType"]?.ToString() ?? "";
                    var type = name.Contains("SSD", StringComparison.OrdinalIgnoreCase) || media.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                        ? "SSD" : "HDD / non determinato";
                    result.Add(new DiskInfo(name, ToUInt64(item["Size"]), type, item["Status"]?.ToString() ?? "non disponibile"));
                }
            }
            catch { }
        }
        return result;
    }

    private static string FormatRamType(uint value) => value switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4",
        30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "tipo non disponibile"
    };

    private static string FormatDiskType(uint value) => value switch
    {
        3 => "HDD meccanico", 4 => "SSD", 5 => "SCM", _ => "tipo non disponibile"
    };

    private static string FormatDiskHealth(uint value) => value switch
    {
        0 => "Integro", 1 => "Attenzione", 2 => "Non integro", 5 => "Sconosciuto", _ => "non disponibile"
    };

    private static string FormatLine(string label, string value) => $"{label.PadRight(16)}: {value}";
    private static string FormatBytes(ulong bytes) => $"{bytes / 1073741824d:0.##} GB";
    private static ulong ToUInt64(object? value) { try { return Convert.ToUInt64(value); } catch { return 0; } }
    private static uint ToUInt32(object? value) { try { return Convert.ToUInt32(value); } catch { return 0; } }

    public void Dispose()
    {
        try { computer.Close(); } catch { }
    }

    private sealed record RamModuleInfo(ulong CapacityBytes, string Type, uint SpeedMhz, string Manufacturer);
    private sealed record DiskInfo(string Name, ulong SizeBytes, string Type, string Health);
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
        report.AppendLine(FormatNameAndDate(Environment.MachineName, DateTime.Now));
        report.AppendLine(FormatLine("Utente", Environment.UserName));
        report.AppendLine(FormatLine("IP pubblico", publicIp));

        report.AppendLine();
        report.AppendLine("DETTAGLI SCHEDE DI RETE".PadRight(82));
        if (adapters.Count == 0)
        {
            report.AppendLine("Nessuna scheda di rete rilevata.");
        }
        else
        {
            foreach (var adapter in adapters)
            {
                report.AppendLine(FormatLine("Descrizione", $"{adapter.Name} - {adapter.Description}"));
                report.AppendLine(FormatLine("Stato", adapter.IsConnected ? "Connesso" : "Non connesso"));
                report.AppendLine(FormatIpAndDhcp(
                    adapter.IpAddresses.Count == 0 ? "non disponibile" : string.Join(", ", adapter.IpAddresses),
                    adapter.DhcpEnabled));
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
            .Where(IsPhysicalAdapter)
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
                    n.OperationalStatus == OperationalStatus.Up,
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
            .OrderByDescending(a => a.IsConnected)
            .ThenBy(a => a.Name)
            .ToList();
    }

    private static bool IsPhysicalAdapter(NetworkInterface adapter)
    {
        var physicalTypes = new[]
        {
            NetworkInterfaceType.Ethernet,
            NetworkInterfaceType.GigabitEthernet,
            NetworkInterfaceType.FastEthernetFx,
            NetworkInterfaceType.FastEthernetT,
            NetworkInterfaceType.Wireless80211
        };
        if (!physicalTypes.Contains(adapter.NetworkInterfaceType)) return false;

        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        string[] virtualMarkers =
        {
            "virtual", "vethernet", "hyper-v", "vmware", "virtualbox",
            "vpn", " tap", "tun", "loopback", "npcap", "miniport",
            "bluetooth", "container", "docker", "wsl"
        };
        return !virtualMarkers.Any(text.Contains);
    }

    private static string FormatMacAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "non disponibile" : string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string FormatLine(string label, string value) => $"{label.PadRight(16)}: {value}";

    private static string FormatIpAndDhcp(string ipAddress, bool dhcpEnabled)
    {
        var left = FormatLine("Indirizzo IP", ipAddress);
        var right = $"DHCP: {(dhcpEnabled ? "Automatico" : "Manuale")}";
        var spaces = Math.Max(2, 82 - left.Length - right.Length);
        return left + new string(' ', spaces) + right;
    }

    private static string FormatNameAndDate(string computerName, DateTime dateTime)
    {
        var left = FormatLine("Nome PC", computerName);
        var right = dateTime.ToString("dd/MM/yyyy HH:mm:ss");
        var spaces = Math.Max(2, 82 - left.Length - right.Length);
        return left + new string(' ', spaces) + right;
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

    private sealed record NetworkAdapterInfo(
        string Name,
        string Description,
        bool IsConnected,
        bool DhcpEnabled,
        string MacAddress,
        List<string> IpAddresses,
        List<string> Gateways,
        List<string> DnsServers);
}
