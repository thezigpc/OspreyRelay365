using Relay365.Core.Config;
using Relay365.Core.Graph;
using Relay365.Core.Logging;

namespace Relay365.Forms;

/// <summary>
/// Editor for a single SuffixRule or RecipientFileRule.
/// Opened from FileRoutingRulesForm for add and edit operations.
/// The caller checks SuffixResult / RecipientResult after DialogResult.OK.
/// </summary>
public class FileRuleEditorForm : Form
{
    // ── Outputs ───────────────────────────────────────────────────────────────
    public SuffixRule? SuffixResult { get; private set; }
    public RecipientFileRule? RecipientResult { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly SuffixRule? _editSuffix;
    private readonly RecipientFileRule? _editRecipient;
    private readonly bool _isSuffixMode;
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    // ── Controls — rule type ──────────────────────────────────────────────────
    private RadioButton _rdoSuffix = null!;
    private RadioButton _rdoRecipient = null!;

    // Suffix-specific
    private TextBox _txtSuffix = null!;
    private TextBox _txtBaseDomain = null!;

    // Recipient-specific
    private TextBox _txtToAddress = null!;

    // ── Destination type ──────────────────────────────────────────────────────
    private RadioButton _rdoTypeRelay = null!;
    private RadioButton _rdoTypeOneDrive = null!;
    private RadioButton _rdoTypeSharePoint = null!;
    private RadioButton _rdoTypeSmarthost = null!;

    // Relay
    private TextBox _txtRelayVia = null!;

    // OneDrive
    private TextBox _txtOneDriveUser = null!;
    private TextBox _txtOneDrivePath = null!;

    // SharePoint
    private TextBox _txtSiteSearch = null!;
    private Button _btnSearchSites = null!;
    private ComboBox _cboSiteSearchResults = null!;
    private TextBox _txtSiteUrl = null!;
    private Label _lblSiteId = null!;
    private ComboBox _cboLibrary = null!;
    private Label _lblDriveId = null!;
    private TextBox _txtSpFolderPath = null!;
    private Button _btnResolveSite = null!;
    private Button _btnVerifyFolder = null!;

    // ── Per-rule overrides ────────────────────────────────────────────────────
    private CheckBox _chkOverrideSaveWhat = null!;
    private ComboBox _cboSaveWhat = null!;
    private CheckBox _chkOverrideSubfolder = null!;
    private CheckBox _chkSubfolderValue = null!;
    private CheckBox _chkOverrideFromSender = null!;
    private ComboBox _cboFromSender = null!;
    private CheckBox _chkOverrideFilenameTemplate = null!;
    private TextBox _txtFilenameTemplate = null!;
    private CheckBox _chkOverrideSubjectDelimiter = null!;
    private TextBox _txtSubjectDelimiter = null!;
    private CheckBox _chkEnabled = null!;

    // Panels grouping destination UI
    private Panel _pnlRelayDest = null!;
    private Panel _pnlOneDriveDest = null!;
    private Panel _pnlSpDest = null!;
    private Panel _pnlSmarthostDest = null!;
    private CheckBox _chkSmarthostUseGlobal = null!;
    private TextBox _txtSmarthostHostOverride = null!;
    private NumericUpDown _nudSmarthostPortOverride = null!;
    private ComboBox _cboSmarthostTlsOverride = null!;
    private TextBox _txtSmarthostUserOverride = null!;
    private TextBox _txtSmarthostPassOverride = null!;

    // Resolved SP data
    private string _resolvedSiteId = "";
    private List<(string Name, string DriveId)> _libraries = new();
    private List<(string DisplayName, string Url)> _siteSearchResults = new();

    // Scroll container holds all scrollable content
    private Panel _scroll = null!;

    public FileRuleEditorForm(
        SuffixRule? editSuffix, RecipientFileRule? editRecipient,
        ConfigManager configManager, RelayLogger logger)
    {
        _editSuffix    = editSuffix;
        _editRecipient = editRecipient;
        _isSuffixMode  = editSuffix != null || editRecipient == null;
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        PopulateFromEdit();
    }

    private void InitializeComponent()
    {
        Text = "File Rule Editor";
        Size = new Size(680, 700);
        MinimumSize = new Size(620, 580);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        // ── Rule type selector (fixed top) ────────────────────────────────────
        var pnlType = new Panel
        {
            Dock = DockStyle.Top, Height = 56,
            Padding = new Padding(10, 8, 10, 0),
            BackColor = Color.FromArgb(245, 245, 250)
        };
        _rdoSuffix    = new RadioButton { Text = "Suffix rule  (match by To: domain pattern)", Location = new Point(8, 8), AutoSize = true, Checked = _isSuffixMode };
        _rdoRecipient = new RadioButton { Text = "Recipient rule  (exact To: address match)", Location = new Point(8, 30), AutoSize = true, Checked = !_isSuffixMode };
        _rdoSuffix.CheckedChanged    += (_, _) => RebuildScroll();
        _rdoRecipient.CheckedChanged += (_, _) => RebuildScroll();
        pnlType.Controls.AddRange(new Control[] { _rdoSuffix, _rdoRecipient });

        // ── Scrollable content ────────────────────────────────────────────────
        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // ── Bottom nav ────────────────────────────────────────────────────────
        var pnlNav = new Panel
        {
            Dock = DockStyle.Bottom, Height = 46,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        var btnSave   = new Button { Text = "Save",       Size = new Size(100, 30), Location = new Point(680 - 220, 8), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnCancel = new Button { Text = "Cancel",     Size = new Size(100, 30), Location = new Point(680 - 112, 8), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnVars   = new Button { Text = "Variables…", Size = new Size(100, 30), Location = new Point(10, 8),        Anchor = AnchorStyles.Bottom | AnchorStyles.Left,  FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        btnSave.Click   += (_, _) => SaveRule();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnVars.Click   += (_, _) => new VariablesHelpForm().ShowDialog(this);
        pnlNav.Controls.AddRange(new Control[] { btnSave, btnCancel, btnVars });

        Controls.Add(_scroll);
        Controls.Add(pnlType);
        Controls.Add(pnlNav);

        BuildScrollContent();
    }

    // ── Build scrollable content (called once; rebuilt on mode switch) ─────────

    private void BuildScrollContent()
    {
        _scroll.Controls.Clear();
        int y = 8;
        const int lx = 10; // left x for most controls
        const int w  = 600; // standard width

        bool isSuffix = _rdoSuffix.Checked;

        // ── Rule-type-specific fields ─────────────────────────────────────────
        if (isSuffix)
        {
            // Two fields side-by-side on one row
            Lbl(_scroll, "Suffix segment  (blank or * = wildcard, captures any subdomain as %suffix%):", lx, y);
            y += 18;
            _txtSuffix    = Txt(_scroll, lx, y, 200);
            Lbl(_scroll, "Base domain  (optional; blank = any domain):", lx + 220, y - 18);
            _txtBaseDomain = Txt(_scroll, lx + 220, y, 260, placeholder: "e.g. company.com");
            y += 26;
            SLabel(_scroll,
                "Example: suffix=files, domain=company.com matches jane@files.company.com\n" +
                "Wildcard: suffix blank, domain=company.com matches any jane@X.company.com; captures X as %suffix%",
                lx, ref y, 620);
        }
        else
        {
            Lbl(_scroll, "To: address  (exact match, case-insensitive):", lx, y);
            y += 18;
            _txtToAddress = Txt(_scroll, lx, y, 500, placeholder: "invoices@relay.local");
            y += 26;
        }

        y += 8;
        Sep(_scroll, lx, y, w); y += 10;

        // ── Destination type ──────────────────────────────────────────────────
        BoldLabel(_scroll, "Destination:", lx, y); y += 24;
        _rdoTypeRelay      = Rdo(_scroll, "Email Relay",  lx,       y);
        _rdoTypeOneDrive   = Rdo(_scroll, "OneDrive",     lx + 110, y);
        _rdoTypeSharePoint = Rdo(_scroll, "SharePoint",   lx + 210, y);
        _rdoTypeSmarthost  = Rdo(_scroll, "Smarthost",    lx + 310, y);
        _rdoTypeRelay.Checked = true;
        _rdoTypeRelay.CheckedChanged      += (_, _) => UpdateDestVisibility();
        _rdoTypeOneDrive.CheckedChanged   += (_, _) => UpdateDestVisibility();
        _rdoTypeSharePoint.CheckedChanged += (_, _) => UpdateDestVisibility();
        _rdoTypeSmarthost.CheckedChanged  += (_, _) => UpdateDestVisibility();
        y += 30;

        // ── Relay destination ─────────────────────────────────────────────────
        _pnlRelayDest = new Panel { Location = new Point(lx, y), Width = w, Height = 50 };
        Lbl(_pnlRelayDest, "Send via mailbox (empty = passthrough):", 0, 0);
        _txtRelayVia = Txt(_pnlRelayDest, 0, 18, 400);
        _scroll.Controls.Add(_pnlRelayDest);

        // ── OneDrive destination ──────────────────────────────────────────────
        _pnlOneDriveDest = new Panel { Location = new Point(lx, y), Width = w, Height = 80, Visible = false };
        Lbl(_pnlOneDriveDest, "User UPN  (blank = resolve from matched To: address):", 0, 0);
        _txtOneDriveUser = Txt(_pnlOneDriveDest, 0, 18, 400,
            placeholder: "e.g. user@company.com or leave blank for suffix rules");
        Lbl(_pnlOneDriveDest, "Folder path  (supports %variables%):", 0, 44);
        _txtOneDrivePath = Txt(_pnlOneDriveDest, 0, 62, 500,
            placeholder: "e.g. /Invoices/%date% or /%toupn%/%suffix%");
        _pnlOneDriveDest.Height = 82;
        _scroll.Controls.Add(_pnlOneDriveDest);

        // ── SharePoint destination ────────────────────────────────────────────
        _pnlSpDest = new Panel { Location = new Point(lx, y), Width = w, Height = 10, Visible = false };
        BuildSharePointPanel();
        _scroll.Controls.Add(_pnlSpDest);

        // ── Smarthost destination ─────────────────────────────────────────────
        _pnlSmarthostDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        BuildSmarthostDestPanel();
        _scroll.Controls.Add(_pnlSmarthostDest);

        y += Math.Max(
            _pnlRelayDest.Height,
            Math.Max(_pnlOneDriveDest.Height,
            Math.Max(_pnlSpDest.Height, _pnlSmarthostDest.Height))) + 8;

        // We'll position overrides below. Because SP panel height varies, we use
        // a placeholder that gets updated in UpdateDestVisibility.
        // For now, snap to the max. UpdateDestVisibility will reposition.
        int overrideY = y;

        Sep(_scroll, lx, overrideY, w); overrideY += 10;
        BoldLabel(_scroll, "Per-rule overrides  (leave unchecked to use global defaults):", lx, overrideY);
        overrideY += 26;

        // Save What
        _chkOverrideSaveWhat = Chk(_scroll, "Override save what:", lx, overrideY);
        _cboSaveWhat = new ComboBox { Location = new Point(lx + 195, overrideY - 2), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<SaveWhat>()) _cboSaveWhat.Items.Add(v);
        _cboSaveWhat.SelectedIndex = 0;
        _chkOverrideSaveWhat.CheckedChanged += (_, _) => _cboSaveWhat.Enabled = _chkOverrideSaveWhat.Checked;
        _scroll.Controls.Add(_cboSaveWhat);
        overrideY += 28;

        // Per-email subfolder
        _chkOverrideSubfolder = Chk(_scroll, "Override per-email subfolder:", lx, overrideY);
        _chkSubfolderValue = new CheckBox { Text = "Enabled", Location = new Point(lx + 235, overrideY), AutoSize = true, Enabled = false };
        _chkOverrideSubfolder.CheckedChanged += (_, _) => _chkSubfolderValue.Enabled = _chkOverrideSubfolder.Checked;
        _scroll.Controls.Add(_chkSubfolderValue);
        overrideY += 28;

        // From: sender
        _chkOverrideFromSender = Chk(_scroll, "Override From: sender handling:", lx, overrideY);
        _cboFromSender = new ComboBox { Location = new Point(lx + 243, overrideY - 2), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<FromSenderHandling>()) _cboFromSender.Items.Add(v);
        _cboFromSender.SelectedIndex = 0;
        _chkOverrideFromSender.CheckedChanged += (_, _) => _cboFromSender.Enabled = _chkOverrideFromSender.Checked;
        _scroll.Controls.Add(_cboFromSender);
        overrideY += 28;

        // Filename template override
        _chkOverrideFilenameTemplate = Chk(_scroll, "Override filename template:", lx, overrideY);
        overrideY += 22;
        _txtFilenameTemplate = new TextBox
        {
            Location = new Point(lx, overrideY), Width = 540,
            Font = new Font("Segoe UI", 9), Enabled = false,
            PlaceholderText = "e.g. %date%_%subject[0]%_%originalbasefilename%"
        };
        _chkOverrideFilenameTemplate.CheckedChanged += (_, _) => _txtFilenameTemplate.Enabled = _chkOverrideFilenameTemplate.Checked;
        _scroll.Controls.Add(_txtFilenameTemplate);
        overrideY += 28;

        // Subject delimiter override
        _chkOverrideSubjectDelimiter = Chk(_scroll, "Override subject delimiter:", lx, overrideY);
        _txtSubjectDelimiter = new TextBox
        {
            Location = new Point(lx + 220, overrideY - 2), Width = 80,
            Font = new Font("Segoe UI", 9), Enabled = false,
            PlaceholderText = "space"
        };
        _chkOverrideSubjectDelimiter.CheckedChanged += (_, _) => _txtSubjectDelimiter.Enabled = _chkOverrideSubjectDelimiter.Checked;
        _scroll.Controls.Add(_txtSubjectDelimiter);
        overrideY += 28;

        // Enabled checkbox
        _chkEnabled = new CheckBox { Text = "Rule enabled", Location = new Point(lx, overrideY + 4), AutoSize = true, Checked = true };
        _scroll.Controls.Add(_chkEnabled);

        // Store starting position for the override section so UpdateDestVisibility can reposition it
        _overrideSectionStartY = y; // the y after destination panels

        UpdateDestVisibility();
    }

    // y coordinate where override section begins (after the tallest destination panel)
    private int _overrideSectionStartY;

    private void BuildSharePointPanel()
    {
        int y = 0;
        const int w = 590;

        // Search
        Lbl(_pnlSpDest, "Search SharePoint sites (blank = load all, up to 500):", 0, y); y += 18;
        _txtSiteSearch = new TextBox { Location = new Point(0, y), Width = 390, Font = new Font("Segoe UI", 9), PlaceholderText = "type keyword or leave blank" };
        _txtSiteSearch.KeyPress += (_, e) => { if (e.KeyChar == (char)Keys.Return) { e.Handled = true; _ = SearchSitesAsync(); } };
        _btnSearchSites = new Button { Text = "Search", Location = new Point(396, y - 1), Size = new Size(80, 24), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        _btnSearchSites.Click += async (_, _) => await SearchSitesAsync();
        _pnlSpDest.Controls.AddRange(new Control[] { _txtSiteSearch, _btnSearchSites });
        y += 28;

        _cboSiteSearchResults = new ComboBox
        {
            Location = new Point(0, y), Width = w,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9),
            DropDownWidth = w + 60
        };
        _cboSiteSearchResults.SelectedIndexChanged += (_, _) =>
        {
            var idx = _cboSiteSearchResults.SelectedIndex;
            if (idx >= 0 && idx < _siteSearchResults.Count)
                _txtSiteUrl.Text = _siteSearchResults[idx].Url;
        };
        _pnlSpDest.Controls.Add(_cboSiteSearchResults);
        y += 30;

        // URL + resolve
        Lbl(_pnlSpDest, "SharePoint site URL:", 0, y); y += 18;
        _txtSiteUrl = new TextBox { Location = new Point(0, y), Width = 380, Font = new Font("Segoe UI", 9) };
        _btnResolveSite = new Button { Text = "Resolve / Load Libraries", Location = new Point(386, y - 1), Size = new Size(170, 24), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        _btnResolveSite.Click += async (_, _) => await ResolveSiteAsync();
        _pnlSpDest.Controls.AddRange(new Control[] { _txtSiteUrl, _btnResolveSite });
        y += 28;

        _lblSiteId = new Label { Text = "Site ID: (not resolved)", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) };
        _pnlSpDest.Controls.Add(_lblSiteId);
        y += 20;

        // Library
        Lbl(_pnlSpDest, "Document library:", 0, y); y += 18;
        _cboLibrary = new ComboBox { Location = new Point(0, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
        _cboLibrary.SelectedIndexChanged += (_, _) => UpdateLibraryDriveId();
        _pnlSpDest.Controls.Add(_cboLibrary);
        y += 28;

        _lblDriveId = new Label { Text = "Drive ID: (select a library)", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) };
        _pnlSpDest.Controls.Add(_lblDriveId);
        y += 20;

        // Folder path
        Lbl(_pnlSpDest, "Folder path within library  (supports %variables%):", 0, y); y += 18;
        _txtSpFolderPath = new TextBox
        {
            Location = new Point(0, y), Width = 390, Font = new Font("Segoe UI", 9),
            PlaceholderText = "e.g. /Invoices/%date% or /%suffix%/%toupn%"
        };
        _btnVerifyFolder = new Button { Text = "Verify / Create", Location = new Point(396, y - 1), Size = new Size(120, 24), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        _btnVerifyFolder.Click += async (_, _) => await VerifyFolderAsync();
        _pnlSpDest.Controls.AddRange(new Control[] { _txtSpFolderPath, _btnVerifyFolder });
        y += 28;

        _pnlSpDest.Height = y;
    }

    private void BuildSmarthostDestPanel()
    {
        int y = 0;

        _chkSmarthostUseGlobal = new CheckBox
        {
            Text = "Use global smarthost settings (configured in Settings → Smarthost Failover)",
            Location = new Point(0, y), AutoSize = true,
            Font = new Font("Segoe UI", 9), Checked = true
        };
        _chkSmarthostUseGlobal.CheckedChanged += (_, _) => UpdateSmarthostOverrideFields();
        _pnlSmarthostDest.Controls.Add(_chkSmarthostUseGlobal);
        y += 26;

        var hint = new Label
        {
            Text = "Mail matching this rule is always delivered via smarthost — not as a failover.\n" +
                   "Useful for departments or devices that must bypass Microsoft 365.",
            Location = new Point(0, y), AutoSize = false, Width = 570, Height = 34,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        };
        _pnlSmarthostDest.Controls.Add(hint);
        y += 42;

        Lbl(_pnlSmarthostDest, "Host (IP or hostname):", 0, y); y += 18;
        _txtSmarthostHostOverride = Txt(_pnlSmarthostDest, 0, y, 380,
            placeholder: "e.g. relay.company.com  or  192.168.1.100");
        y += 28;

        Lbl(_pnlSmarthostDest, "Port:", 0, y);
        Lbl(_pnlSmarthostDest, "TLS:", 110, y);
        y += 18;
        _nudSmarthostPortOverride = new NumericUpDown
        {
            Location = new Point(0, y), Width = 90,
            Minimum = 1, Maximum = 65535, Value = 587,
            Font = new Font("Segoe UI", 9)
        };
        _pnlSmarthostDest.Controls.Add(_nudSmarthostPortOverride);
        _cboSmarthostTlsOverride = new ComboBox
        {
            Location = new Point(110, y), Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        _cboSmarthostTlsOverride.Items.AddRange(new object[] { "None", "STARTTLS (Recommended)", "SSL/TLS" });
        _cboSmarthostTlsOverride.SelectedIndex = 1;
        _pnlSmarthostDest.Controls.Add(_cboSmarthostTlsOverride);
        y += 32;

        Lbl(_pnlSmarthostDest, "Username  (leave blank for unauthenticated relay):", 0, y); y += 18;
        _txtSmarthostUserOverride = Txt(_pnlSmarthostDest, 0, y, 340);
        y += 28;

        Lbl(_pnlSmarthostDest, "Password:", 0, y); y += 18;
        _txtSmarthostPassOverride = new TextBox
        {
            Location = new Point(0, y), Width = 340,
            UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 9)
        };
        _pnlSmarthostDest.Controls.Add(_txtSmarthostPassOverride);
        y += 28;

        _pnlSmarthostDest.Height = y;
        UpdateSmarthostOverrideFields();
    }

    private void UpdateSmarthostOverrideFields()
    {
        bool useGlobal = _chkSmarthostUseGlobal?.Checked ?? true;
        if (_txtSmarthostHostOverride != null)  _txtSmarthostHostOverride.Enabled  = !useGlobal;
        if (_nudSmarthostPortOverride != null)  _nudSmarthostPortOverride.Enabled  = !useGlobal;
        if (_cboSmarthostTlsOverride  != null)  _cboSmarthostTlsOverride.Enabled   = !useGlobal;
        if (_txtSmarthostUserOverride != null)  _txtSmarthostUserOverride.Enabled  = !useGlobal;
        if (_txtSmarthostPassOverride != null)  _txtSmarthostPassOverride.Enabled  = !useGlobal;
    }

    // ── Rebuild on mode switch ────────────────────────────────────────────────

    private void RebuildScroll()
    {
        // Cache values that are in shared controls before clearing
        // (shared controls like destination type are recreated)
        BuildScrollContent();
        PopulateFromEdit();
    }

    // ── Populate from existing rule ───────────────────────────────────────────

    private void PopulateFromEdit()
    {
        if (_editSuffix != null)
        {
            _rdoSuffix.Checked = true;
            if (_txtSuffix != null) _txtSuffix.Text = _editSuffix.Suffix;
            if (_txtBaseDomain != null) _txtBaseDomain.Text = _editSuffix.BaseDomain;
            PopulateDestination(_editSuffix.DestinationType,
                relayVia: "",
                oneDriveUser: _editSuffix.OneDriveUser,
                siteUrl: _editSuffix.SiteUrl,
                siteId: _editSuffix.SiteId,
                libName: _editSuffix.LibraryName,
                driveId: _editSuffix.LibraryDriveId,
                folderPath: _editSuffix.FolderPath,
                useSubfolder: _editSuffix.UsePerEmailSubfolder,
                saveWhat: _editSuffix.SaveWhat,
                fromSender: _editSuffix.FromSenderHandling,
                filenameTemplate: _editSuffix.FilenameTemplate,
                subjectDelimiter: _editSuffix.SubjectDelimiter,
                enabled: _editSuffix.Enabled,
                useGlobalSmarthost: _editSuffix.UseGlobalSmarthost,
                smarthostHost: _editSuffix.SmarthostOverrideHost,
                smarthostPort: _editSuffix.SmarthostOverridePort,
                smarthostTls: _editSuffix.SmarthostOverrideTls,
                smarthostUser: _editSuffix.SmarthostOverrideUsername,
                smarthostPass: _editSuffix.SmarthostOverridePassword);
        }
        else if (_editRecipient != null)
        {
            _rdoRecipient.Checked = true;
            if (_txtToAddress != null) _txtToAddress.Text = _editRecipient.ToAddress;
            PopulateDestination(_editRecipient.DestinationType,
                relayVia: _editRecipient.RelayVia,
                oneDriveUser: _editRecipient.OneDriveUser,
                siteUrl: _editRecipient.SiteUrl,
                siteId: _editRecipient.SiteId,
                libName: _editRecipient.LibraryName,
                driveId: _editRecipient.LibraryDriveId,
                folderPath: _editRecipient.FolderPath,
                useSubfolder: _editRecipient.UsePerEmailSubfolder,
                saveWhat: _editRecipient.SaveWhat,
                fromSender: _editRecipient.FromSenderHandling,
                filenameTemplate: _editRecipient.FilenameTemplate,
                subjectDelimiter: _editRecipient.SubjectDelimiter,
                enabled: _editRecipient.Enabled,
                useGlobalSmarthost: _editRecipient.UseGlobalSmarthost,
                smarthostHost: _editRecipient.SmarthostOverrideHost,
                smarthostPort: _editRecipient.SmarthostOverridePort,
                smarthostTls: _editRecipient.SmarthostOverrideTls,
                smarthostUser: _editRecipient.SmarthostOverrideUsername,
                smarthostPass: _editRecipient.SmarthostOverridePassword);
        }
    }

    private void PopulateDestination(
        FileDestinationType type, string relayVia, string oneDriveUser,
        string siteUrl, string siteId, string libName, string driveId,
        string folderPath, bool? useSubfolder, SaveWhat? saveWhat,
        FromSenderHandling fromSender,
        string? filenameTemplate, string? subjectDelimiter,
        bool enabled,
        bool useGlobalSmarthost = true, string smarthostHost = "",
        int smarthostPort = 587, SmarthostTls smarthostTls = SmarthostTls.StartTls,
        string smarthostUser = "", string smarthostPass = "")
    {
        _rdoTypeRelay.Checked      = type == FileDestinationType.EmailRelay;
        _rdoTypeOneDrive.Checked   = type == FileDestinationType.OneDrive;
        _rdoTypeSharePoint.Checked = type == FileDestinationType.SharePoint;
        if (_rdoTypeSmarthost != null)
            _rdoTypeSmarthost.Checked = type == FileDestinationType.SmarthostRelay;

        if (_chkSmarthostUseGlobal != null) _chkSmarthostUseGlobal.Checked = useGlobalSmarthost;
        if (_txtSmarthostHostOverride != null) _txtSmarthostHostOverride.Text = smarthostHost;
        if (_nudSmarthostPortOverride != null) _nudSmarthostPortOverride.Value = Math.Clamp(smarthostPort, 1, 65535);
        if (_cboSmarthostTlsOverride  != null) _cboSmarthostTlsOverride.SelectedIndex = (int)smarthostTls;
        if (_txtSmarthostUserOverride != null) _txtSmarthostUserOverride.Text = smarthostUser;
        if (_txtSmarthostPassOverride != null) _txtSmarthostPassOverride.Text = smarthostPass;

        if (_txtRelayVia != null)     _txtRelayVia.Text     = relayVia;
        if (_txtOneDriveUser != null) _txtOneDriveUser.Text = oneDriveUser;
        if (_txtOneDrivePath != null) _txtOneDrivePath.Text = type != FileDestinationType.SharePoint ? folderPath : "";
        if (_txtSiteUrl != null)      _txtSiteUrl.Text      = siteUrl;
        if (_txtSpFolderPath != null) _txtSpFolderPath.Text = type == FileDestinationType.SharePoint ? folderPath : "";
        _resolvedSiteId = siteId;
        if (_lblSiteId != null)
            _lblSiteId.Text = siteId.Length > 0 ? $"Site ID: {siteId}" : "Site ID: (not resolved)";

        if (!string.IsNullOrWhiteSpace(libName) && !string.IsNullOrWhiteSpace(driveId))
        {
            _libraries = new List<(string, string)> { (libName, driveId) };
            _cboLibrary?.Items.Clear();
            _cboLibrary?.Items.Add(libName);
            if (_cboLibrary != null) _cboLibrary.SelectedIndex = 0;
            if (_lblDriveId != null) _lblDriveId.Text = $"Drive ID: {driveId}";
        }

        if (useSubfolder.HasValue)
        {
            _chkOverrideSubfolder.Checked = true;
            _chkSubfolderValue.Checked    = useSubfolder.Value;
        }
        if (saveWhat.HasValue)
        {
            _chkOverrideSaveWhat.Checked = true;
            _cboSaveWhat.SelectedItem    = saveWhat.Value.ToString();
        }
        if (fromSender != FromSenderHandling.Ignore)
        {
            _chkOverrideFromSender.Checked = true;
            _cboFromSender.SelectedItem    = fromSender.ToString();
        }
        if (!string.IsNullOrWhiteSpace(filenameTemplate))
        {
            _chkOverrideFilenameTemplate.Checked = true;
            _txtFilenameTemplate.Text            = filenameTemplate;
        }
        if (subjectDelimiter is not null)
        {
            _chkOverrideSubjectDelimiter.Checked = true;
            _txtSubjectDelimiter.Text            = subjectDelimiter == " " ? "" : subjectDelimiter;
        }

        _chkEnabled.Checked = enabled;
        UpdateDestVisibility();
    }

    // ── Destination visibility + repositioning overrides ──────────────────────

    private void UpdateDestVisibility()
    {
        bool isRelay     = _rdoTypeRelay?.Checked ?? true;
        bool isOD        = _rdoTypeOneDrive?.Checked ?? false;
        bool isSp        = _rdoTypeSharePoint?.Checked ?? false;
        bool isSmarthost = _rdoTypeSmarthost?.Checked ?? false;

        if (_pnlRelayDest    != null) _pnlRelayDest.Visible    = isRelay;
        if (_pnlOneDriveDest != null) _pnlOneDriveDest.Visible = isOD;
        if (_pnlSpDest       != null) _pnlSpDest.Visible       = isSp;
        if (_pnlSmarthostDest != null) _pnlSmarthostDest.Visible = isSmarthost;

        // Reposition controls below the active destination panel
        int activeHeight = isRelay     ? (_pnlRelayDest?.Height     ?? 50)
                         : isOD       ? (_pnlOneDriveDest?.Height   ?? 80)
                         : isSmarthost ? (_pnlSmarthostDest?.Height ?? 200)
                                       : (_pnlSpDest?.Height        ?? 280);

        int newY = _overrideSectionStartY + activeHeight + 8;
        RepositionOverrides(newY);

        // Grey out file-storage-only overrides when Email Relay or Smarthost is the destination.
        bool fs = !isRelay && !isSmarthost;
        void SetPair(CheckBox? chk, Control? sub)
        {
            if (chk == null) return;
            chk.Enabled = fs;
            if (sub != null) sub.Enabled = fs && chk.Checked;
        }
        SetPair(_chkOverrideSaveWhat,         _cboSaveWhat);
        SetPair(_chkOverrideSubfolder,        _chkSubfolderValue);
        SetPair(_chkOverrideFilenameTemplate, _txtFilenameTemplate);
        SetPair(_chkOverrideSubjectDelimiter, _txtSubjectDelimiter);
    }

    private void RepositionOverrides(int startY)
    {
        // Find all controls that sit at or after the previous startY and shift them
        // We identify them by being after the destination panels
        // The separator before overrides has a known relative position:
        // startY, startY+10 (bold label), +26, +28... We just look for controls
        // positioned >= _overrideSectionStartY and shift them.
        int delta = startY - _lastOverrideY;
        if (delta == 0) return;
        _lastOverrideY = startY;

        foreach (Control c in _scroll.Controls)
        {
            if (c == _pnlRelayDest || c == _pnlOneDriveDest || c == _pnlSpDest) continue;
            if (c.Location.Y >= _overrideSectionStartY)
                c.Location = new Point(c.Location.X, c.Location.Y + delta);
        }
    }

    private int _lastOverrideY;

    // ── Site search ───────────────────────────────────────────────────────────

    private async Task SearchSitesAsync()
    {
        _btnSearchSites.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var fileStorer = new GraphFileStorer(_configManager, mailSender, _logger);

            var query = _txtSiteSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) query = "*";

            _siteSearchResults = await fileStorer.SearchSitesAsync(query, default);

            _cboSiteSearchResults.Items.Clear();
            if (_siteSearchResults.Count == 0)
            {
                MessageBox.Show("No sites found.", "Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var (name, url) in _siteSearchResults)
                _cboSiteSearchResults.Items.Add($"{name}  ({url})");

            if (_siteSearchResults.Count == 1)
                _cboSiteSearchResults.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Site search failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearchSites.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    // ── Site resolution ───────────────────────────────────────────────────────

    private async Task ResolveSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtSiteUrl.Text))
        {
            MessageBox.Show("Enter a SharePoint site URL first.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnResolveSite.Enabled = false;
        _lblSiteId.Text = "Resolving…";
        Cursor = Cursors.WaitCursor;

        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var fileStorer = new GraphFileStorer(_configManager, mailSender, _logger);

            _resolvedSiteId = await fileStorer.ResolveSiteIdAsync(_txtSiteUrl.Text.Trim(), default);
            _lblSiteId.Text = $"Site ID: {_resolvedSiteId}";

            _libraries = await fileStorer.GetLibrariesAsync(_resolvedSiteId, default);
            _cboLibrary.Items.Clear();
            foreach (var (name, _) in _libraries) _cboLibrary.Items.Add(name);
            if (_cboLibrary.Items.Count > 0) _cboLibrary.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _lblSiteId.Text = "Resolution failed";
            MessageBox.Show($"Could not resolve site:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnResolveSite.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private async Task VerifyFolderAsync()
    {
        var driveId = GetSelectedDriveId();
        if (string.IsNullOrWhiteSpace(driveId))
        {
            MessageBox.Show("Resolve the site and select a library first.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnVerifyFolder.Enabled = false;
        Cursor = Cursors.WaitCursor;

        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var fileStorer = new GraphFileStorer(_configManager, mailSender, _logger);
            var path = _txtSpFolderPath.Text.Trim();

            bool exists = await fileStorer.FolderExistsAsync(driveId, path, default);
            if (exists)
            {
                MessageBox.Show($"Folder '{path}' exists.", "Verified",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                if (MessageBox.Show($"Folder '{path}' does not exist. Create it now?",
                    "Folder Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    await fileStorer.EnsureFolderPathAsync(driveId, path, default);
                    MessageBox.Show("Folder created.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Folder verification failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnVerifyFolder.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void UpdateLibraryDriveId()
    {
        var idx = _cboLibrary.SelectedIndex;
        if (idx >= 0 && idx < _libraries.Count)
            _lblDriveId.Text = $"Drive ID: {_libraries[idx].DriveId}";
    }

    private string GetSelectedDriveId()
    {
        var idx = _cboLibrary.SelectedIndex;
        return idx >= 0 && idx < _libraries.Count ? _libraries[idx].DriveId : "";
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void SaveRule()
    {
        var destType = _rdoTypeOneDrive.Checked   ? FileDestinationType.OneDrive
                     : _rdoTypeSharePoint.Checked ? FileDestinationType.SharePoint
                     : _rdoTypeSmarthost.Checked  ? FileDestinationType.SmarthostRelay
                                                  : FileDestinationType.EmailRelay;

        bool? subfolder = _chkOverrideSubfolder.Checked ? _chkSubfolderValue.Checked : null;
        SaveWhat? saveWhat = _chkOverrideSaveWhat.Checked
            ? Enum.Parse<SaveWhat>(_cboSaveWhat.SelectedItem?.ToString() ?? "AttachmentsOnly")
            : null;
        FromSenderHandling fromSender = _chkOverrideFromSender.Checked
            ? Enum.Parse<FromSenderHandling>(_cboFromSender.SelectedItem?.ToString() ?? "Ignore")
            : FromSenderHandling.Ignore;
        string? filenameTemplate = _chkOverrideFilenameTemplate.Checked
            ? _txtFilenameTemplate.Text.Trim()
            : null;
        string? subjectDelimiter = _chkOverrideSubjectDelimiter.Checked
            ? (string.IsNullOrEmpty(_txtSubjectDelimiter.Text) ? " " : _txtSubjectDelimiter.Text)
            : null;

        var libName  = _cboLibrary.SelectedItem?.ToString() ?? "";
        var driveId  = GetSelectedDriveId();
        var folderPath = destType == FileDestinationType.SharePoint
            ? _txtSpFolderPath.Text.Trim()
            : _txtOneDrivePath.Text.Trim();

        if (_rdoSuffix.Checked)
        {
            var suffix = _txtSuffix?.Text.Trim() ?? "";
            var base_  = _txtBaseDomain?.Text.Trim() ?? "";

            // Both blank = too broad (would match everything)
            if (string.IsNullOrWhiteSpace(suffix) && string.IsNullOrWhiteSpace(base_))
            {
                MessageBox.Show(
                    "Wildcard suffix rules require a Base Domain to avoid matching all traffic.\n" +
                    "Please enter a Base Domain (e.g. company.com).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SuffixResult = new SuffixRule
            {
                Id             = _editSuffix?.Id ?? Guid.NewGuid().ToString("N")[..8],
                Suffix         = suffix,
                BaseDomain     = base_,
                Enabled        = _chkEnabled.Checked,
                DestinationType = destType,
                OneDriveUser   = _txtOneDriveUser?.Text.Trim() ?? "",
                SiteUrl        = _txtSiteUrl?.Text.Trim() ?? "",
                SiteId         = _resolvedSiteId,
                LibraryName    = libName,
                LibraryDriveId = driveId,
                FolderPath     = folderPath,
                UsePerEmailSubfolder = subfolder,
                SaveWhat       = saveWhat,
                FromSenderHandling = fromSender,
                FilenameTemplate = filenameTemplate,
                SubjectDelimiter = subjectDelimiter,
                UseGlobalSmarthost         = _chkSmarthostUseGlobal?.Checked ?? true,
                SmarthostOverrideHost      = _txtSmarthostHostOverride?.Text.Trim() ?? "",
                SmarthostOverridePort      = (int)(_nudSmarthostPortOverride?.Value ?? 587),
                SmarthostOverrideTls       = (SmarthostTls)(_cboSmarthostTlsOverride?.SelectedIndex ?? 1),
                SmarthostOverrideUsername  = _txtSmarthostUserOverride?.Text.Trim() ?? "",
                SmarthostOverridePassword  = _txtSmarthostPassOverride?.Text ?? "",
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_txtToAddress?.Text))
            {
                MessageBox.Show("To: address is required.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RecipientResult = new RecipientFileRule
            {
                Id             = _editRecipient?.Id ?? Guid.NewGuid().ToString("N")[..8],
                ToAddress      = _txtToAddress!.Text.Trim(),
                Enabled        = _chkEnabled.Checked,
                DestinationType = destType,
                RelayVia       = _txtRelayVia?.Text.Trim() ?? "",
                OneDriveUser   = _txtOneDriveUser?.Text.Trim() ?? "",
                SiteUrl        = _txtSiteUrl?.Text.Trim() ?? "",
                SiteId         = _resolvedSiteId,
                LibraryName    = libName,
                LibraryDriveId = driveId,
                FolderPath     = folderPath,
                UsePerEmailSubfolder = subfolder,
                SaveWhat       = saveWhat,
                FromSenderHandling = fromSender,
                FilenameTemplate = filenameTemplate,
                SubjectDelimiter = subjectDelimiter,
                UseGlobalSmarthost         = _chkSmarthostUseGlobal?.Checked ?? true,
                SmarthostOverrideHost      = _txtSmarthostHostOverride?.Text.Trim() ?? "",
                SmarthostOverridePort      = (int)(_nudSmarthostPortOverride?.Value ?? 587),
                SmarthostOverrideTls       = (SmarthostTls)(_cboSmarthostTlsOverride?.SelectedIndex ?? 1),
                SmarthostOverrideUsername  = _txtSmarthostUserOverride?.Text.Trim() ?? "",
                SmarthostOverridePassword  = _txtSmarthostPassOverride?.Text ?? "",
            };
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void Lbl(Control parent, string text, int x, int y, int width = 0)
    {
        var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = width == 0, Font = new Font("Segoe UI", 9) };
        if (width > 0) { lbl.Width = width; lbl.AutoSize = false; }
        parent.Controls.Add(lbl);
    }

    private static void BoldLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
    }

    private static void SLabel(Control parent, string text, int x, ref int y, int width)
    {
        var lbl = new Label
        {
            Text = text, Location = new Point(x, y), Width = width, Height = 34,
            AutoSize = false, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        };
        parent.Controls.Add(lbl);
        y += 36;
    }

    private static TextBox Txt(Control parent, int x, int y, int width, string placeholder = "")
    {
        var tb = new TextBox
        {
            Location = new Point(x, y), Width = width,
            Font = new Font("Segoe UI", 9),
            PlaceholderText = placeholder
        };
        parent.Controls.Add(tb);
        return tb;
    }

    private static RadioButton Rdo(Control parent, string text, int x, int y)
    {
        var r = new RadioButton { Text = text, Location = new Point(x, y), AutoSize = true };
        parent.Controls.Add(r);
        return r;
    }

    private static CheckBox Chk(Control parent, string text, int x, int y)
    {
        var c = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Font = new Font("Segoe UI", 9) };
        parent.Controls.Add(c);
        return c;
    }

    private static void Sep(Control parent, int x, int y, int width)
    {
        parent.Controls.Add(new Panel
        {
            Location = new Point(x, y), Size = new Size(width, 1),
            BackColor = Color.FromArgb(210, 210, 220)
        });
    }
}
