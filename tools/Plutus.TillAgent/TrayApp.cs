using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.Win32;

namespace Plutus.TillAgent
{
    /// <summary>
    /// The tray icon + settings window. This is the whole UI: pick a printer, test it, copy the
    /// pairing token into the till, see whether the last print worked.
    /// </summary>
    public sealed class TrayApp : IDisposable
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "PlutusTillAgent";

        private readonly AgentState _state;
        private readonly WebApplication _web;
        private readonly NotifyIcon _icon;
        private SettingsForm? _settings;

        public TrayApp(AgentState state, WebApplication web)
        {
            _state = state;
            _web = web;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
            menu.Items.Add("Test print", null, async (_, _) =>
            {
                await _state.PrintAsync(Program.TestReceipt(_state.Config.Columns));
                Refresh();
                if (_state.LastError != null)
                    MessageBox.Show(_state.LastError, "Test print failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            menu.Items.Add("Open cash drawer", null, async (_, _) => { await _state.KickDrawerAsync(); Refresh(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { _web.StopAsync().GetAwaiter().GetResult(); Application.Exit(); });

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = menu,
            };
            _icon.DoubleClick += (_, _) => ShowSettings();
            _state.Changed += Refresh;
            Refresh();

            // First run has no printer chosen and nothing paired — say so rather than sit silently.
            if (string.IsNullOrWhiteSpace(_state.Config.PrinterName) && string.IsNullOrWhiteSpace(_state.Config.PosDeviceId))
                ShowSettings();
        }

        private void Refresh()
        {
            if (_icon.Container?.Components == null && _icon.Icon == null) return;
            var printer = !string.IsNullOrWhiteSpace(_state.Config.PosDeviceName) ? _state.Config.PosDeviceName
                : !string.IsNullOrWhiteSpace(_state.Config.PrinterName) ? _state.Config.PrinterName
                : "no printer selected";
            var health = _state.LastError != null ? $"ERROR: {_state.LastError}" : "ready";
            // NotifyIcon truncates past 63 chars and throws on longer in some Windows versions.
            var text = $"Plutus Till Agent v{Program.AgentVersion}\n{printer}\n{health}";
            _icon.Text = text.Length > 62 ? text[..62] : text;
        }

        private void ShowSettings()
        {
            if (_settings is { IsDisposed: false }) { _settings.Activate(); return; }
            _settings = new SettingsForm(_state);
            _settings.FormClosed += (_, _) => Refresh();
            _settings.Show();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }

        /// <summary>Auto-start at login via the Run key (per user — no admin needed, and the till
        /// PC logs in automatically anyway).</summary>
        public static bool AutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }

        public static void SetAutoStart(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enabled) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>Deliberately plain: a cashier or a manager on the phone has to be able to work it.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly AgentState _state;
        private readonly ComboBox _printers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly ComboBox _columns = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly ComboBox _emulation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly TextBox _token = new() { Width = 330, ReadOnly = true };
        private readonly TextBox _origin = new() { Width = 330 };
        private readonly CheckBox _autoStart = new() { Text = "Start automatically when this PC logs in", AutoSize = true };
        private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(360, 0) };

        public SettingsForm(AgentState state)
        {
            _state = state;
            Text = $"Plutus Till Agent v{Program.AgentVersion}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 485); // grew for the FE3.1 printer-language row

            var y = 12;
            void Add(Control c, int height = 0) { c.Left = 20; c.Top = y; Controls.Add(c); y += (height > 0 ? height : c.Height) + 8; }

            Add(new Label { Text = "Receipt printer", AutoSize = true });
            foreach (var p in state.Transport.ListPrinters()) _printers.Items.Add(new PrinterChoice(p, null));
            if (!string.IsNullOrWhiteSpace(state.Config.PrinterName) && string.IsNullOrWhiteSpace(state.Config.PosDeviceId))
                SelectChoice(c => c.PosId == null && c.Display == state.Config.PrinterName);
            Add(_printers);
            // FE3.2: POS-mode devices enumerate async; they land at the top marked "Direct" —
            // the queue-less route the NatApp proved against the shop's TSP143.
            _ = LoadPosDevicesAsync();

            Add(new Label { Text = "Paper width", AutoSize = true });
            _columns.Items.AddRange(new object[] { "80mm (42 characters)", "58mm (32 characters)" });
            _columns.SelectedIndex = state.Config.Columns == 32 ? 1 : 0;
            Add(_columns);

            // FE3.1: TSP100-family printers are raster-only; Auto recognises them from the queue
            // name so nobody has to know what an emulation is. Explicit choices for renamed queues.
            Add(new Label { Text = "Printer language", AutoSize = true });
            _emulation.Items.AddRange(new object[]
            {
                "Auto (recommended)",
                "ESC/POS — Epson and most receipt printers",
                "Star raster — TSP100 / TSP113 / TSP143",
            });
            _emulation.SelectedIndex = state.Config.Emulation switch
            {
                Plutus.TillAgent.Core.EmulationResolver.EscPos => 1,
                Plutus.TillAgent.Core.EmulationResolver.StarRasterMode => 2,
                _ => 0,
            };
            Add(_emulation);

            Add(new Label { Text = "Pairing token — type this into the till's Settings → Hardware", AutoSize = true });
            _token.Text = state.Config.Token;
            Add(_token);

            var copy = new Button { Text = "Copy token", Width = 110 };
            copy.Click += (_, _) => { Clipboard.SetText(_state.Config.Token); };
            var regen = new Button { Text = "New token", Width = 110, Left = 140 };
            regen.Click += (_, _) =>
            {
                if (MessageBox.Show("Generate a new token? The till will stop printing until you pair it again.",
                        "New token", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                _state.Config.Token = AgentConfig.NewToken();
                _state.Config.Save();
                _token.Text = _state.Config.Token;
            };
            var row = new Panel { Height = 30, Width = 330 };
            row.Controls.Add(copy);
            row.Controls.Add(regen);
            Add(row, 30);

            Add(new Label { Text = "Till address allowed to connect", AutoSize = true });
            _origin.Text = state.Config.AllowedOrigin;
            Add(_origin);

            _autoStart.Checked = TrayApp.AutoStartEnabled();
            Add(_autoStart);

            var test = new Button { Text = "Test print", Width = 110 };
            test.Click += async (_, _) => { Save(); await _state.PrintAsync(Program.TestReceipt(_state.Config.Columns)); ShowResult("Test print sent."); };
            var drawer = new Button { Text = "Open drawer", Width = 110, Left = 140 };
            drawer.Click += async (_, _) => { Save(); await _state.KickDrawerAsync(); ShowResult("Drawer kick sent."); };
            var testRow = new Panel { Height = 30, Width = 330 };
            testRow.Controls.Add(test);
            testRow.Controls.Add(drawer);
            Add(testRow, 30);

            Add(_status, 40);

            var save = new Button { Text = "Save", Width = 110, DialogResult = DialogResult.OK };
            save.Click += (_, _) => { Save(); Close(); };
            Add(save);

            _status.Text = $"Listening on http://127.0.0.1:{Program.Port} (this PC only).";
        }

        /// <summary>One entry in the printer dropdown: a PointOfService device (PosId set) or a
        /// Windows spooler queue (PosId null).</summary>
        private sealed record PrinterChoice(string Display, string? PosId)
        {
            public override string ToString() => PosId == null ? Display : $"Direct: {Display} (recommended)";
        }

        private void SelectChoice(Func<PrinterChoice, bool> match)
        {
            foreach (var item in _printers.Items)
                if (item is PrinterChoice c && match(c)) { _printers.SelectedItem = item; return; }
        }

        private async System.Threading.Tasks.Task LoadPosDevicesAsync()
        {
            var devices = await PosPrint.ListPrintersAsync();
            if (IsDisposed || devices.Count == 0) return;
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                for (var i = devices.Count - 1; i >= 0; i--)
                    _printers.Items.Insert(0, new PrinterChoice(devices[i].Name, devices[i].Id));
                if (!string.IsNullOrWhiteSpace(_state.Config.PosDeviceId))
                    SelectChoice(c => c.PosId == _state.Config.PosDeviceId);
                // nothing configured at all → preselect the first Direct device so Save just works
                else if (_printers.SelectedItem == null && string.IsNullOrWhiteSpace(_state.Config.PrinterName))
                    _printers.SelectedIndex = 0;
            });
        }

        private void Save()
        {
            if (_printers.SelectedItem is PrinterChoice { PosId: not null } pos)
            {
                _state.Config.PosDeviceId = pos.PosId;
                _state.Config.PosDeviceName = pos.Display;
            }
            else
            {
                _state.Config.PosDeviceId = string.Empty;
                _state.Config.PosDeviceName = string.Empty;
                _state.Config.PrinterName = (_printers.SelectedItem as PrinterChoice)?.Display ?? string.Empty;
            }
            _state.Config.Columns = _columns.SelectedIndex == 1 ? 32 : 42;
            _state.Config.Emulation = _emulation.SelectedIndex switch
            {
                1 => Plutus.TillAgent.Core.EmulationResolver.EscPos,
                2 => Plutus.TillAgent.Core.EmulationResolver.StarRasterMode,
                _ => Plutus.TillAgent.Core.EmulationResolver.Auto,
            };
            _state.Config.AllowedOrigin = _origin.Text.Trim();
            _state.Config.Save();
            TrayApp.SetAutoStart(_autoStart.Checked);
        }

        private void ShowResult(string sent) =>
            _status.Text = _state.LastError != null ? $"⚠ {_state.LastError}" : $"{sent} Check the printer.";
    }
}
