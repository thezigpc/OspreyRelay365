using OspreyRelay.Core.Config;
using OspreyRelay.M365.Graph;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.App.Forms;

/// <summary>
/// Editor for a single FtpRoutingRule. Opened from FtpBridgeForm for add and edit operations.
/// </summary>
public class FtpRuleEditorForm : Form
{
    public FtpRoutingRule? Result { get; private set; }

    private readonly FtpRoutingRule? _edit;
    private readonly ConfigManager   _configManager;
    private readonly RelayLogger     _logger;

    // ── Match controls ────────────────────────────────────────────────────────
    private TextBox  _txtName        = null!;
    private CheckBox _chkEnabled     = null!;
    private TextBox  _txtVirtualPath = null!;
    private TextBox  _txtUsername    = null!;

    // ── Destination controls ──────────────────────────────────────────────────
    private RadioButton _rdoOneDrive   = null!;
    private RadioButton _rdoSharePoint = null!;
    private Panel       _pnlOneDrive   = null!;
    private Panel       _pnlSharePoint = null!;

    // OneDrive
    private TextBox _txtOneDriveUser = null!;

    // SharePoint
    private TextBox  _txtSiteSearch  = null!;
    private Button   _btnSearchSites = null!;
    private ComboBox _cboSiteResults = null!;
    private Label    _lblSiteUrl     = null!;
    private Label    _lblSiteId      = null!;
    private ComboBox _cboLibrary     = null!;
    private Label    _lblDriveId     = null!;
    private TextBox  _txtSpFolderPath = null!;
    private Button   _btnResolveSite  = null!;

    private string _resolvedSiteId  = "";
    private string _resolvedSiteUrl = "";
    private List<(string Name, string DriveId)> _libraries      = new();
    private List<(string DisplayName, string Url)> _siteSearchResults = new();

    // ── Common path controls ──────────────────────────────────────────────────
    private TextBox _txtFolderPath       = null!;
    private TextBox _txtFilenameTemplate = null!;

    // Scroll container reference for dynamic repositioning
    private Panel _scroll = null!;
    private int   _pathSectionY;

    public FtpRuleEditorForm(FtpRoutingRule? edit, ConfigManager configManager, RelayLogger logger)
    {
        _edit          = edit;
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        if (edit != null) PopulateFromEdit(edit);
        UpdateDestinationPanels();
    }

    private void InitializeComponent()
    {
        Text            = _edit == null ? "Add FTP Rule" : "Edit FTP Rule";
        Size            = new Size(560, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 10, 10, 0) };

        int y = 0;

        // ── Identity ──────────────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Identity", ref y);
        _chkEnabled     = AddCheckBox(_scroll, "Enabled", ref y, true);
        _txtName        = AddLabeledText(_scroll, "Name:", ref y, "", tip: null);

        // ── Match ─────────────────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Match", ref y);
        _txtVirtualPath = AddLabeledText(_scroll, "Virtual path prefix:", ref y, "/",
            tip: "FTP directory that triggers this rule. Use / to match everything.");
        _txtUsername    = AddLabeledText(_scroll, "Username (blank = any):", ref y, "",
            tip: null);

        // ── Destination type ──────────────────────────────────────────────────
        AddSectionLabel(_scroll, "Destination", ref y);
        _rdoOneDrive   = new RadioButton { Text = "OneDrive",   Location = new Point(0, y),  AutoSize = true, Checked = true };
        _rdoSharePoint = new RadioButton { Text = "SharePoint", Location = new Point(105, y), AutoSize = true };
        _rdoOneDrive.CheckedChanged += (_, _) => UpdateDestinationPanels();
        _scroll.Controls.AddRange(new Control[] { _rdoOneDrive, _rdoSharePoint });
        y += 28;

        // OneDrive panel (56 px tall)
        _pnlOneDrive = new Panel { Location = new Point(0, y), Width = 520, Height = 56 };
        {
            int py = 2;
            _txtOneDriveUser = AddLabeledTextInPanel(_pnlOneDrive, "User UPN:", ref py, "(blank = from FTP username)");
            _pnlOneDrive.Controls.Add(new Label
            {
                Text      = "Tip: leave blank — the relay will resolve OneDrive from the FTP login username.",
                Location  = new Point(4, py),
                Width     = 510,
                Font      = new Font("Segoe UI", 7.5f),
                ForeColor = Color.Gray,
                AutoSize  = false
            });
        }

        // SharePoint panel (230 px tall)
        _pnlSharePoint = new Panel { Location = new Point(0, y), Width = 520, Height = 232, Visible = false };
        {
            int spy = 2;

            _pnlSharePoint.Controls.Add(new Label { Text = "Search sites:", Location = new Point(0, spy + 3), AutoSize = true });
            _txtSiteSearch  = new TextBox { Location = new Point(95, spy),  Width = 290 };
            _btnSearchSites = new Button  { Text = "Search", Location = new Point(392, spy), Width = 74, Height = 26 };
            _btnSearchSites.Click += (_, _) => SearchSitesAsync();
            _pnlSharePoint.Controls.AddRange(new Control[] { _txtSiteSearch, _btnSearchSites });
            spy += 30;

            _cboSiteResults = new ComboBox { Location = new Point(0, spy), Width = 466, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboSiteResults.SelectedIndexChanged += (_, _) => OnSiteResultSelected();
            _pnlSharePoint.Controls.Add(_cboSiteResults);
            spy += 28;

            _lblSiteUrl = new Label { Location = new Point(0, spy), Width = 466, Font = new Font("Segoe UI", 7.5f), ForeColor = Color.Gray, AutoSize = false };
            _pnlSharePoint.Controls.Add(_lblSiteUrl);
            spy += 18;

            _btnResolveSite = new Button { Text = "Load Libraries", Location = new Point(0, spy), Width = 110, Height = 26 };
            _btnResolveSite.Click += (_, _) => ResolveSiteAsync();
            _pnlSharePoint.Controls.Add(_btnResolveSite);
            spy += 32;

            _pnlSharePoint.Controls.Add(new Label { Text = "Library:", Location = new Point(0, spy + 3), AutoSize = true });
            _cboLibrary = new ComboBox { Location = new Point(60, spy), Width = 406, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboLibrary.SelectedIndexChanged += (_, _) => OnLibrarySelected();
            _pnlSharePoint.Controls.Add(_cboLibrary);
            spy += 30;

            _lblSiteId  = new Label { Location = new Point(0, spy),      Width = 466, Font = new Font("Segoe UI", 7.5f), ForeColor = Color.Gray, AutoSize = false };
            _lblDriveId = new Label { Location = new Point(0, spy + 16), Width = 466, Font = new Font("Segoe UI", 7.5f), ForeColor = Color.Gray, AutoSize = false };
            _pnlSharePoint.Controls.AddRange(new Control[] { _lblSiteId, _lblDriveId });
            spy += 36;

            _pnlSharePoint.Controls.Add(new Label { Text = "Folder path:", Location = new Point(0, spy + 3), AutoSize = true });
            _txtSpFolderPath = new TextBox { Location = new Point(90, spy), Width = 376, Text = "/FtpRelay" };
            _pnlSharePoint.Controls.Add(_txtSpFolderPath);
            _pnlSharePoint.Height = spy + 32;
        }

        _scroll.Controls.Add(_pnlOneDrive);
        _scroll.Controls.Add(_pnlSharePoint);

        // Reserve space for whichever dest panel is taller
        y += _pnlSharePoint.Height + 8;
        _pathSectionY = y;

        // ── Storage path (OneDrive only — SharePoint uses its own field above) ─
        AddSectionLabel(_scroll, "Storage path (OneDrive)", ref y);
        _txtFolderPath = AddLabeledText(_scroll, "Folder path:", ref y, "/FtpRelay/%username%",
            tip: "Variables: %username%, %date%, %datetime%, %ftppath%");
        _txtFilenameTemplate = AddLabeledText(_scroll, "Filename template:", ref y, "",
            tip: "Optional. Variables: %filename%, %date%, %username%. Blank = keep original name.");

        // ── OK / Cancel ───────────────────────────────────────────────────────
        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height        = 42,
            Padding       = new Padding(0, 6, 8, 0)
        };
        var btnOk     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Size = new Size(80, 28) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
        btnOk.Click   += (_, _) => TrySave();
        AcceptButton   = btnOk;
        CancelButton   = btnCancel;
        btnRow.Controls.AddRange(new Control[] { btnCancel, btnOk });

        Controls.Add(_scroll);
        Controls.Add(btnRow);
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void AddSectionLabel(Panel p, string text, ref int y)
    {
        p.Controls.Add(new Label
        {
            Text      = text,
            Location  = new Point(0, y),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 80, 140)
        });
        y += 24;
    }

    private static CheckBox AddCheckBox(Panel p, string text, ref int y, bool defaultChecked)
    {
        var cb = new CheckBox { Text = text, Location = new Point(0, y), AutoSize = true, Checked = defaultChecked };
        p.Controls.Add(cb);
        y += 26;
        return cb;
    }

    private static TextBox AddLabeledText(Panel p, string label, ref int y, string defaultVal, string? tip)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(0, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(160, y), Width = 350, Text = defaultVal };
        p.Controls.Add(tb);
        y += 28;
        if (tip != null)
        {
            p.Controls.Add(new Label { Text = tip, Location = new Point(4, y), Width = 506, AutoSize = false,
                Font = new Font("Segoe UI", 7.5f), ForeColor = Color.Gray });
            y += 18;
        }
        return tb;
    }

    private static TextBox AddLabeledTextInPanel(Panel p, string label, ref int y, string placeholder)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(0, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(100, y), Width = 406, PlaceholderText = placeholder };
        p.Controls.Add(tb);
        y += 28;
        return tb;
    }

    // ── Populate from existing rule ───────────────────────────────────────────

    private void PopulateFromEdit(FtpRoutingRule rule)
    {
        _txtName.Text        = rule.Name;
        _chkEnabled.Checked  = rule.Enabled;
        _txtVirtualPath.Text = rule.VirtualPath;
        _txtUsername.Text    = rule.Username;

        if (rule.DestinationType == FileDestinationType.SharePoint)
        {
            _rdoSharePoint.Checked = true;
            _resolvedSiteUrl       = rule.SiteUrl;
            _resolvedSiteId        = rule.SiteId;
            _lblSiteUrl.Text       = rule.SiteUrl;
            _lblSiteId.Text        = $"Site ID: {rule.SiteId}";
            _lblDriveId.Text       = $"Drive ID: {rule.LibraryDriveId}";

            if (!string.IsNullOrWhiteSpace(rule.LibraryName))
            {
                _libraries = new List<(string, string)> { (rule.LibraryName, rule.LibraryDriveId) };
                _cboLibrary.Items.Add(rule.LibraryName);
                _cboLibrary.SelectedIndex = 0;
            }
            _txtSpFolderPath.Text = rule.FolderPath;
        }
        else
        {
            _rdoOneDrive.Checked  = true;
            _txtOneDriveUser.Text = rule.OneDriveUser;
        }

        _txtFolderPath.Text       = rule.FolderPath;
        _txtFilenameTemplate.Text = rule.FilenameTemplate ?? "";
    }

    // ── Destination panel switching ───────────────────────────────────────────

    private void UpdateDestinationPanels()
    {
        _pnlOneDrive.Visible   = _rdoOneDrive.Checked;
        _pnlSharePoint.Visible = _rdoSharePoint.Checked;
    }

    // ── SharePoint search / resolve ───────────────────────────────────────────

    private async void SearchSitesAsync()
    {
        _btnSearchSites.Enabled = false;
        _cboSiteResults.Items.Clear();
        _siteSearchResults.Clear();

        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var storer     = new GraphFileStorer(_configManager, mailSender, _logger);
            _siteSearchResults = await storer.SearchSitesAsync(_txtSiteSearch.Text.Trim(), default);

            foreach (var (name, _) in _siteSearchResults)
                _cboSiteResults.Items.Add(name);

            if (_siteSearchResults.Count == 0)
                MessageBox.Show("No sites found.", "Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                _cboSiteResults.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Search failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearchSites.Enabled = true;
        }
    }

    private void OnSiteResultSelected()
    {
        var idx = _cboSiteResults.SelectedIndex;
        if (idx < 0 || idx >= _siteSearchResults.Count) return;
        _resolvedSiteUrl = _siteSearchResults[idx].Url;
        _lblSiteUrl.Text = _resolvedSiteUrl;
        _lblSiteId.Text  = "";
        _resolvedSiteId  = "";
        _cboLibrary.Items.Clear();
        _libraries.Clear();
    }

    private async void ResolveSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(_resolvedSiteUrl))
        {
            MessageBox.Show("Select a site from the search results first.", "Info",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnResolveSite.Enabled = false;
        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var storer     = new GraphFileStorer(_configManager, mailSender, _logger);
            _resolvedSiteId = await storer.ResolveSiteIdAsync(_resolvedSiteUrl, default);
            _lblSiteId.Text = $"Site ID: {_resolvedSiteId}";

            _libraries = await storer.GetLibrariesAsync(_resolvedSiteId, default);
            _cboLibrary.Items.Clear();
            foreach (var (name, _) in _libraries)
                _cboLibrary.Items.Add(name);

            if (_libraries.Count > 0)
                _cboLibrary.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not resolve site:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnResolveSite.Enabled = true;
        }
    }

    private void OnLibrarySelected()
    {
        var idx = _cboLibrary.SelectedIndex;
        if (idx < 0 || idx >= _libraries.Count) return;
        _lblDriveId.Text = $"Drive ID: {_libraries[idx].DriveId}";
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void TrySave()
    {
        var vpath = _txtVirtualPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(vpath)) vpath = "/";
        if (!vpath.StartsWith('/')) vpath = "/" + vpath;

        var rule = new FtpRoutingRule
        {
            Id               = _edit?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Enabled          = _chkEnabled.Checked,
            Name             = _txtName.Text.Trim(),
            VirtualPath      = vpath,
            Username         = _txtUsername.Text.Trim(),
            FilenameTemplate = string.IsNullOrWhiteSpace(_txtFilenameTemplate.Text) ? null : _txtFilenameTemplate.Text.Trim()
        };

        if (_rdoSharePoint.Checked)
        {
            if (string.IsNullOrWhiteSpace(_resolvedSiteId)
                || _cboLibrary.SelectedIndex < 0
                || _libraries.Count == 0)
            {
                MessageBox.Show("Search for a site and select a library before saving.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var (libName, driveId) = _libraries[_cboLibrary.SelectedIndex];
            rule.DestinationType = FileDestinationType.SharePoint;
            rule.SiteUrl         = _resolvedSiteUrl;
            rule.SiteId          = _resolvedSiteId;
            rule.LibraryName     = libName;
            rule.LibraryDriveId  = driveId;
            rule.FolderPath      = _txtSpFolderPath.Text.Trim();
        }
        else
        {
            rule.DestinationType = FileDestinationType.OneDrive;
            rule.OneDriveUser    = _txtOneDriveUser.Text.Trim();
            rule.FolderPath      = _txtFolderPath.Text.Trim();
        }

        Result       = rule;
        DialogResult = DialogResult.OK;
    }
}
