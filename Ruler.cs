using System.Runtime.InteropServices;
using System.Text.Json;

namespace InfoPCTray;

internal enum RulerUnit { Pixels, Millimeters }
internal enum RulerOrientation { Horizontal, Vertical }
internal enum RulerZeroPosition { Start, Center }

internal sealed class RulerSettings
{
    public RulerUnit Unit { get; set; } = RulerUnit.Pixels;
    public decimal Length { get; set; } = 800;
    public int OpacityPercent { get; set; } = 85;
    public decimal CalibratedDpi { get; set; } = 96;
    public RulerOrientation Ruler1Orientation { get; set; } = RulerOrientation.Horizontal;
    public RulerOrientation Ruler2Orientation { get; set; } = RulerOrientation.Vertical;
    public RulerZeroPosition Ruler1ZeroPosition { get; set; } = RulerZeroPosition.Start;
    public RulerZeroPosition Ruler2ZeroPosition { get; set; } = RulerZeroPosition.Start;
    public Point Ruler1Position { get; set; } = new(100, 100);
    public Point Ruler2Position { get; set; } = new(160, 160);
}

internal sealed class RulerManager : IDisposable
{
    private readonly RulerForm?[] rulers = new RulerForm?[2];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InfoPC-Tray", "ruler-settings.json");

    public RulerSettings Settings { get; private set; } = Load();

    public void SetVisible(int index, bool visible)
    {
        if (visible)
        {
            rulers[index] ??= CreateRuler(index);
            rulers[index]!.Apply(Settings);
            rulers[index]!.Show();
            rulers[index]!.BringToFront();
        }
        else
        {
            rulers[index]?.Hide();
        }
    }

    public void ApplySettings(RulerSettings settings)
    {
        settings.Ruler1Position = rulers[0]?.Location ?? Settings.Ruler1Position;
        settings.Ruler2Position = rulers[1]?.Location ?? Settings.Ruler2Position;
        Settings = settings;
        foreach (var ruler in rulers)
            ruler?.Apply(Settings);
        Save();
    }

    private RulerForm CreateRuler(int index)
    {
        var form = new RulerForm(index);
        form.SettingsChanged += (_, updated) => ApplySettings(updated);
        form.LocationChanged += (_, _) =>
        {
            if (!form.Visible) return;
            if (index == 0) Settings.Ruler1Position = form.Location;
            else Settings.Ruler2Position = form.Location;
        };
        return form;
    }

    private static RulerSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<RulerSettings>(File.ReadAllText(SettingsPath)) ?? new RulerSettings();
        }
        catch { return new RulerSettings(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Dispose()
    {
        if (rulers[0] is not null) Settings.Ruler1Position = rulers[0]!.Location;
        if (rulers[1] is not null) Settings.Ruler2Position = rulers[1]!.Location;
        Save();
        foreach (var ruler in rulers) ruler?.Dispose();
    }
}

internal sealed class RulerForm : Form
{
    private readonly int index;
    private RulerSettings settings = new();
    private RulerOrientation orientation;
    private float pixelsPerUnit;
    private readonly ToolStripMenuItem pixelsItem;
    private readonly ToolStripMenuItem millimetersItem;
    private readonly ToolStripMenuItem zeroStartItem;
    private readonly ToolStripMenuItem zeroCenterItem;
    private readonly TrackBar opacitySlider;
    private Point? hoverPoint;
    private int extensionPixels;

    public event EventHandler<RulerSettings>? SettingsChanged;

    public RulerForm(int index)
    {
        this.index = index;
        Text = $"Righello {index + 1}";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Cursor = Cursors.Default;
        MouseDown += BeginDrag;
        MouseMove += TrackMouse;
        MouseLeave += (_, _) => { hoverPoint = null; Invalidate(); };

        var menu = new ContextMenuStrip { AutoSize = true };
        var unitMenu = new ToolStripMenuItem("Unità di misura");
        pixelsItem = new ToolStripMenuItem("Pixel") { CheckOnClick = false };
        millimetersItem = new ToolStripMenuItem("Millimetri") { CheckOnClick = false };
        pixelsItem.Click += (_, _) => ChangeUnit(RulerUnit.Pixels);
        millimetersItem.Click += (_, _) => ChangeUnit(RulerUnit.Millimeters);
        unitMenu.DropDownItems.Add(pixelsItem);
        unitMenu.DropDownItems.Add(millimetersItem);
        menu.Items.Add(unitMenu);

        var zeroMenu = new ToolStripMenuItem("Posizione dello zero");
        zeroStartItem = new ToolStripMenuItem("A sinistra / in alto") { CheckOnClick = false };
        zeroCenterItem = new ToolStripMenuItem("Al centro") { CheckOnClick = false };
        zeroStartItem.Click += (_, _) => ChangeZeroPosition(RulerZeroPosition.Start);
        zeroCenterItem.Click += (_, _) => ChangeZeroPosition(RulerZeroPosition.Center);
        zeroMenu.DropDownItems.Add(zeroStartItem);
        zeroMenu.DropDownItems.Add(zeroCenterItem);
        menu.Items.Add(zeroMenu);

        var opacityLabel = new ToolStripLabel("Trasparenza")
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(5, 5, 5, 0)
        };
        menu.Items.Add(opacityLabel);
        opacitySlider = new TrackBar
        {
            Minimum = 20,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Width = 190,
            Height = 42,
            AutoSize = false
        };
        opacitySlider.ValueChanged += (_, _) =>
        {
            settings.OpacityPercent = opacitySlider.Value;
            Opacity = opacitySlider.Value / 100d;
        };
        opacitySlider.MouseUp += (_, _) => SettingsChanged?.Invoke(this, settings);
        menu.Items.Add(new ToolStripControlHost(opacitySlider)
        {
            AutoSize = false,
            Size = new Size(200, 46),
            Margin = new Padding(4, 0, 4, 3)
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ruota righello", null, (_, _) =>
        {
            orientation = orientation == RulerOrientation.Horizontal
                ? RulerOrientation.Vertical : RulerOrientation.Horizontal;
            if (index == 0) settings.Ruler1Orientation = orientation;
            else settings.Ruler2Orientation = orientation;
            Apply(settings);
            SettingsChanged?.Invoke(this, settings);
        });
        menu.Items.Add("Nascondi", null, (_, _) => Hide());
        menu.Opening += (_, _) => RefreshContextMenu();
        ContextMenuStrip = menu;
    }

    public void Apply(RulerSettings value)
    {
        settings = value;
        orientation = index == 0 ? value.Ruler1Orientation : value.Ruler2Orientation;
        var dpi = Math.Max(20f, (float)value.CalibratedDpi);
        pixelsPerUnit = value.Unit == RulerUnit.Pixels ? 1f : dpi / 25.4f;
        extensionPixels = Math.Max(5, (int)Math.Round(dpi / 25.4f * 5f));
        var lengthPixels = Math.Clamp((int)Math.Round((float)value.Length * pixelsPerUnit), 80, 12000);
        Size = orientation == RulerOrientation.Horizontal
            ? new Size(lengthPixels, 66 + extensionPixels)
            : new Size(66 + extensionPixels, lengthPixels);
        Opacity = Math.Clamp(value.OpacityPercent / 100d, 0.2d, 1d);
        Location = index == 0 ? value.Ruler1Position : value.Ruler2Position;
        Invalidate();
    }

    private void ChangeUnit(RulerUnit newUnit)
    {
        if (settings.Unit == newUnit) return;
        settings.Unit = newUnit;
        // Offre una lunghezza iniziale sensata quando si passa da una scala all'altra.
        settings.Length = newUnit == RulerUnit.Millimeters
            ? Math.Clamp(Math.Round(settings.Length * 25.4M / settings.CalibratedDpi, 1), 10, 10000)
            : Math.Clamp(Math.Round(settings.Length * settings.CalibratedDpi / 25.4M, 0), 10, 10000);
        SettingsChanged?.Invoke(this, settings);
    }

    private void ChangeZeroPosition(RulerZeroPosition position)
    {
        if (index == 0)
            settings.Ruler1ZeroPosition = position;
        else
            settings.Ruler2ZeroPosition = position;
        Invalidate();
        SettingsChanged?.Invoke(this, settings);
    }

    private void RefreshContextMenu()
    {
        pixelsItem.Checked = settings.Unit == RulerUnit.Pixels;
        millimetersItem.Checked = settings.Unit == RulerUnit.Millimeters;
        var zeroPosition = index == 0 ? settings.Ruler1ZeroPosition : settings.Ruler2ZeroPosition;
        zeroStartItem.Checked = zeroPosition == RulerZeroPosition.Start;
        zeroCenterItem.Checked = zeroPosition == RulerZeroPosition.Center;
        var value = Math.Clamp(settings.OpacityPercent, opacitySlider.Minimum, opacitySlider.Maximum);
        if (opacitySlider.Value != value) opacitySlider.Value = value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var horizontal = orientation == RulerOrientation.Horizontal;
        var length = horizontal ? ClientSize.Width : ClientSize.Height;
        var bodyOrigin = extensionPixels;
        var bodyRectangle = horizontal
            ? new Rectangle(0, bodyOrigin, ClientSize.Width, 66)
            : new Rectangle(bodyOrigin, 0, 66, ClientSize.Height);
        var unitStep = settings.Unit == RulerUnit.Pixels ? 5f : 1f;
        var maximumUnits = length / pixelsPerUnit;
        using var linePen = new Pen(Color.FromArgb(45, 45, 45), 1);
        using var borderPen = new Pen(Color.FromArgb(70, 70, 70), 1);
        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
        using var titleFont = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);

        using var rulerBrush = new SolidBrush(Color.FromArgb(255, 244, 184));
        e.Graphics.FillRectangle(rulerBrush, bodyRectangle);
        e.Graphics.DrawRectangle(borderPen, bodyRectangle.X, bodyRectangle.Y,
            bodyRectangle.Width - 1, bodyRectangle.Height - 1);
        var unitName = settings.Unit == RulerUnit.Pixels ? "px" : "mm";

        var zeroPosition = index == 0 ? settings.Ruler1ZeroPosition : settings.Ruler2ZeroPosition;
        var centerOffset = zeroPosition == RulerZeroPosition.Center ? length / 2f : 0f;
        var firstValue = zeroPosition == RulerZeroPosition.Center
            ? -(float)Math.Floor(maximumUnits / 2f / unitStep) * unitStep
            : 0f;
        var lastValue = zeroPosition == RulerZeroPosition.Center
            ? (float)Math.Floor(maximumUnits / 2f / unitStep) * unitStep
            : maximumUnits;

        for (float value = firstValue; value <= lastValue; value += unitStep)
        {
            var coordinate = (int)Math.Round(centerOffset + value * pixelsPerUnit);
            var integerValue = (int)Math.Round(value);
            var majorEvery = settings.Unit == RulerUnit.Pixels ? 50 : 10;
            var mediumEvery = settings.Unit == RulerUnit.Pixels ? 10 : 5;
            var isMajor = integerValue % majorEvery == 0;
            var isMedium = integerValue % mediumEvery == 0;
            var tick = isMajor ? 28 : isMedium ? 18 : 10;

            if (horizontal)
            {
                e.Graphics.DrawLine(linePen, coordinate, bodyOrigin, coordinate, bodyOrigin + tick);
                if (isMajor)
                    e.Graphics.DrawString(integerValue.ToString(), labelFont, Brushes.Black, coordinate + 2, bodyOrigin + 27);
            }
            else
            {
                e.Graphics.DrawLine(linePen, bodyOrigin, coordinate, bodyOrigin + tick, coordinate);
                if (isMajor)
                    e.Graphics.DrawString(integerValue.ToString(), labelFont, Brushes.Black, bodyOrigin + 28, coordinate + 1);
            }
        }

        DrawMouseIndicator(e.Graphics, horizontal, bodyOrigin, length, unitName, titleFont);
        DrawApplicationLabel(e.Graphics, horizontal, bodyOrigin, length, labelFont);
    }

    private void DrawMouseIndicator(Graphics graphics, bool horizontal, int bodyOrigin, int length,
        string unitName, Font font)
    {
        if (hoverPoint is null)
        {
            graphics.DrawString($"R{index + 1}  {unitName}", font, Brushes.Black,
                horizontal ? 4 : bodyOrigin + 4, horizontal ? bodyOrigin + 45 : 4);
            return;
        }

        var axisCoordinate = horizontal ? hoverPoint.Value.X : hoverPoint.Value.Y;
        axisCoordinate = Math.Clamp(axisCoordinate, 0, Math.Max(0, length - 1));
        var zeroPosition = index == 0 ? settings.Ruler1ZeroPosition : settings.Ruler2ZeroPosition;
        var zeroCoordinate = zeroPosition == RulerZeroPosition.Center ? length / 2f : 0f;
        var measuredValue = (axisCoordinate - zeroCoordinate) / pixelsPerUnit;
        var valueText = settings.Unit == RulerUnit.Pixels
            ? $"R{index + 1}  {Math.Round(measuredValue):0} px"
            : $"R{index + 1}  {measuredValue:0.0} mm";
        using var guidePen = new Pen(Color.FromArgb(0, 90, 190), 2);
        var textSize = graphics.MeasureString(valueText, font);

        if (horizontal)
        {
            graphics.DrawLine(guidePen, axisCoordinate, 0, axisCoordinate, bodyOrigin + 42);
            var textX = Math.Clamp(axisCoordinate - textSize.Width / 2f, 2f,
                Math.Max(2f, ClientSize.Width - textSize.Width - 2f));
            graphics.DrawString(valueText, font, Brushes.Black, textX, bodyOrigin + 45);
        }
        else
        {
            graphics.DrawLine(guidePen, 0, axisCoordinate, bodyOrigin + 42, axisCoordinate);
            var textY = Math.Clamp(axisCoordinate - textSize.Height / 2f, 2f,
                Math.Max(2f, ClientSize.Height - textSize.Height - 2f));
            graphics.DrawString(valueText, font, Brushes.Black, bodyOrigin + 4, textY);
        }
    }

    private static void DrawApplicationLabel(Graphics graphics, bool horizontal, int bodyOrigin,
        int length, Font font)
    {
        const string text = "InfoPC-Tray v1.3.4";
        using var brush = new SolidBrush(Color.FromArgb(90, 90, 90));
        if (horizontal)
        {
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush, Math.Max(2, length - size.Width - 5), bodyOrigin + 47);
        }
        else
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bodyOrigin + 47, Math.Max(5, length - 5));
            graphics.RotateTransform(-90);
            graphics.DrawString(text, font, brush, 0, 0);
            graphics.Restore(state);
        }
    }

    private void TrackMouse(object? sender, MouseEventArgs e)
    {
        hoverPoint = e.Location;
        Invalidate();
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Cursor = Cursors.SizeAll;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
        Cursor = Cursors.Default;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}

internal sealed class RulerSettingsForm : Form
{
    private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown length = new() { Minimum = 10, Maximum = 10000, DecimalPlaces = 1 };
    private readonly NumericUpDown opacity = new() { Minimum = 20, Maximum = 100 };
    private readonly NumericUpDown dpi = new() { Minimum = 20, Maximum = 1000, DecimalPlaces = 2, Increment = 0.25M };
    private readonly ComboBox orientation1 = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox orientation2 = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RulerSettings original;
    public RulerSettings Result { get; private set; }

    public RulerSettingsForm(RulerSettings current)
    {
        original = current;
        Result = current;
        Text = "Impostazioni righelli";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(410, 330);
        Font = new Font("Segoe UI", 9.5f);

        unit.Items.AddRange(new object[] { "Pixel", "Millimetri" });
        orientation1.Items.AddRange(new object[] { "Orizzontale", "Verticale" });
        orientation2.Items.AddRange(new object[] { "Orizzontale", "Verticale" });
        unit.SelectedIndex = current.Unit == RulerUnit.Pixels ? 0 : 1;
        length.Value = Math.Clamp(current.Length, length.Minimum, length.Maximum);
        opacity.Value = Math.Clamp(current.OpacityPercent, (int)opacity.Minimum, (int)opacity.Maximum);
        dpi.Value = Math.Clamp(current.CalibratedDpi, dpi.Minimum, dpi.Maximum);
        orientation1.SelectedIndex = current.Ruler1Orientation == RulerOrientation.Horizontal ? 0 : 1;
        orientation2.SelectedIndex = current.Ruler2Orientation == RulerOrientation.Horizontal ? 0 : 1;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 235, Padding = new Padding(14),
            ColumnCount = 2, RowCount = 6
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        AddRow(table, 0, "Unità di misura", unit);
        AddRow(table, 1, "Lunghezza", length);
        AddRow(table, 2, "Trasparenza (20–100%)", opacity);
        AddRow(table, 3, "DPI calibrati", dpi);
        AddRow(table, 4, "Righello 1", orientation1);
        AddRow(table, 5, "Righello 2", orientation2);

        var note = new Label
        {
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(14, 3, 14, 3),
            Text = "Per misure reali in millimetri, correggi i DPI confrontando il righello sullo schermo con un righello fisico."
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Applica", AutoSize = true };
        ok.Click += (_, _) => Accept();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(buttons);
        Controls.Add(note);
        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel table, int row, string text, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        table.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
    }

    private void Accept()
    {
        Result = new RulerSettings
        {
            Unit = unit.SelectedIndex == 0 ? RulerUnit.Pixels : RulerUnit.Millimeters,
            Length = length.Value,
            OpacityPercent = (int)opacity.Value,
            CalibratedDpi = dpi.Value,
            Ruler1Orientation = orientation1.SelectedIndex == 0 ? RulerOrientation.Horizontal : RulerOrientation.Vertical,
            Ruler2Orientation = orientation2.SelectedIndex == 0 ? RulerOrientation.Horizontal : RulerOrientation.Vertical,
            Ruler1ZeroPosition = original.Ruler1ZeroPosition,
            Ruler2ZeroPosition = original.Ruler2ZeroPosition,
            Ruler1Position = original.Ruler1Position,
            Ruler2Position = original.Ruler2Position
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
