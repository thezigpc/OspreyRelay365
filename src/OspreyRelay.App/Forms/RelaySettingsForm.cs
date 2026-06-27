using OspreyRelay.Core.Config;

namespace OspreyRelay.App.Forms;

/// <summary>
/// Operational settings form — relay endpoint, email options, SMTP auth, and smarthost failover.
/// Azure AD credentials are managed in SetupWizardForm (Configure App).
/// </summary>
public class RelaySettingsForm : Form
{
    private readonly ConfigManager _configManager;

    // SMTP Settings tab
    private NumericUpDown _nudPort = null!;
    private TextBox _txtBindAddress = null!;
    private NumericUpDown _nudMaxSize = null!;
    private TextBox _txtFallbackSender = null!;
    private CheckBox _chkSaveToSent = null!;
    private CheckBox _chkRequireAuth = null!;
    private TextBox _txtSmtpUser = null!;
    private TextBox _txtSmtpPass = null!;

    // Smarthost tab
    private CheckBox _chkSmarthostEnabled = null!;
    private TextBox _txtSmarthostHost = null!;
    private NumericUpDown _nudSmarthostPort = null!;
    private ComboBox _cboSmarthostTls = null!;
    private TextBox _txtSmarthostUser = null!;
    private TextBox _txtSmarthostPass = null!;
    private RadioButton _rdoSmarthostOriginalFrom = null!;
    private RadioButton _rdoSmarthostFallbackFrom = null!;

    public RelaySettingsForm(ConfigManager configManager)
    {
        _configManager = configManager;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Settings";
        ClientSize = new Size(560, 580);
        MinimumSize = new Size(500, 520);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var btnSave = new Button
        {
            Text = "Save",
            Size = new Size(100, 30),
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        var btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(100, 30),
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        btnSave.Click   += (_, _) => SaveSettings();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var pnlBottom = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        pnlBottom.Controls.Add(btnSave);   // DockRight: added first = left of pair
        pnlBottom.Controls.Add(btnCancel); // DockRight: added second = rightmost

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSmtpTab());
        tabs.TabPages.Add(BuildSmarthostTab());

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tlp.RowStyles.Clear();
        tlp.ColumnStyles.Clear();
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tlp.Controls.Add(tabs, 0, 0);
        tlp.Controls.Add(pnlBottom, 0, 1);

        Controls.Add(tlp);
    }

    // ── SMTP Settings tab ─────────────────────────────────────────────────────

    private TabPage BuildSmtpTab()
    {
        var page = new TabPage("SMTP Settings");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 8, 12, 8) };

        int y = 8;

        Section(scroll, "Relay Endpoint", ref y);

        scroll.Controls.Add(new Label { Text = "Local SMTP port:", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _nudPort = new NumericUpDown
        {
            Location = new Point(12, y), Width = 90,
            Minimum = 1, Maximum = 65535, Value = 25,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_nudPort);
        scroll.Controls.Add(new Label
        {
            Text = "Ports below 1024 require the relay to run as a service or with admin elevation.",
            Location = new Point(110, y + 4), AutoSize = true,
            ForeColor = Color.DarkOrange, Font = new Font("Segoe UI", 8)
        });
        y += 36;

        scroll.Controls.Add(new Label { Text = "Max message size (MB):", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _nudMaxSize = new NumericUpDown
        {
            Location = new Point(12, y), Width = 90,
            Minimum = 1, Maximum = 150, Value = 25,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_nudMaxSize);
        y += 36;

        _txtBindAddress = Field(scroll, "Bind address  (0.0.0.0 = all interfaces):", ref y);

        y += 4;
        Section(scroll, "Email", ref y);

        _txtFallbackSender = Field(scroll, "Fallback sender email  (Microsoft 365 mailbox used when From: isn't in tenant):", ref y);

        _chkSaveToSent = Check(scroll, "Save sent emails to Sent Items", ref y);

        y += 4;
        Section(scroll, "SMTP Authentication", ref y);

        _chkRequireAuth = Check(scroll, "Require SMTP authentication from local clients", ref y);
        _chkRequireAuth.CheckedChanged += (_, _) => UpdateSmtpAuthFields();

        _txtSmtpUser = Field(scroll, "SMTP username:", ref y);
        _txtSmtpPass = Field(scroll, "SMTP password:", ref y, password: true);

        page.Controls.Add(scroll);
        return page;
    }

    // ── Smarthost Failover tab ────────────────────────────────────────────────

    private TabPage BuildSmarthostTab()
    {
        var page = new TabPage("Smarthost Failover");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 8, 12, 8) };

        int y = 8;

        _chkSmarthostEnabled = new CheckBox
        {
            Text = "Enable smarthost failover for Email Relay routes",
            Location = new Point(12, y), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        _chkSmarthostEnabled.CheckedChanged += (_, _) => UpdateSmarthostFields();
        scroll.Controls.Add(_chkSmarthostEnabled);
        y += 28;

        scroll.Controls.Add(new Label
        {
            Text = "When Microsoft 365 is unreachable (HTTP 503/504), the relay attempts to deliver\n" +
                   "via this smarthost instead of saving to the unrouted folder. Typically a corporate\n" +
                   "mail gateway (Barracuda, Mimecast, etc.) that queues and spools for later delivery.",
            Location = new Point(12, y), AutoSize = false, Width = 510, Height = 50,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        });
        y += 58;

        _txtSmarthostHost = Field(scroll, "Smarthost host (IP or hostname):", ref y,
            placeholder: "e.g. mail.company.com  or  192.168.1.100");

        scroll.Controls.Add(new Label { Text = "Port:", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        scroll.Controls.Add(new Label { Text = "TLS:", Location = new Point(130, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _nudSmarthostPort = new NumericUpDown
        {
            Location = new Point(12, y), Width = 90,
            Minimum = 1, Maximum = 65535, Value = 587,
            Font = new Font("Segoe UI", 9)
        };
        _cboSmarthostTls = new ComboBox
        {
            Location = new Point(130, y), Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        _cboSmarthostTls.Items.AddRange(new object[] { "None", "STARTTLS (Recommended)", "SSL/TLS" });
        _cboSmarthostTls.SelectedIndex = 1;
        scroll.Controls.AddRange(new Control[] { _nudSmarthostPort, _cboSmarthostTls });
        y += 36;

        _txtSmarthostUser = Field(scroll, "Username (optional — leave blank for unauthenticated relay):", ref y);
        _txtSmarthostPass = Field(scroll, "Password (optional):", ref y, password: true);

        y += 4;
        scroll.Controls.Add(new Label
        {
            Text = "From address on smarthost delivery:",
            Location = new Point(12, y), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        y += 24;

        _rdoSmarthostOriginalFrom = new RadioButton
        {
            Text = "Use original envelope-from  (preserves sender identity; requires smarthost to accept arbitrary MAIL FROM)",
            Location = new Point(12, y), AutoSize = true,
            Font = new Font("Segoe UI", 9), Checked = true
        };
        scroll.Controls.Add(_rdoSmarthostOriginalFrom);
        y += 26;

        _rdoSmarthostFallbackFrom = new RadioButton
        {
            Text = "Use fallback sender  (substitutes FallbackSenderEmail; original From: added as Reply-To)",
            Location = new Point(12, y), AutoSize = true,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_rdoSmarthostFallbackFrom);

        page.Controls.Add(scroll);
        return page;
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        var cfg = _configManager.Config;

        _nudPort.Value         = Math.Clamp(cfg.RelayPort, 1, 65535);
        _nudMaxSize.Value      = Math.Clamp(cfg.MaxMessageSizeMb, 1, 150);
        _txtBindAddress.Text   = string.IsNullOrWhiteSpace(cfg.BindAddress) ? "0.0.0.0" : cfg.BindAddress;
        _txtFallbackSender.Text = cfg.FallbackSenderEmail;
        _chkSaveToSent.Checked = cfg.SaveToSentItems;
        _chkRequireAuth.Checked = cfg.RequireSmtpAuth;
        _txtSmtpUser.Text      = cfg.SmtpUsername;
        _txtSmtpPass.Text      = cfg.SmtpPassword;

        _chkSmarthostEnabled.Checked  = cfg.SmarthostEnabled;
        _txtSmarthostHost.Text         = cfg.SmarthostHost;
        _nudSmarthostPort.Value        = Math.Clamp(cfg.SmarthostPort, 1, 65535);
        _cboSmarthostTls.SelectedIndex = (int)cfg.SmarthostTls;
        _txtSmarthostUser.Text         = cfg.SmarthostUsername;
        _txtSmarthostPass.Text         = cfg.SmarthostPassword;
        _rdoSmarthostOriginalFrom.Checked  = cfg.SmarthostUseOriginalFrom;
        _rdoSmarthostFallbackFrom.Checked  = !cfg.SmarthostUseOriginalFrom;

        UpdateSmtpAuthFields();
        UpdateSmarthostFields();
    }

    private void SaveSettings()
    {
        var cfg = _configManager.Config;

        cfg.RelayPort          = (int)_nudPort.Value;
        cfg.MaxMessageSizeMb   = (int)_nudMaxSize.Value;
        cfg.BindAddress        = _txtBindAddress.Text.Trim();
        cfg.FallbackSenderEmail = _txtFallbackSender.Text.Trim();
        cfg.SaveToSentItems    = _chkSaveToSent.Checked;
        cfg.RequireSmtpAuth    = _chkRequireAuth.Checked;
        cfg.SmtpUsername       = _txtSmtpUser.Text.Trim();
        cfg.SmtpPassword       = _txtSmtpPass.Text;

        cfg.SmarthostEnabled        = _chkSmarthostEnabled.Checked;
        cfg.SmarthostHost           = _txtSmarthostHost.Text.Trim();
        cfg.SmarthostPort           = (int)_nudSmarthostPort.Value;
        cfg.SmarthostTls            = (SmarthostTls)_cboSmarthostTls.SelectedIndex;
        cfg.SmarthostUsername       = _txtSmarthostUser.Text.Trim();
        cfg.SmarthostPassword       = _txtSmarthostPass.Text;
        cfg.SmarthostUseOriginalFrom = _rdoSmarthostOriginalFrom.Checked;

        _configManager.Save(cfg);
        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Dynamic field enable/disable ──────────────────────────────────────────

    private void UpdateSmtpAuthFields()
    {
        bool auth = _chkRequireAuth.Checked;
        _txtSmtpUser.Enabled = auth;
        _txtSmtpPass.Enabled = auth;
    }

    private void UpdateSmarthostFields()
    {
        bool on = _chkSmarthostEnabled.Checked;
        _txtSmarthostHost.Enabled         = on;
        _nudSmarthostPort.Enabled         = on;
        _cboSmarthostTls.Enabled          = on;
        _txtSmarthostUser.Enabled         = on;
        _txtSmarthostPass.Enabled         = on;
        _rdoSmarthostOriginalFrom.Enabled = on;
        _rdoSmarthostFallbackFrom.Enabled = on;
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static TextBox Field(Panel pnl, string label, ref int y,
        bool password = false, string placeholder = "")
    {
        pnl.Controls.Add(new Label
        {
            Text = label, Location = new Point(12, y),
            AutoSize = true, Font = new Font("Segoe UI", 9)
        });
        y += 20;
        var tb = new TextBox
        {
            Location = new Point(12, y), Width = 510,
            UseSystemPasswordChar = password,
            Font = new Font("Segoe UI", 9),
            PlaceholderText = placeholder
        };
        pnl.Controls.Add(tb);
        y += 32;
        return tb;
    }

    private static CheckBox Check(Panel pnl, string text, ref int y)
    {
        var chk = new CheckBox
        {
            Text = text, Location = new Point(12, y),
            AutoSize = true, Font = new Font("Segoe UI", 9)
        };
        pnl.Controls.Add(chk);
        y += 28;
        return chk;
    }

    private static void Section(Panel pnl, string title, ref int y)
    {
        pnl.Controls.Add(new Label
        {
            Text = title, Location = new Point(12, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        y += 22;
    }
}
