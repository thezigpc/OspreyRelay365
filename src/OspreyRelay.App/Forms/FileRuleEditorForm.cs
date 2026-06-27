using System.Text.RegularExpressions;
using OspreyRelay.Core.Config;
using OspreyRelay.M365.Graph;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.App.Forms;

/// <summary>
/// Editor for a single RoutingRule. Opened from FileRoutingRulesForm for add and edit operations.
/// </summary>
public class FileRuleEditorForm : Form
{
    // ── Output ────────────────────────────────────────────────────────────────
    public RoutingRule? Result { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly RoutingRule? _editRule;
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    // ── Match mode selector (fixed top) ───────────────────────────────────────
    private ComboBox _cboMatchMode = null!;

    // ── Match section controls (rebuilt on mode change) ───────────────────────
    // DomainSuffix
    private TextBox? _txtSuffix;
    private TextBox? _txtBaseDomain;
    // ExactTo + all Regex modes
    private TextBox? _txtPattern;
    // Regex modes only
    private CheckBox? _chkCaseInsensitive;
    private TextBox? _txtTestInput;
    private Label? _lblTestResult;

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
    private CheckBox _chkOverrideSaveEmbeddedImages = null!;
    private CheckBox _chkSaveEmbeddedImagesValue = null!;
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

    // Delivery address overrides — relay panel
    private CheckBox? _chkStripSuffixRelay;
    private TextBox _txtDeliverToOverrideRelay = null!;

    // Delivery address overrides — smarthost panel
    private CheckBox? _chkStripSuffixSmarthost;
    private TextBox _txtDeliverToOverrideSmarthost = null!;
    private CheckBox _chkRewriteToHeader = null!;

    // Resolved SP data
    private string _resolvedSiteId = "";
    private List<(string Name, string DriveId)> _libraries = new();
    private List<(string DisplayName, string Url)> _siteSearchResults = new();

    // Scroll container
    private Panel _scroll = null!;
    private int _destPanelY;
    private int _lastOverrideY;

    // ── Constructor ───────────────────────────────────────────────────────────

    public FileRuleEditorForm(RoutingRule? editRule, ConfigManager configManager, RelayLogger logger)
    {
        _editRule      = editRule;
        _configManager = configManager;
        _logger        = logger;

        InitializeComponent();
        PopulateFromEdit();
    }

    private MatchMode CurrentMode => _cboMatchMode.SelectedIndex switch
    {
        0 => MatchMode.DomainSuffix,
        1 => MatchMode.ExactTo,
        2 => MatchMode.RegexTo,
        3 => MatchMode.RegexFrom,
        4 => MatchMode.RegexSubject,
        _ => MatchMode.DomainSuffix
    };

    private void InitializeComponent()
    {
        Text = "Rule Editor";
        Size = new Size(700, 720);
        MinimumSize = new Size(640, 580);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        // ── Match mode selector (fixed top) ───────────────────────────────────
        var pnlTop = new Panel
        {
            Dock = DockStyle.Top, Height = 42,
            Padding = new Padding(10, 8, 10, 0),
            BackColor = Color.FromArgb(245, 245, 250)
        };
        var lblMode = new Label
        {
            Text = "Match mode:", Location = new Point(10, 12),
            AutoSize = true, Font = new Font("Segoe UI", 9)
        };
        _cboMatchMode = new ComboBox
        {
            Location = new Point(102, 9), Width = 270,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        _cboMatchMode.Items.AddRange(new object[]
        {
            "Domain Suffix  (subdomain pattern)",
            "Exact To:  (case-insensitive address)",
            "Regex — To: address",
            "Regex — From: address",
            "Regex — Subject line"
        });
        _cboMatchMode.SelectedIndex = 0;
        _cboMatchMode.SelectedIndexChanged += (_, _) => RebuildMatchSection();
        pnlTop.Controls.AddRange(new Control[] { lblMode, _cboMatchMode });

        // ── Scrollable content ────────────────────────────────────────────────
        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // ── Bottom nav ────────────────────────────────────────────────────────
        var btnSave   = new Button { Text = "Save",       Size = new Size(100, 30), Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnCancel = new Button { Text = "Cancel",     Size = new Size(100, 30), Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        var btnVars   = new Button { Text = "Variables…", Size = new Size(100, 30), Dock = DockStyle.Left,  FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        btnSave.Click   += (_, _) => SaveRule();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnVars.Click   += (_, _) => new VariablesHelpForm().ShowDialog(this);
        var pnlNav = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 245, 248) };
        pnlNav.Controls.Add(btnSave);
        pnlNav.Controls.Add(btnCancel);
        pnlNav.Controls.Add(btnVars);

        var tlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Padding = new Padding(0), Margin = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tlp.RowStyles.Clear();
        tlp.ColumnStyles.Clear();
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tlp.Controls.Add(_scroll, 0, 0);
        tlp.Controls.Add(pnlNav, 0, 1);

        Controls.Add(tlp);
        Controls.Add(pnlTop);

        BuildScrollContent();
    }

    // ── Build the full scroll content ─────────────────────────────────────────

    private void BuildScrollContent()
    {
        _scroll.Controls.Clear();
        int y = 8;
        const int lx = 10;
        const int w  = 620;

        BuildMatchSection(lx, ref y, w);

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

        _destPanelY = y;

        // ── Relay destination ─────────────────────────────────────────────────
        _pnlRelayDest = new Panel { Location = new Point(lx, y), Width = w };
        BuildRelayDestPanel();
        _scroll.Controls.Add(_pnlRelayDest);

        // ── OneDrive destination ──────────────────────────────────────────────
        _pnlOneDriveDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        Lbl(_pnlOneDriveDest, "User UPN  (blank = resolve from matched To: address):", 0, 0);
        _txtOneDriveUser = Txt(_pnlOneDriveDest, 0, 18, 420,
            placeholder: "e.g. user@company.com");
        Lbl(_pnlOneDriveDest, "Folder path  (supports %variables%):", 0, 44);
        _txtOneDrivePath = Txt(_pnlOneDriveDest, 0, 62, 520,
            placeholder: "e.g. /Invoices/%date% or /%toupn%/%suffix%");
        _pnlOneDriveDest.Height = 84;
        _scroll.Controls.Add(_pnlOneDriveDest);

        // ── SharePoint destination ────────────────────────────────────────────
        _pnlSpDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        BuildSharePointPanel();
        _scroll.Controls.Add(_pnlSpDest);

        // ── Smarthost destination ─────────────────────────────────────────────
        _pnlSmarthostDest = new Panel { Location = new Point(lx, y), Width = w, Visible = false };
        BuildSmarthostDestPanel();
        _scroll.Controls.Add(_pnlSmarthostDest);

        y += Math.Max(_pnlRelayDest.Height,
             Math.Max(_pnlOneDriveDest.Height,
             Math.Max(_pnlSpDest.Height, _pnlSmarthostDest.Height))) + 8;

        _lastOverrideY = y;

        // ── Per-rule overrides ────────────────────────────────────────────────
        Sep(_scroll, lx, y, w); y += 10;
        BoldLabel(_scroll, "Per-rule overrides  (leave unchecked to use global defaults):", lx, y);
        y += 26;

        _chkOverrideSaveWhat = Chk(_scroll, "Override save what:", lx, y);
        _cboSaveWhat = new ComboBox { Location = new Point(lx + 195, y - 2), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<SaveWhat>()) _cboSaveWhat.Items.Add(v);
        _cboSaveWhat.SelectedIndex = 0;
        _chkOverrideSaveWhat.CheckedChanged += (_, _) => _cboSaveWhat.Enabled = _chkOverrideSaveWhat.Checked;
        _scroll.Controls.Add(_cboSaveWhat);
        y += 28;

        _chkOverrideSubfolder = Chk(_scroll, "Override per-email subfolder:", lx, y);
        _chkSubfolderValue = new CheckBox { Text = "Enabled", Location = new Point(lx + 235, y), AutoSize = true, Enabled = false };
        _chkOverrideSubfolder.CheckedChanged += (_, _) => _chkSubfolderValue.Enabled = _chkOverrideSubfolder.Checked;
        _scroll.Controls.Add(_chkSubfolderValue);
        y += 28;

        _chkOverrideFromSender = Chk(_scroll, "Override From: sender handling:", lx, y);
        _cboFromSender = new ComboBox { Location = new Point(lx + 243, y - 2), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9), Enabled = false };
        foreach (var v in Enum.GetNames<FromSenderHandling>()) _cboFromSender.Items.Add(v);
        _cboFromSender.SelectedIndex = 0;
        _chkOverrideFromSender.CheckedChanged += (_, _) => _cboFromSender.Enabled = _chkOverrideFromSender.Checked;
        _scroll.Controls.Add(_cboFromSender);
        y += 28;

        _chkOverrideSaveEmbeddedImages = Chk(_scroll, "Override save embedded images:", lx, y);
        _chkSaveEmbeddedImagesValue = new CheckBox { Text = "Enabled", Location = new Point(lx + 235, y), AutoSize = true, Enabled = false };
        _chkOverrideSaveEmbeddedImages.CheckedChanged += (_, _) => _chkSaveEmbeddedImagesValue.Enabled = _chkOverrideSaveEmbeddedImages.Checked;
        _scroll.Controls.Add(_chkSaveEmbeddedImagesValue);
        y += 28;

        _chkOverrideFilenameTemplate = Chk(_scroll, "Override filename template:", lx, y);
        y += 22;
        _txtFilenameTemplate = new TextBox
        {
            Location = new Point(lx, y), Width = 570,
            Font = new Font("Segoe UI", 9), Enabled = false,
            PlaceholderText = "e.g. %date%_%subject[0]%_%originalbasefilename%"
        };
        _chkOverrideFilenameTemplate.CheckedChanged += (_, _) => _txtFilenameTemplate.Enabled = _chkOverrideFilenameTemplate.Checked;
        _scroll.Controls.Add(_txtFilenameTemplate);
        y += 28;

        _chkOverrideSubjectDelimiter = Chk(_scroll, "Override subject delimiter:", lx, y);
        _txtSubjectDelimiter = new TextBox
        {
            Location = new Point(lx + 220, y - 2), Width = 80,
            Font = new Font("Segoe UI", 9), Enabled = false,
            PlaceholderText = "space"
        };
        _chkOverrideSubjectDelimiter.CheckedChanged += (_, _) => _txtSubjectDelimiter.Enabled = _chkOverrideSubjectDelimiter.Checked;
        _scroll.Controls.Add(_txtSubjectDelimiter);
        y += 28;

        _chkEnabled = new CheckBox { Text = "Rule enabled", Location = new Point(lx, y + 4), AutoSize = true, Checked = true };
        _scroll.Controls.Add(_chkEnabled);

        UpdateDestVisibility();
    }

    // ── Match section (rebuilt when mode changes) ─────────────────────────────

    private static readonly string[] _matchSectionTags = ["__matchsection__"];

    private void BuildMatchSection(int lx, ref int y, int w)
    {
        // All match-section controls are tagged so RebuildMatchSection can find them
        var mode = CurrentMode;

        if (mode == MatchMode.DomainSuffix)
        {
            Lbl(_scroll, "Suffix segment  (blank or * = wildcard — captures any subdomain as %suffix%):", lx, y).Tag = "match";
            y += 18;
            _txtSuffix     = Txt(_scroll, lx, y, 200); _txtSuffix.Tag = "match";
            Lbl(_scroll, "Base domain  (optional; blank = any domain):", lx + 220, y - 18).Tag = "match";
            _txtBaseDomain = Txt(_scroll, lx + 220, y, 280, placeholder: "e.g. company.com"); _txtBaseDomain.Tag = "match";
            y += 28;
            var hint = new Label
            {
                Text = "Example: suffix=files, domain=company.com matches jane@files.company.com\n" +
                       "Wildcard (suffix blank): domain=company.com matches any jane@X.company.com — captures X as %suffix%",
                Location = new Point(lx, y), Width = w, Height = 34, AutoSize = false,
                ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Tag = "match"
            };
            _scroll.Controls.Add(hint);
            y += 36;
        }
        else if (mode == MatchMode.ExactTo)
        {
            Lbl(_scroll, "To: address  (exact match, case-insensitive):", lx, y).Tag = "match";
            y += 18;
            _txtPattern = Txt(_scroll, lx, y, 500, placeholder: "invoices@relay.local"); _txtPattern.Tag = "match";
            y += 28;
        }
        else
        {
            var fieldLabel = mode switch
            {
                MatchMode.RegexFrom    => "From: address pattern  (matched against envelope From:):",
                MatchMode.RegexSubject => "Subject pattern  (matched against message Subject: header):",
                _                     => "To: address pattern  (matched against each envelope To:):"
            };
            Lbl(_scroll, fieldLabel, lx, y).Tag = "match";
            y += 18;
            _txtPattern = Txt(_scroll, lx, y, 480, placeholder: @"e.g. (?<type>INVOICE|PO)-(?<num>\d+)"); _txtPattern.Tag = "match";

            var btnTest = new Button
            {
                Text = "Test…", Location = new Point(lx + 490, y - 1), Size = new Size(60, 24),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true, Tag = "match"
            };
            btnTest.Click += (_, _) => RunPatternTest();
            _scroll.Controls.Add(btnTest);
            y += 28;

            _chkCaseInsensitive = new CheckBox
            {
                Text = "Case-insensitive", Location = new Point(lx, y), AutoSize = true,
                Checked = true, Font = new Font("Segoe UI", 9), Tag = "match"
            };
            _scroll.Controls.Add(_chkCaseInsensitive);
            y += 24;

            _txtTestInput = new TextBox
            {
                Location = new Point(lx, y), Width = 480,
                Font = new Font("Segoe UI", 9),
                PlaceholderText = "Sample input to test — then click Test…",
                Tag = "match"
            };
            _scroll.Controls.Add(_txtTestInput);
            y += 26;

            _lblTestResult = new Label
            {
                Location = new Point(lx, y), Width = w, Height = 40, AutoSize = false,
                Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray, Tag = "match",
                Text = "Enter a sample above and click Test… to see match result and captured variables."
            };
            _scroll.Controls.Add(_lblTestResult);
            y += 44;

            var hintRx = new Label
            {
                Text = "Named groups (?<name>...) become %name% in path templates. " +
                       "Numbered groups become %match1%, %match2%, etc.",
                Location = new Point(lx, y), Width = w, Height = 28, AutoSize = false,
                ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Tag = "match"
            };
            _scroll.Controls.Add(hintRx);
            y += 30;
        }
    }

    private void RebuildMatchSection()
    {
        // Remove all tagged match-section controls
        var toRemove = _scroll.Controls.Cast<Control>()
            .Where(c => "match".Equals(c.Tag?.ToString()))
            .ToList();

        // Remember how many pixels the old match section occupied
        int oldBottom = toRemove.Count > 0
            ? toRemove.Max(c => c.Bottom) + 8
            : 0;

        foreach (var c in toRemove)
            _scroll.Controls.Remove(c);

        // Re-nullify mode-specific references
        _txtSuffix = null; _txtBaseDomain = null;
        _txtPattern = null; _chkCaseInsensitive = null;
        _txtTestInput = null; _lblTestResult = null;

        // Build new match section at the same top position
        int startY = 8;
        BuildMatchSection(10, ref startY, 620);
        int newBottom = startY + 8; // +8 for the sep gap

        // Shift all non-match controls by the delta
        int delta = newBottom - oldBottom;
        if (delta != 0)
        {
            foreach (Control c in _scroll.Controls)
            {
                if ("match".Equals(c.Tag?.ToString())) continue;
                if (c.Location.Y >= oldBottom)
                    c.Location = new Point(c.Location.X, c.Location.Y + delta);
            }
            // Update tracked Y references
            _destPanelY   += delta;
            _lastOverrideY += delta;
            foreach (var pnl in new[] { _pnlRelayDest, _pnlOneDriveDest, _pnlSpDest, _pnlSmarthostDest })
                if (pnl != null)
                    pnl.Location = new Point(pnl.Location.X, pnl.Location.Y + delta);
        }

        // Rebuild strip-suffix checkbox visibility in relay/smarthost panels
        // (only shown for DomainSuffix mode)
        RebuildStripSuffixInRelayPanel();
        RebuildStripSuffixInSmarthostPanel();
    }

    // ── Relay destination panel ───────────────────────────────────────────────

    private void BuildRelayDestPanel()
    {
        int ry = 0;
        Lbl(_pnlRelayDest, "Send via mailbox  (empty = passthrough):", 0, ry); ry += 18;
        _txtRelayVia = Txt(_pnlRelayDest, 0, ry, 400); ry += 30;
        Sep(_pnlRelayDest, 0, ry, 590); ry += 12;

        _chkStripSuffixRelay = null;
        if (CurrentMode == MatchMode.DomainSuffix)
        {
            _chkStripSuffixRelay = Chk(_pnlRelayDest,
                "Strip suffix segment from recipient address before delivery  (e.g. john@files.co → john@co)",
                0, ry);
            _chkStripSuffixRelay.Tag = "striprow_relay";
            ry += 24;
        }

        Lbl(_pnlRelayDest, "Override recipient address  (optional; takes priority over strip if set):", 0, ry); ry += 18;
        _txtDeliverToOverrideRelay = Txt(_pnlRelayDest, 0, ry, 420,
            placeholder: "e.g. support@company.com — leave blank to use original or stripped");
        ry += 28;
        _pnlRelayDest.Height = ry;
    }

    private void RebuildStripSuffixInRelayPanel()
    {
        if (_pnlRelayDest == null) return;
        var existing = _pnlRelayDest.Controls.Cast<Control>()
            .FirstOrDefault(c => "striprow_relay".Equals(c.Tag?.ToString()));
        bool wantStrip = CurrentMode == MatchMode.DomainSuffix;

        if (existing != null && !wantStrip)
        {
            // Shift subsequent controls up
            int dy = existing.Height + 4;
            foreach (Control c in _pnlRelayDest.Controls)
                if (c.Location.Y > existing.Location.Y) c.Location = new Point(c.Location.X, c.Location.Y - dy);
            _pnlRelayDest.Controls.Remove(existing);
            _chkStripSuffixRelay = null;
            _pnlRelayDest.Height -= dy;
        }
        else if (existing == null && wantStrip && _txtDeliverToOverrideRelay != null)
        {
            // Insert above the override address row
            int insertY = _txtDeliverToOverrideRelay.Location.Y - 18 - 24; // label + field gap
            _chkStripSuffixRelay = Chk(_pnlRelayDest,
                "Strip suffix segment from recipient address before delivery  (e.g. john@files.co → john@co)",
                0, insertY);
            _chkStripSuffixRelay.Tag = "striprow_relay";
            int dy = _chkStripSuffixRelay.Height + 4;
            foreach (Control c in _pnlRelayDest.Controls)
                if (c != _chkStripSuffixRelay && c.Location.Y >= insertY)
                    c.Location = new Point(c.Location.X, c.Location.Y + dy);
            _pnlRelayDest.Height += dy;
        }
    }

    // ── SharePoint destination panel ──────────────────────────────────────────

    private void BuildSharePointPanel()
    {
        int y = 0;
        const int w = 600;

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
            Font = new Font("Segoe UI", 9), DropDownWidth = w + 60
        };
        _cboSiteSearchResults.SelectedIndexChanged += (_, _) =>
        {
            var idx = _cboSiteSearchResults.SelectedIndex;
            if (idx >= 0 && idx < _siteSearchResults.Count)
                _txtSiteUrl.Text = _siteSearchResults[idx].Url;
        };
        _pnlSpDest.Controls.Add(_cboSiteSearchResults);
        y += 30;

        Lbl(_pnlSpDest, "SharePoint site URL:", 0, y); y += 18;
        _txtSiteUrl = new TextBox { Location = new Point(0, y), Width = 380, Font = new Font("Segoe UI", 9) };
        _btnResolveSite = new Button { Text = "Resolve / Load Libraries", Location = new Point(386, y - 1), Size = new Size(170, 24), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true };
        _btnResolveSite.Click += async (_, _) => await ResolveSiteAsync();
        _pnlSpDest.Controls.AddRange(new Control[] { _txtSiteUrl, _btnResolveSite });
        y += 28;

        _lblSiteId = new Label { Text = "Site ID: (not resolved)", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) };
        _pnlSpDest.Controls.Add(_lblSiteId);
        y += 20;

        Lbl(_pnlSpDest, "Document library:", 0, y); y += 18;
        _cboLibrary = new ComboBox { Location = new Point(0, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9) };
        _cboLibrary.SelectedIndexChanged += (_, _) => UpdateLibraryDriveId();
        _pnlSpDest.Controls.Add(_cboLibrary);
        y += 28;

        _lblDriveId = new Label { Text = "Drive ID: (select a library)", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) };
        _pnlSpDest.Controls.Add(_lblDriveId);
        y += 20;

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

    // ── Smarthost destination panel ───────────────────────────────────────────

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
            Location = new Point(0, y), AutoSize = false, Width = 590, Height = 34,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        };
        _pnlSmarthostDest.Controls.Add(hint);
        y += 42;

        Lbl(_pnlSmarthostDest, "Host (IP or hostname):", 0, y); y += 18;
        _txtSmarthostHostOverride = Txt(_pnlSmarthostDest, 0, y, 380, placeholder: "e.g. relay.company.com  or  192.168.1.100");
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
            DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9)
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
            UseSystemPasswordChar = true, Font = new Font("Segoe UI", 9)
        };
        _pnlSmarthostDest.Controls.Add(_txtSmarthostPassOverride);
        y += 28;

        Sep(_pnlSmarthostDest, 0, y, 590); y += 12;

        _chkStripSuffixSmarthost = null;
        if (CurrentMode == MatchMode.DomainSuffix)
        {
            _chkStripSuffixSmarthost = Chk(_pnlSmarthostDest,
                "Strip suffix segment from recipient address before delivery  (e.g. john@files.co → john@co)",
                0, y);
            _chkStripSuffixSmarthost.Tag = "striprow_smarthost";
            y += 24;
        }

        Lbl(_pnlSmarthostDest, "Override recipient address  (optional; takes priority over strip if set):", 0, y); y += 18;
        _txtDeliverToOverrideSmarthost = Txt(_pnlSmarthostDest, 0, y, 420,
            placeholder: "e.g. support@company.com — leave blank to use original or stripped");
        y += 30;

        _chkRewriteToHeader = new CheckBox
        {
            Text = "Also rewrite embedded To: header in message",
            Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9)
        };
        _pnlSmarthostDest.Controls.Add(_chkRewriteToHeader);
        y += 24;

        var warnLabel = new Label
        {
            Text = "Warning: rewriting the To: header may invalidate DKIM signatures on the original message.",
            Location = new Point(18, y), Width = 570, Height = 28, AutoSize = false,
            ForeColor = Color.DarkGoldenrod, Font = new Font("Segoe UI", 8.5f)
        };
        _pnlSmarthostDest.Controls.Add(warnLabel);
        y += 32;

        _pnlSmarthostDest.Height = y;
        UpdateSmarthostOverrideFields();
    }

    private void RebuildStripSuffixInSmarthostPanel()
    {
        if (_pnlSmarthostDest == null) return;
        var existing = _pnlSmarthostDest.Controls.Cast<Control>()
            .FirstOrDefault(c => "striprow_smarthost".Equals(c.Tag?.ToString()));
        bool wantStrip = CurrentMode == MatchMode.DomainSuffix;

        if (existing != null && !wantStrip)
        {
            int dy = existing.Height + 4;
            foreach (Control c in _pnlSmarthostDest.Controls)
                if (c.Location.Y > existing.Location.Y) c.Location = new Point(c.Location.X, c.Location.Y - dy);
            _pnlSmarthostDest.Controls.Remove(existing);
            _chkStripSuffixSmarthost = null;
            _pnlSmarthostDest.Height -= dy;
        }
        else if (existing == null && wantStrip && _txtDeliverToOverrideSmarthost != null)
        {
            int insertY = _txtDeliverToOverrideSmarthost.Location.Y - 18 - 24;
            _chkStripSuffixSmarthost = Chk(_pnlSmarthostDest,
                "Strip suffix segment from recipient address before delivery  (e.g. john@files.co → john@co)",
                0, insertY);
            _chkStripSuffixSmarthost.Tag = "striprow_smarthost";
            int dy = _chkStripSuffixSmarthost.Height + 4;
            foreach (Control c in _pnlSmarthostDest.Controls)
                if (c != _chkStripSuffixSmarthost && c.Location.Y >= insertY)
                    c.Location = new Point(c.Location.X, c.Location.Y + dy);
            _pnlSmarthostDest.Height += dy;
        }
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

    // ── Regex test ────────────────────────────────────────────────────────────

    private void RunPatternTest()
    {
        if (_txtPattern == null || _txtTestInput == null || _lblTestResult == null) return;

        var pattern = _txtPattern.Text.Trim();
        var input   = _txtTestInput.Text;

        if (string.IsNullOrEmpty(pattern))
        {
            _lblTestResult.Text      = "No pattern entered.";
            _lblTestResult.ForeColor = Color.DimGray;
            return;
        }

        try
        {
            var opts = RegexOptions.None;
            if (_chkCaseInsensitive?.Checked ?? true) opts |= RegexOptions.IgnoreCase;
            var m = Regex.Match(input, pattern, opts, TimeSpan.FromMilliseconds(500));

            if (!m.Success)
            {
                _lblTestResult.Text      = "No match.";
                _lblTestResult.ForeColor = Color.Firebrick;
                return;
            }

            var sb = new System.Text.StringBuilder("Match");
            // Named captures
            foreach (Group g in m.Groups)
            {
                if (int.TryParse(g.Name, out _)) continue;
                if (g.Success) sb.Append($"  %{g.Name}% = \"{g.Value}\"");
            }
            // Numbered groups
            for (int i = 1; i < m.Groups.Count; i++)
                if (m.Groups[i].Success) sb.Append($"  %match{i}% = \"{m.Groups[i].Value}\"");

            _lblTestResult.Text      = sb.ToString();
            _lblTestResult.ForeColor = Color.DarkGreen;
        }
        catch (ArgumentException ex)
        {
            _lblTestResult.Text      = $"Regex error: {ex.Message}";
            _lblTestResult.ForeColor = Color.Firebrick;
        }
    }

    // ── Populate from existing rule ───────────────────────────────────────────

    private void PopulateFromEdit()
    {
        var r = _editRule;
        if (r == null) return;

        _cboMatchMode.SelectedIndex = r.Mode switch
        {
            MatchMode.ExactTo      => 1,
            MatchMode.RegexTo      => 2,
            MatchMode.RegexFrom    => 3,
            MatchMode.RegexSubject => 4,
            _                      => 0
        };
        // RebuildMatchSection is triggered by the SelectedIndexChanged above

        if (_txtSuffix != null)    _txtSuffix.Text    = r.Suffix;
        if (_txtBaseDomain != null) _txtBaseDomain.Text = r.BaseDomain;
        if (_txtPattern != null)   _txtPattern.Text   = r.Pattern;
        if (_chkCaseInsensitive != null) _chkCaseInsensitive.Checked = r.CaseInsensitive;

        PopulateDestination(
            type:                 r.DestinationType,
            relayVia:             r.RelayVia,
            oneDriveUser:         r.OneDriveUser,
            siteUrl:              r.SiteUrl,
            siteId:               r.SiteId,
            libName:              r.LibraryName,
            driveId:              r.LibraryDriveId,
            folderPath:           r.FolderPath,
            useSubfolder:         r.UsePerEmailSubfolder,
            saveWhat:             r.SaveWhat,
            fromSender:           r.FromSenderHandling,
            saveEmbeddedImages:   r.SaveEmbeddedImages,
            filenameTemplate:     r.FilenameTemplate,
            subjectDelimiter:     r.SubjectDelimiter,
            enabled:              r.Enabled,
            useGlobalSmarthost:   r.UseGlobalSmarthost,
            smarthostHost:        r.SmarthostOverrideHost,
            smarthostPort:        r.SmarthostOverridePort,
            smarthostTls:         r.SmarthostOverrideTls,
            smarthostUser:        r.SmarthostOverrideUsername,
            smarthostPass:        r.SmarthostOverridePassword,
            stripSuffixFromTo:    r.StripSuffixFromTo,
            deliverToOverride:    r.DeliverToOverride,
            rewriteToHeader:      r.RewriteToHeader);
    }

    private void PopulateDestination(
        FileDestinationType type, string relayVia, string oneDriveUser,
        string siteUrl, string siteId, string libName, string driveId,
        string folderPath, bool? useSubfolder, SaveWhat? saveWhat,
        FromSenderHandling fromSender, bool? saveEmbeddedImages,
        string? filenameTemplate, string? subjectDelimiter,
        bool enabled,
        bool useGlobalSmarthost = true, string smarthostHost = "",
        int smarthostPort = 587, SmarthostTls smarthostTls = SmarthostTls.StartTls,
        string smarthostUser = "", string smarthostPass = "",
        bool stripSuffixFromTo = false, string deliverToOverride = "",
        bool rewriteToHeader = false)
    {
        _rdoTypeRelay.Checked      = type == FileDestinationType.EmailRelay;
        _rdoTypeOneDrive.Checked   = type == FileDestinationType.OneDrive;
        _rdoTypeSharePoint.Checked = type == FileDestinationType.SharePoint;
        _rdoTypeSmarthost.Checked  = type == FileDestinationType.SmarthostRelay;

        if (_chkSmarthostUseGlobal != null)     _chkSmarthostUseGlobal.Checked = useGlobalSmarthost;
        if (_txtSmarthostHostOverride != null)   _txtSmarthostHostOverride.Text = smarthostHost;
        if (_nudSmarthostPortOverride != null)   _nudSmarthostPortOverride.Value = Math.Clamp(smarthostPort, 1, 65535);
        if (_cboSmarthostTlsOverride  != null)   _cboSmarthostTlsOverride.SelectedIndex = (int)smarthostTls;
        if (_txtSmarthostUserOverride != null)   _txtSmarthostUserOverride.Text = smarthostUser;
        if (_txtSmarthostPassOverride != null)   _txtSmarthostPassOverride.Text = smarthostPass;

        if (_chkStripSuffixRelay != null)           _chkStripSuffixRelay.Checked      = stripSuffixFromTo;
        if (_txtDeliverToOverrideRelay != null)      _txtDeliverToOverrideRelay.Text   = deliverToOverride;
        if (_chkStripSuffixSmarthost != null)        _chkStripSuffixSmarthost.Checked  = stripSuffixFromTo;
        if (_txtDeliverToOverrideSmarthost != null)  _txtDeliverToOverrideSmarthost.Text = deliverToOverride;
        if (_chkRewriteToHeader != null)             _chkRewriteToHeader.Checked       = rewriteToHeader;

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
        if (saveEmbeddedImages.HasValue)
        {
            _chkOverrideSaveEmbeddedImages.Checked = true;
            _chkSaveEmbeddedImagesValue.Checked    = saveEmbeddedImages.Value;
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

    // ── Destination visibility + override repositioning ───────────────────────

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

        int activeHeight = isRelay      ? (_pnlRelayDest?.Height     ?? 50)
                         : isOD         ? (_pnlOneDriveDest?.Height   ?? 80)
                         : isSmarthost  ? (_pnlSmarthostDest?.Height  ?? 200)
                                        : (_pnlSpDest?.Height         ?? 280);

        int newY = _destPanelY + activeHeight + 8;
        RepositionOverrides(newY);

        bool fs = !isRelay && !isSmarthost;
        void SetPair(CheckBox? chk, Control? sub)
        {
            if (chk == null) return;
            chk.Enabled = fs;
            if (sub != null) sub.Enabled = fs && chk.Checked;
        }
        SetPair(_chkOverrideSaveWhat,           _cboSaveWhat);
        SetPair(_chkOverrideSubfolder,          _chkSubfolderValue);
        SetPair(_chkOverrideSaveEmbeddedImages, _chkSaveEmbeddedImagesValue);
        SetPair(_chkOverrideFilenameTemplate,   _txtFilenameTemplate);
        SetPair(_chkOverrideSubjectDelimiter,   _txtSubjectDelimiter);
    }

    private void RepositionOverrides(int startY)
    {
        int delta = startY - _lastOverrideY;
        if (delta == 0) return;
        int prevY = _lastOverrideY;
        _lastOverrideY = startY;

        foreach (Control c in _scroll.Controls)
        {
            if (c == _pnlRelayDest || c == _pnlOneDriveDest ||
                c == _pnlSpDest   || c == _pnlSmarthostDest) continue;
            if ("match".Equals(c.Tag?.ToString())) continue;
            if (c.Location.Y >= prevY)
                c.Location = new Point(c.Location.X, c.Location.Y + delta);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void SaveRule()
    {
        var mode = CurrentMode;

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
        bool? saveEmbeddedImages = _chkOverrideSaveEmbeddedImages.Checked
            ? _chkSaveEmbeddedImagesValue.Checked
            : (bool?)null;
        string? filenameTemplate = _chkOverrideFilenameTemplate.Checked
            ? _txtFilenameTemplate.Text.Trim()
            : null;
        string? subjectDelimiter = _chkOverrideSubjectDelimiter.Checked
            ? (string.IsNullOrEmpty(_txtSubjectDelimiter.Text) ? " " : _txtSubjectDelimiter.Text)
            : null;

        var libName    = _cboLibrary.SelectedItem?.ToString() ?? "";
        var driveId    = GetSelectedDriveId();
        var folderPath = destType == FileDestinationType.SharePoint
            ? (_txtSpFolderPath?.Text.Trim() ?? "")
            : (_txtOneDrivePath?.Text.Trim() ?? "");

        bool stripSuffix = destType == FileDestinationType.EmailRelay
            ? (_chkStripSuffixRelay?.Checked ?? false)
            : destType == FileDestinationType.SmarthostRelay
                ? (_chkStripSuffixSmarthost?.Checked ?? false)
                : false;

        string deliverToOverride = destType == FileDestinationType.EmailRelay
            ? (_txtDeliverToOverrideRelay?.Text.Trim() ?? "")
            : destType == FileDestinationType.SmarthostRelay
                ? (_txtDeliverToOverrideSmarthost?.Text.Trim() ?? "")
                : "";

        bool rewriteToHeader = destType == FileDestinationType.SmarthostRelay
            && (_chkRewriteToHeader?.Checked ?? false);

        // ── Validate match section ────────────────────────────────────────────
        if (mode == MatchMode.DomainSuffix)
        {
            var suffix = _txtSuffix?.Text.Trim() ?? "";
            var baseDom = _txtBaseDomain?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(suffix) && string.IsNullOrWhiteSpace(baseDom))
            {
                MessageBox.Show(
                    "Wildcard suffix rules require a Base Domain to avoid matching all traffic.\n" +
                    "Please enter a Base Domain (e.g. company.com).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            var pattern = _txtPattern?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.Show(
                    mode == MatchMode.ExactTo
                        ? "To: address is required."
                        : "Pattern is required.",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (mode != MatchMode.ExactTo)
            {
                // Validate regex
                try
                {
                    _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show($"Invalid regex pattern:\n{ex.Message}", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
        }

        Result = new RoutingRule
        {
            Id      = _editRule?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Enabled = _chkEnabled.Checked,
            Mode    = mode,

            // Match
            Suffix          = _txtSuffix?.Text.Trim() ?? "",
            BaseDomain      = _txtBaseDomain?.Text.Trim() ?? "",
            Pattern         = _txtPattern?.Text.Trim() ?? "",
            CaseInsensitive = _chkCaseInsensitive?.Checked ?? true,

            // Destination
            DestinationType = destType,
            RelayVia        = _txtRelayVia?.Text.Trim() ?? "",
            OneDriveUser    = _txtOneDriveUser?.Text.Trim() ?? "",
            SiteUrl         = _txtSiteUrl?.Text.Trim() ?? "",
            SiteId          = _resolvedSiteId,
            LibraryName     = libName,
            LibraryDriveId  = driveId,
            FolderPath      = folderPath,

            // Overrides
            UsePerEmailSubfolder     = subfolder,
            SaveWhat                 = saveWhat,
            FromSenderHandling       = fromSender,
            SaveEmbeddedImages       = saveEmbeddedImages,
            FilenameTemplate         = filenameTemplate,
            SubjectDelimiter         = subjectDelimiter,

            // Smarthost
            UseGlobalSmarthost        = _chkSmarthostUseGlobal?.Checked ?? true,
            SmarthostOverrideHost     = _txtSmarthostHostOverride?.Text.Trim() ?? "",
            SmarthostOverridePort     = (int)(_nudSmarthostPortOverride?.Value ?? 587),
            SmarthostOverrideTls      = (SmarthostTls)(_cboSmarthostTlsOverride?.SelectedIndex ?? 1),
            SmarthostOverrideUsername = _txtSmarthostUserOverride?.Text.Trim() ?? "",
            SmarthostOverridePassword = _txtSmarthostPassOverride?.Text ?? "",

            // Delivery
            StripSuffixFromTo = stripSuffix,
            DeliverToOverride = deliverToOverride,
            RewriteToHeader   = rewriteToHeader,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── SharePoint async helpers ──────────────────────────────────────────────

    private async Task SearchSitesAsync()
    {
        _btnSearchSites.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var mailSender = new GraphMailSender(_configManager, _logger);
            var fileStorer = new GraphFileStorer(_configManager, mailSender, _logger);
            var query      = _txtSiteSearch.Text.Trim();
            // Blank query → SearchSitesAsync uses GET /sites?$top=500 (no search param)
            // which reliably returns real team sites. search=* only surfaces system entries.

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
            MessageBox.Show($"Site search failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearchSites.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private async Task ResolveSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtSiteUrl.Text))
        {
            MessageBox.Show("Enter a SharePoint site URL first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            MessageBox.Show($"Could not resolve site:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show("Resolve the site and select a library first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show($"Folder '{path}' exists.", "Verified", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (MessageBox.Show($"Folder '{path}' does not exist. Create it now?",
                "Folder Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                await fileStorer.EnsureFolderPathAsync(driveId, path, default);
                MessageBox.Show("Folder created.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Folder verification failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    // ── Layout helpers ────────────────────────────────────────────────────────

    private Label Lbl(Control parent, string text, int x, int y, int width = 0)
    {
        var lbl = new Label { Text = text, Location = new Point(x, y), AutoSize = width == 0, Font = new Font("Segoe UI", 9) };
        if (width > 0) { lbl.Width = width; lbl.AutoSize = false; }
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static void BoldLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
    }

    private TextBox Txt(Control parent, int x, int y, int width, string placeholder = "")
    {
        var tb = new TextBox
        {
            Location = new Point(x, y), Width = width,
            Font = new Font("Segoe UI", 9), PlaceholderText = placeholder
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
