using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples.UI.WinForms;

/// <summary>
///     Hand-built (no designer) so every line of wiring is visible. Left half: the zero-code
///     settings dialog — <c>PropertyGrid.SelectedObject = settings</c>; every edit committed in
///     the grid runs a woven setter and autosaves. Right half: the same properties through
///     classic <c>BindingSource</c> data binding. Editing either side updates the other live,
///     because both listen to the <c>INotifyPropertyChanged</c> that <c>[NotifyChangesMixin]</c>
///     injected into <see cref="AppSettings"/> at build time.
/// </summary>
public sealed class MainForm : Form {
    private readonly AppSettings _settings = AppSettings.Instance;
    private readonly VaultSettings _vault = VaultSettings.Instance;
    private readonly BindingSource _binding;
    private readonly PropertyGrid _grid;
    private readonly ToolStripStatusLabel _saveLabel;
    private readonly TextBox _serverBox;
    private readonly TextBox _portBox;
    private readonly TextBox _rawBox;
    private int _saveCount;

    public MainForm() {
        Text = "Nucs.JsonSettings — WinForms";
        MinimumSize = new Size(760, 620);
        ClientSize = new Size(880, 660);

        // ---- left: the zero-code settings dialog -------------------------------------------
        _grid = new PropertyGrid {
            Dock = DockStyle.Fill,
            SelectedObject = _settings,
            HelpVisible = true,
        };

        // ---- right: classic two-way data binding over the SAME instance --------------------
        _binding = new BindingSource { DataSource = _settings };

        var nameBox = new TextBox { Dock = DockStyle.Fill };
        nameBox.DataBindings.Add(nameof(TextBox.Text), _binding, nameof(AppSettings.DisplayName),
                                 false, DataSourceUpdateMode.OnPropertyChanged);

        var emailBox = new TextBox { Dock = DockStyle.Fill };
        emailBox.DataBindings.Add(nameof(TextBox.Text), _binding, nameof(AppSettings.Email),
                                  false, DataSourceUpdateMode.OnPropertyChanged);

        var syncCheck = new CheckBox { Text = "Enable sync", Dock = DockStyle.Fill };
        syncCheck.DataBindings.Add(nameof(CheckBox.Checked), _binding, nameof(AppSettings.EnableSync),
                                   false, DataSourceUpdateMode.OnPropertyChanged);

        var minutesUpDown = new NumericUpDown { Minimum = 1, Maximum = 1440, Dock = DockStyle.Fill };
        // formattingEnabled: true lets the binding convert between the control's decimal and the
        // property's int.
        minutesUpDown.DataBindings.Add(nameof(NumericUpDown.Value), _binding, nameof(AppSettings.RefreshMinutes),
                                       true, DataSourceUpdateMode.OnPropertyChanged);

        // ---- batched apply: many writes, one save ------------------------------------------
        var serverBox = new TextBox { Dock = DockStyle.Fill, Text = _settings.Server };
        var portBox = new TextBox { Dock = DockStyle.Fill, Text = _settings.Port.ToString() };
        var applyBtn = new Button { Text = "Apply server settings (one save)", Dock = DockStyle.Fill };
        applyBtn.Click += OnApplyServerSettings;
        _serverBox = serverBox;
        _portBox = portBox;

        // ---- encrypted vault: the same grid UX over a ciphertext file ----------------------
        var vaultGrid = new PropertyGrid {
            Dock = DockStyle.Fill,
            SelectedObject = _vault,
            HelpVisible = false,
            ToolbarVisible = false,
        };

        var rawBtn = new Button { Text = "Show raw files on disk (plain vs encrypted)", Dock = DockStyle.Fill };
        rawBtn.Click += OnShowRawFiles;
        _rawBox = new TextBox {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 8.25f),
            Text = "click the button above after editing something on either grid",
        };

        var right = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 13,
        };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(right, "BindingSource, two-way:", null, bold: true);
        AddRow(right, "Display name", nameBox);
        AddRow(right, "E-mail", emailBox);
        AddRow(right, "", syncCheck);
        AddRow(right, "Refresh (min)", minutesUpDown);
        AddRow(right, "Batched (SuspendAutosave):", null, bold: true);
        AddRow(right, "Server", serverBox);
        AddRow(right, "Port", portBox);
        AddRow(right, "", applyBtn);
        AddRow(right, "Encrypted vault (WithEncryption):", null, bold: true);
        AddRow(right, "", vaultGrid, rowHeight: 130);
        AddRow(right, "", rawBtn);
        AddRow(right, "", _rawBox, rowHeight: 120);

        var split = new SplitContainer {
            Dock = DockStyle.Fill,
            SplitterDistance = 380,
            FixedPanel = FixedPanel.None,
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(right);

        _saveLabel = new ToolStripStatusLabel("not saved yet");
        var status = new StatusStrip();
        status.Items.Add(_saveLabel);

        Controls.Add(split);
        Controls.Add(status);

        // ---- the glue both halves share ----------------------------------------------------

        // AppSettings does not implement INotifyPropertyChanged in SOURCE — [NotifyChangesMixin]
        // weaves it in — so plain code casts through object to subscribe. The bound controls need
        // no such cast: BindingSource discovers the interface on the instance by itself.
        ((INotifyPropertyChanged)(object)_settings).PropertyChanged += (_, e) => {
            _grid.Refresh();   // reflect edits made through the bound controls
            if (e.PropertyName == nameof(AppSettings.DisplayName))
                UpdateTitle();
        };

        // Every write above also SAVED (the [Autosave] weave); count them to make the batching
        // demo measurable. All writes in this demo happen on the UI thread, so no marshalling.
        // Both settings objects feed the same counter — the destination path tells them apart,
        // and a vault edit visibly saves the ENCRYPTED file.
        _settings.AfterSave += (_, destination) =>
            _saveLabel.Text = $"save #{++_saveCount} → {destination}";
        _vault.AfterSave += (_, destination) =>
            _saveLabel.Text = $"save #{++_saveCount} → {destination} (encrypted)";

        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = string.IsNullOrWhiteSpace(_settings.DisplayName)
            ? "Nucs.JsonSettings — WinForms"
            : $"Nucs.JsonSettings — WinForms — {_settings.DisplayName}";

    private void OnApplyServerSettings(object? sender, EventArgs e) {
        if (!int.TryParse(_portBox.Text, out var port) || port is < 1 or > 65535) {
            MessageBox.Show(this, "Port must be a number between 1 and 65535.", "Invalid port");
            return;
        }

        // Two woven setters, ONE file save: SuspendAutosave() batches until the scope closes.
        // Watch the save counter in the status bar go up by exactly one.
        using (_settings.SuspendAutosave()) {
            _settings.Server = _serverBox.Text;
            _settings.Port = port;
        }
    }

    private void OnShowRawFiles(object? sender, EventArgs e) {
        // The point of the whole vault demo, made visible: two files written by the SAME code
        // path (a woven setter followed by a module pipeline), one readable, one ciphertext.
        static string Preview(string path) {
            if (!File.Exists(path))
                return "(no file yet — change something first)";
            var bytes = File.ReadAllBytes(path);
            var head = new byte[Math.Min(bytes.Length, 64)];
            Array.Copy(bytes, head, head.Length);
            var looksText = head.All(b => b is 9 or 10 or 13 or (>= 32 and < 127));
            return looksText
                ? Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 220)).Replace("\r", " ").Replace("\n", " ")
                : BitConverter.ToString(head).Replace("-", " ") + " …";
        }

        _rawBox.Text = $"plain  {_settings.FileName}:\r\n{Preview(_settings.FileName)}\r\n\r\n" +
                       $"vault  {_vault.FileName}:\r\n{Preview(_vault.FileName)}";
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control? control, bool bold = false, int rowHeight = 0) {
        var row = panel.RowStyles.Count;   // rows are appended in order, so the next index is the count
        panel.RowStyles.Add(rowHeight > 0 ? new RowStyle(SizeType.Absolute, rowHeight) : new RowStyle(SizeType.AutoSize));
        var lbl = new Label {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
        };
        if (bold)
            lbl.Font = new Font(lbl.Font, FontStyle.Bold);
        if (control is null) {
            panel.Controls.Add(lbl, 0, row);
            panel.SetColumnSpan(lbl, 2);
        } else {
            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(control, 1, row);
        }
    }
}
