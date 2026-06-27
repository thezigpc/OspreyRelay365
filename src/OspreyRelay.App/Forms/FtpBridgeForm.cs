using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.App.Forms;

/// <summary>
/// Manages the FTP bridge: general settings, per-device users, and routing rules.
/// Returns DialogResult.OK when settings were saved (caller should restart the relay).
/// </summary>
public class FtpBridgeForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger   _logger;

    // ── General tab ───────────────────────────────────────────────────────────
    private CheckBox     _chkEnabled       = null!;
    private CheckBox     _chkAnyLogin      = null!;
    private NumericUpDown _nudPort         = null!;
    private TextBox      _txtBindAddress   = null!;
    private NumericUpDown _nudPassiveMin   = null!;
    private NumericUpDown _nudPassiveMax   = null!;

    // ── Users tab ─────────────────────────────────────────────────────────────
    private ListView _lvUsers   = null!;
    private Button   _btnAddUser    = null!;
    private Button   _btnEditUser   = null!;
    private Button   _btnDeleteUser = null!;
    private List<FtpUserConfig> _users = new();

    // ── Rules tab ─────────────────────────────────────────────────────────────
    private ListView _lvRules   = null!;
    private Button   _btnAddRule    = null!;
    private Button   _btnEditRule   = null!;
    private Button   _btnDeleteRule = null!;
    private Button   _btnRuleUp     = null!;
    private Button   _btnRuleDown   = null!;
    private List<FtpRoutingRule> _rules = new();

    public FtpBridgeForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        LoadFromConfig();
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text            = "FTP Bridge";
        Size            = new Size(660, 540);
        MinimumSize     = new Size(540, 440);
        StartPosition   = FormStartPosition.CenterParent;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildUsersTab());
        tabs.TabPages.Add(BuildRulesTab());

        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height        = 42,
            Padding       = new Padding(0, 6, 8, 0)
        };
        var btnOk     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Size = new Size(80, 28) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
        btnOk.Click += (_, _) => Save();
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        btnRow.Controls.AddRange(new Control[] { btnCancel, btnOk });

        Controls.Add(tabs);
        Controls.Add(btnRow);
    }

    // ── General tab ──────────────────────────────────────────────────────────

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General");
        var pnl  = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            Padding     = new Padding(14, 14, 14, 8),
            AutoSize    = true
        };
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        _chkEnabled = new CheckBox { Text = "Enable FTP bridge", AutoSize = true, Checked = false };
        pnl.Controls.Add(new Label(), 0, row);
        pnl.Controls.Add(_chkEnabled, 1, row++);

        _chkAnyLogin = new CheckBox { Text = "Accept any login (no user list required)", AutoSize = true };
        pnl.Controls.Add(new Label(), 0, row);
        pnl.Controls.Add(_chkAnyLogin, 1, row++);
        pnl.Controls.Add(Help("When on, any username and password is accepted. Suitable for trusted LAN devices."), 1, row++);

        _nudPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 2121, Width = 90 };
        AddRow(pnl, row++, "Port:", _nudPort);
        pnl.Controls.Add(Help("Default 2121. Port 21 requires elevated privileges on Windows."), 1, row++);

        _txtBindAddress = new TextBox { Text = "0.0.0.0", Width = 160 };
        AddRow(pnl, row++, "Bind address:", _txtBindAddress);

        // Passive port range
        var portRangePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _nudPassiveMin = new NumericUpDown { Minimum = 1024, Maximum = 65530, Value = 50000, Width = 80 };
        _nudPassiveMax = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 50100, Width = 80 };
        portRangePanel.Controls.Add(_nudPassiveMin);
        portRangePanel.Controls.Add(new Label { Text = " to ", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        portRangePanel.Controls.Add(_nudPassiveMax);
        AddRow(pnl, row++, "Passive ports:", portRangePanel);
        pnl.Controls.Add(Help("These ports must be open in the firewall for passive FTP data connections."), 1, row++);

        pnl.Controls.Add(new Label
        {
            Text      = "Note: FTPS (explicit TLS) is not yet supported — use plain FTP on a trusted LAN only.",
            ForeColor = Color.Gray,
            AutoSize  = false,
            Width     = 400,
            Height    = 40
        }, 1, row++);

        page.Controls.Add(pnl);
        return page;
    }

    // ── Users tab ─────────────────────────────────────────────────────────────

    private TabPage BuildUsersTab()
    {
        var page = new TabPage("Users");

        _lvUsers = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            MultiSelect   = false
        };
        _lvUsers.Columns.AddRange(new[]
        {
            new ColumnHeader { Text = "Username",          Width = 160 },
            new ColumnHeader { Text = "Any Password",      Width = 100 },
            new ColumnHeader { Text = "Notes",             Width = 260 }
        });
        _lvUsers.DoubleClick += (_, _) => EditUser();

        _btnAddUser    = SmallBtn("Add");
        _btnEditUser   = SmallBtn("Edit");
        _btnDeleteUser = SmallBtn("Delete");
        _btnAddUser.Click    += (_, _) => AddUser();
        _btnEditUser.Click   += (_, _) => EditUser();
        _btnDeleteUser.Click += (_, _) => DeleteUser();

        var btnBar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 38,
            FlowDirection = FlowDirection.LeftToRight,
            Padding       = new Padding(4, 4, 4, 0)
        };
        btnBar.Controls.AddRange(new Control[] { _btnAddUser, _btnEditUser, _btnDeleteUser });

        page.Controls.Add(_lvUsers);
        page.Controls.Add(btnBar);
        return page;
    }

    // ── Rules tab ─────────────────────────────────────────────────────────────

    private TabPage BuildRulesTab()
    {
        var page = new TabPage("Rules");

        _lvRules = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            MultiSelect   = false
        };
        _lvRules.Columns.AddRange(new[]
        {
            new ColumnHeader { Text = "Name",        Width = 120 },
            new ColumnHeader { Text = "Enabled",     Width = 60  },
            new ColumnHeader { Text = "Virtual Path", Width = 110 },
            new ColumnHeader { Text = "Username",    Width = 90  },
            new ColumnHeader { Text = "Destination", Width = 120 },
            new ColumnHeader { Text = "Folder Path", Width = 140 }
        });
        _lvRules.DoubleClick += (_, _) => EditRule();

        _btnAddRule    = SmallBtn("Add");
        _btnEditRule   = SmallBtn("Edit");
        _btnDeleteRule = SmallBtn("Delete");
        _btnRuleUp     = SmallBtn("↑");
        _btnRuleDown   = SmallBtn("↓");
        _btnAddRule.Click    += (_, _) => AddRule();
        _btnEditRule.Click   += (_, _) => EditRule();
        _btnDeleteRule.Click += (_, _) => DeleteRule();
        _btnRuleUp.Click     += (_, _) => MoveRule(-1);
        _btnRuleDown.Click   += (_, _) => MoveRule(+1);

        _btnRuleUp.Width   = 34;
        _btnRuleDown.Width = 34;

        var btnBar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 38,
            FlowDirection = FlowDirection.LeftToRight,
            Padding       = new Padding(4, 4, 4, 0)
        };
        btnBar.Controls.AddRange(new Control[] { _btnAddRule, _btnEditRule, _btnDeleteRule, _btnRuleUp, _btnRuleDown });

        page.Controls.Add(_lvRules);
        page.Controls.Add(btnBar);
        return page;
    }

    // ── Load / Refresh ────────────────────────────────────────────────────────

    private void LoadFromConfig()
    {
        var cfg = _configManager.Config;

        _chkEnabled.Checked    = cfg.FtpEnabled;
        _chkAnyLogin.Checked   = cfg.FtpAcceptAnyLogin;
        _nudPort.Value         = cfg.FtpPort;
        _txtBindAddress.Text   = cfg.FtpBindAddress;
        _nudPassiveMin.Value   = cfg.FtpPassivePortMin;
        _nudPassiveMax.Value   = cfg.FtpPassivePortMax;

        _users = cfg.FtpUsers.Select(Clone).ToList();
        _rules = cfg.FtpRules.Select(Clone).ToList();

        RefreshUserList();
        RefreshRuleList();
    }

    private void RefreshUserList()
    {
        _lvUsers.Items.Clear();
        foreach (var u in _users)
        {
            var item = new ListViewItem(u.Username);
            item.SubItems.Add(u.AcceptAnyPassword ? "Yes" : "No");
            item.SubItems.Add(u.Notes);
            _lvUsers.Items.Add(item);
        }
    }

    private void RefreshRuleList()
    {
        _lvRules.Items.Clear();
        foreach (var r in _rules)
        {
            var dest = r.DestinationType == FileDestinationType.SharePoint
                ? $"SP: {r.LibraryName}"
                : string.IsNullOrWhiteSpace(r.OneDriveUser) ? "OneDrive (auto)" : $"OD: {r.OneDriveUser}";

            var item = new ListViewItem(r.Name.Length > 0 ? r.Name : r.Id);
            item.SubItems.Add(r.Enabled ? "Yes" : "No");
            item.SubItems.Add(r.VirtualPath);
            item.SubItems.Add(r.Username.Length > 0 ? r.Username : "(any)");
            item.SubItems.Add(dest);
            item.SubItems.Add(r.FolderPath);
            _lvRules.Items.Add(item);
        }
    }

    // ── User CRUD ─────────────────────────────────────────────────────────────

    private void AddUser()
    {
        using var dlg = new FtpUserEditorForm();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

        if (_users.Any(u => u.Username.Equals(dlg.Result.Username, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A user with that username already exists.", "Duplicate",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _users.Add(dlg.Result);
        RefreshUserList();
    }

    private void EditUser()
    {
        var idx = _lvUsers.SelectedIndices.Count > 0 ? _lvUsers.SelectedIndices[0] : -1;
        if (idx < 0) return;

        using var dlg = new FtpUserEditorForm(_users[idx]);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

        _users[idx] = dlg.Result;
        RefreshUserList();
    }

    private void DeleteUser()
    {
        var idx = _lvUsers.SelectedIndices.Count > 0 ? _lvUsers.SelectedIndices[0] : -1;
        if (idx < 0) return;

        if (MessageBox.Show($"Delete user '{_users[idx].Username}'?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _users.RemoveAt(idx);
        RefreshUserList();
    }

    // ── Rule CRUD ─────────────────────────────────────────────────────────────

    private void AddRule()
    {
        using var dlg = new FtpRuleEditorForm(null, _configManager, _logger);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
        _rules.Add(dlg.Result);
        RefreshRuleList();
    }

    private void EditRule()
    {
        var idx = _lvRules.SelectedIndices.Count > 0 ? _lvRules.SelectedIndices[0] : -1;
        if (idx < 0) return;

        using var dlg = new FtpRuleEditorForm(_rules[idx], _configManager, _logger);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

        _rules[idx] = dlg.Result;
        RefreshRuleList();
    }

    private void DeleteRule()
    {
        var idx = _lvRules.SelectedIndices.Count > 0 ? _lvRules.SelectedIndices[0] : -1;
        if (idx < 0) return;

        var name = _rules[idx].Name.Length > 0 ? _rules[idx].Name : _rules[idx].VirtualPath;
        if (MessageBox.Show($"Delete rule '{name}'?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _rules.RemoveAt(idx);
        RefreshRuleList();
    }

    private void MoveRule(int delta)
    {
        var idx = _lvRules.SelectedIndices.Count > 0 ? _lvRules.SelectedIndices[0] : -1;
        var newIdx = idx + delta;
        if (idx < 0 || newIdx < 0 || newIdx >= _rules.Count) return;

        (_rules[idx], _rules[newIdx]) = (_rules[newIdx], _rules[idx]);
        RefreshRuleList();
        _lvRules.Items[newIdx].Selected = true;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void Save()
    {
        var cfg = _configManager.Config;

        cfg.FtpEnabled        = _chkEnabled.Checked;
        cfg.FtpAcceptAnyLogin = _chkAnyLogin.Checked;
        cfg.FtpPort           = (int)_nudPort.Value;
        cfg.FtpBindAddress   = _txtBindAddress.Text.Trim();
        cfg.FtpPassivePortMin = (int)_nudPassiveMin.Value;
        cfg.FtpPassivePortMax = (int)_nudPassiveMax.Value;
        cfg.FtpUsers         = _users;
        cfg.FtpRules         = _rules;

        _configManager.Save(cfg);
        DialogResult = DialogResult.OK;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddRow(TableLayoutPanel tbl, int row, string label, Control ctrl)
    {
        tbl.Controls.Add(new Label
        {
            Text      = label,
            TextAlign = ContentAlignment.MiddleRight,
            Dock      = DockStyle.Fill
        }, 0, row);
        tbl.Controls.Add(ctrl, 1, row);
    }

    private static Label Help(string text) => new Label
    {
        Text      = text,
        AutoSize  = false,
        Width     = 380,
        Height    = 20,
        Font      = new Font("Segoe UI", 7.5f),
        ForeColor = Color.Gray
    };

    private static Button SmallBtn(string text) => new Button
    {
        Text      = text,
        Size      = new Size(68, 28),
        Margin    = new Padding(2, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = Color.FromArgb(200, 200, 210) },
        Font      = new Font("Segoe UI", 8f),
        UseVisualStyleBackColor = true
    };

    private static FtpUserConfig Clone(FtpUserConfig u) => new()
    {
        Username          = u.Username,
        PasswordEncrypted = u.PasswordEncrypted,
        Password          = u.Password,
        AcceptAnyPassword = u.AcceptAnyPassword,
        Notes             = u.Notes
    };

    private static FtpRoutingRule Clone(FtpRoutingRule r) => new()
    {
        Id              = r.Id,
        Enabled         = r.Enabled,
        Name            = r.Name,
        VirtualPath     = r.VirtualPath,
        Username        = r.Username,
        DestinationType = r.DestinationType,
        OneDriveUser    = r.OneDriveUser,
        SiteUrl         = r.SiteUrl,
        SiteId          = r.SiteId,
        LibraryName     = r.LibraryName,
        LibraryDriveId  = r.LibraryDriveId,
        FolderPath      = r.FolderPath,
        FilenameTemplate = r.FilenameTemplate
    };
}
