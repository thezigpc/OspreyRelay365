using Relay365.Core.Config;
using Relay365.Core.Graph;
using Relay365.Core.Logging;

namespace Relay365.Forms;

/// <summary>
/// Lists and manages both suffix rules and explicit recipient rules.
/// Opens FileRuleEditorForm for add/edit of individual rules.
/// </summary>
public class FileRoutingRulesForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    private TabControl _tabs = null!;

    // Suffix rules tab
    private ListView _lvSuffix = null!;
    private Button _btnAddSuffix = null!;
    private Button _btnEditSuffix = null!;
    private Button _btnDeleteSuffix = null!;

    // Explicit recipient rules tab
    private ListView _lvRecipient = null!;
    private Button _btnAddRecipient = null!;
    private Button _btnEditRecipient = null!;
    private Button _btnDeleteRecipient = null!;

    // Global defaults tab
    private ComboBox _cboGlobalMode = null!;
    private ComboBox _cboNoMatch = null!;
    private CheckBox _chkCreateFolders = null!;
    private ComboBox _cboConflict = null!;
    private ComboBox _cboSaveWhat = null!;
    private ComboBox _cboNoAttachment = null!;
    private CheckBox _chkPerEmailSubfolder = null!;
    private ComboBox _cboFromSender = null!;
    private TextBox _txtDefaultFilenameTemplate = null!;
    private TextBox _txtDefaultSubjectDelimiter = null!;
    private TextBox _txtFilenameSpaceReplacement = null!;

    // Global catch-all
    private TextBox _txtCatchAllUser = null!;
    private TextBox _txtCatchAllPath = null!;

    // Unrouted tab
    private ComboBox _cboUnroutedAction = null!;
    private TextBox _txtUnroutedLocalPath = null!;
    private NumericUpDown _nudRetentionDays = null!;
    private TextBox _txtUnroutedOneDriveUser = null!;
    private TextBox _txtUnroutedOneDrivePath = null!;
    private TextBox _txtUnroutedAlertEmail = null!;

    // Unrouted → SharePoint
    private Label _lblUnroutedSp = null!;
    private TextBox _txtUnroutedSpSearch = null!;
    private Button _btnUnroutedSpSearch = null!;
    private ComboBox _cboUnroutedSpResults = null!;
    private Label _lblUnroutedSpLibrary = null!;
    private ComboBox _cboUnroutedSpLibrary = null!;
    private TextBox _txtUnroutedSpFolder = null!;

    // Resolved SharePoint IDs (not shown in UI)
    private string _unroutedSpSiteId = "";
    private string _unroutedSpDriveId = "";
    private string _unroutedSpSiteUrl = "";
    private List<(string Name, string DriveId)> _unroutedSpLibraries = new();
    private List<(string DisplayName, string Url)> _unroutedSpSiteResults = new();

    public FileRoutingRulesForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger = logger;
        InitializeComponent();
        LoadAll();
    }

    private void InitializeComponent()
    {
        Text = "File Routing Rules";
        Size = new Size(800, 600);
        MinimumSize = new Size(720, 520);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        _tabs = new TabControl { Dock = DockStyle.Fill };

        _tabs.TabPages.Add(BuildSuffixTab());
        _tabs.TabPages.Add(BuildRecipientTab());
        _tabs.TabPages.Add(BuildDefaultsTab());
        _tabs.TabPages.Add(BuildUnroutedTab());

        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        var btnSave = new Button
        {
            Text = "Save & Close",
            Size = new Size(120, 30),
            Location = new Point(800 - 260, 8),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        var btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(110, 30),
            Location = new Point(800 - 130, 8),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        btnSave.Click += (_, _) => SaveAll();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        pnlBottom.Controls.AddRange(new Control[] { btnSave, btnCancel });

        Controls.Add(_tabs);
        Controls.Add(pnlBottom);
    }

    // ── Suffix rules tab ──────────────────────────────────────────────────────

    private TabPage BuildSuffixTab()
    {
        var page = new TabPage("Suffix Rules");
        _lvSuffix = MakeListView(new[] { ("Suffix", 100), ("Base Domain", 160), ("Type", 90), ("Folder Path", 230), ("On/Off", 55) });
        _lvSuffix.SelectedIndexChanged += (_, _) => UpdateSuffixButtons();

        _btnAddSuffix    = RuleBtn("+ Add");
        _btnEditSuffix   = RuleBtn("Edit");
        _btnDeleteSuffix = RuleBtn("Delete");
        _btnEditSuffix.Enabled = _btnDeleteSuffix.Enabled = false;

        _btnAddSuffix.Click    += (_, _) => AddSuffixRule();
        _btnEditSuffix.Click   += (_, _) => EditSuffixRule();
        _btnDeleteSuffix.Click += (_, _) => DeleteSuffixRule();

        return LayoutRuleTab(page, _lvSuffix,
            new[] { _btnAddSuffix, _btnEditSuffix, _btnDeleteSuffix },
            "Suffix rules match To: addresses like jane@files.company.com. Leave Suffix blank for wildcard (any subdomain).\n" +
            "Path variables: %toupn% %todomain% %tobasedomain% %suffix% %from% %fromdomain% %subject% %date% etc.");
    }

    // ── Explicit recipient rules tab ──────────────────────────────────────────

    private TabPage BuildRecipientTab()
    {
        var page = new TabPage("Recipient Rules");
        _lvRecipient = MakeListView(new[] { ("To: Address", 180), ("Type", 90), ("Destination", 320), ("On/Off", 55) });
        _lvRecipient.SelectedIndexChanged += (_, _) => UpdateRecipientButtons();

        _btnAddRecipient    = RuleBtn("+ Add");
        _btnEditRecipient   = RuleBtn("Edit");
        _btnDeleteRecipient = RuleBtn("Delete");
        _btnEditRecipient.Enabled = _btnDeleteRecipient.Enabled = false;

        _btnAddRecipient.Click    += (_, _) => AddRecipientRule();
        _btnEditRecipient.Click   += (_, _) => EditRecipientRule();
        _btnDeleteRecipient.Click += (_, _) => DeleteRecipientRule();

        return LayoutRuleTab(page, _lvRecipient,
            new[] { _btnAddRecipient, _btnEditRecipient, _btnDeleteRecipient },
            "Explicit rules: exact To: address → email relay mailbox, OneDrive folder, or SharePoint library.");
    }

    // ── Global defaults tab ───────────────────────────────────────────────────

    private TabPage BuildDefaultsTab()
    {
        var page = new TabPage("Global Defaults");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 8, 12, 8) };

        int y = 8;
        _cboGlobalMode = AddCombo(scroll, "Global relay mode:", ref y,
            new[] { "EmailRelay", "FileStorage", "Hybrid" });
        _cboNoMatch = AddCombo(scroll, "No-match behavior (Hybrid mode):", ref y,
            new[] { "Relay", "Unrouted", "Reject" });

        y += 6;
        _chkCreateFolders = AddCheck(scroll, "Create missing folders automatically", ref y);
        _chkPerEmailSubfolder = AddCheck(scroll, "Create per-email subfolder (YYYY-MM-DD_HHmmss Subject)", ref y);

        y += 6;
        _cboConflict = AddCombo(scroll, "Duplicate file behavior:", ref y,
            new[] { "Rename", "Replace", "Fail" });
        _cboSaveWhat = AddCombo(scroll, "Save what:", ref y,
            new[] { "AttachmentsOnly", "AttachmentsAndBody", "FullEml" });
        _cboNoAttachment = AddCombo(scroll, "If no attachments:", ref y,
            new[] { "Skip", "SaveAsEml" });
        _cboFromSender = AddCombo(scroll, "From: sender handling:", ref y,
            new[] { "Ignore", "AsSubfolder", "AsMetadata", "Both" });

        y += 8;
        AddSectionHeader(scroll, "Path & Filename Variables", ref y);

        _txtDefaultFilenameTemplate = AddField(scroll,
            "Default filename template  (blank = keep original filename)", ref y,
            placeholder: "e.g. %date%_%subject[0]%_%originalbasefilename%");
        _txtDefaultSubjectDelimiter = AddField(scroll,
            "Subject word delimiter  (used for %subject[n]% and %subject[*]%)", ref y,
            width: 80, placeholder: "space");
        _txtFilenameSpaceReplacement = AddField(scroll,
            "Replace spaces in final filename with  (blank = keep spaces)", ref y,
            width: 60, placeholder: "e.g. _");

        y += 8;
        AddSectionHeader(scroll, "Global catch-all destination (FileStorage / Hybrid — when no rule matches)", ref y);

        scroll.Controls.Add(new Label
        {
            Text = "Leave OneDrive user blank to send unmatched emails to the Unrouted folder instead.",
            Location = new Point(12, y), AutoSize = false, Width = 660, Height = 28,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        });
        y += 30;

        _txtCatchAllUser = AddField(scroll, "OneDrive user UPN (e.g. relay@company.com):", ref y);
        _txtCatchAllPath = AddField(scroll, "Catch-all folder path  (supports path variables):", ref y,
            placeholder: "/EmailRelay");

        page.Controls.Add(scroll);
        return page;
    }

    // ── Unrouted tab ──────────────────────────────────────────────────────────

    private TabPage BuildUnroutedTab()
    {
        var page = new TabPage("Unrouted Handling");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 8, 12, 8) };

        int y = 8;
        _cboUnroutedAction = AddCombo(scroll, "When an email cannot be routed:", ref y,
            new[] { "LocalFolder", "OneDriveRedirect", "SharePointRedirect", "EmailAsAttachment" });

        scroll.Controls.Add(new Label
        {
            Text = "A local folder copy is always saved as a safety net. OneDrive/SharePoint/Email actions are attempted additionally.",
            Location = new Point(12, y), AutoSize = false, Width = 700, Height = 32,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        });
        y += 40;

        // ── Local folder configuration ─────────────────────────────────────
        AddSectionHeader(scroll, "Local unrouted folder", ref y);

        scroll.Controls.Add(new Label
        {
            Text = "Folder path  (blank = default location):",
            Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9)
        });
        y += 20;

        _txtUnroutedLocalPath = new TextBox
        {
            Location = new Point(12, y), Width = 480,
            Font = new Font("Segoe UI", 9),
            PlaceholderText = $"Default: {System.IO.Path.Combine(Core.Config.ConfigManager.GetConfigDir(), "unrouted")}"
        };
        var btnBrowseUnrouted = new Button
        {
            Text = "Browse…",
            Location = new Point(500, y - 1), Size = new Size(80, 24),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true
        };
        btnBrowseUnrouted.Click += (_, _) =>
        {
            try
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Select unrouted email folder",
                    SelectedPath = string.IsNullOrWhiteSpace(_txtUnroutedLocalPath.Text)
                        ? Core.Config.ConfigManager.GetConfigDir()
                        : _txtUnroutedLocalPath.Text
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtUnroutedLocalPath.Text = dlg.SelectedPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder browser: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        scroll.Controls.AddRange(new Control[] { _txtUnroutedLocalPath, btnBrowseUnrouted });
        y += 32;

        scroll.Controls.Add(new Label
        {
            Text = "Auto-purge files older than:",
            Location = new Point(12, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9)
        });
        _nudRetentionDays = new NumericUpDown
        {
            Location = new Point(194, y), Width = 64,
            Minimum = 0, Maximum = 3650, Value = 30,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_nudRetentionDays);
        scroll.Controls.Add(new Label
        {
            Text = "days  (0 = never purge)",
            Location = new Point(264, y + 3), AutoSize = true,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9)
        });
        y += 34;

        // ── OneDrive section ───────────────────────────────────────────────
        y += 6;
        AddSectionHeader(scroll, "OneDrive / SharePoint / Email redirect", ref y);

        // OneDrive section
        _txtUnroutedOneDriveUser = AddField(scroll, "OneDrive redirect — user UPN:", ref y,
            placeholder: "admin@company.com");
        _txtUnroutedOneDrivePath = AddField(scroll, "OneDrive redirect — folder path:", ref y,
            placeholder: "/Apps/FileRelay/Unrouted");

        // SharePoint section
        _lblUnroutedSp = new Label
        {
            Text = "SharePoint destination:",
            Location = new Point(12, y), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        scroll.Controls.Add(_lblUnroutedSp);
        y += 22;

        scroll.Controls.Add(new Label { Text = "Search sites:", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;

        _txtUnroutedSpSearch = new TextBox
        {
            Location = new Point(12, y), Width = 440,
            Font = new Font("Segoe UI", 9),
            PlaceholderText = "Type to search, or leave blank and click Search to load all sites"
        };
        _btnUnroutedSpSearch = new Button
        {
            Text = "Search",
            Location = new Point(460, y - 2), Width = 80, Height = 28,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_txtUnroutedSpSearch);
        scroll.Controls.Add(_btnUnroutedSpSearch);
        y += 34;

        _cboUnroutedSpResults = new ComboBox
        {
            Location = new Point(12, y), Width = 680,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9),
            DropDownWidth = 700
        };
        scroll.Controls.Add(_cboUnroutedSpResults);
        y += 34;

        _lblUnroutedSpLibrary = new Label { Text = "Document library:", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 9) };
        scroll.Controls.Add(_lblUnroutedSpLibrary);
        y += 20;
        _cboUnroutedSpLibrary = new ComboBox
        {
            Location = new Point(12, y), Width = 360,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        scroll.Controls.Add(_cboUnroutedSpLibrary);
        y += 34;

        _txtUnroutedSpFolder = AddField(scroll, "Folder path within library:", ref y,
            placeholder: "/Apps/FileRelay/Unrouted");

        // Alert email (always shown)
        y += 8;
        _txtUnroutedAlertEmail = AddField(scroll, "Alert email address (notified on every unrouted event):", ref y,
            placeholder: "admin@company.com");

        _btnUnroutedSpSearch.Click  += async (_, _) => await SearchUnroutedSitesAsync();
        _cboUnroutedSpResults.SelectedIndexChanged += async (_, _) => await UnroutedSiteSelectedAsync();
        _cboUnroutedSpLibrary.SelectedIndexChanged += (_, _) => UnroutedLibrarySelected();
        _cboUnroutedAction.SelectedIndexChanged    += (_, _) => UpdateUnroutedFields();

        page.Controls.Add(scroll);
        return page;
    }

    // ── Unrouted SharePoint search ────────────────────────────────────────────

    private async Task SearchUnroutedSitesAsync()
    {
        _btnUnroutedSpSearch.Enabled = false;
        _cboUnroutedSpResults.Items.Clear();
        _cboUnroutedSpResults.Items.Add("Searching...");
        _cboUnroutedSpResults.SelectedIndex = 0;

        try
        {
            var sender = new GraphMailSender(_configManager, _logger);
            var storer = new GraphFileStorer(_configManager, sender, _logger);
            var query  = _txtUnroutedSpSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) query = "*";

            _unroutedSpSiteResults = await storer.SearchSitesAsync(query, CancellationToken.None);

            _cboUnroutedSpResults.Items.Clear();
            if (_unroutedSpSiteResults.Count == 0)
            {
                _cboUnroutedSpResults.Items.Add("No sites found");
                _cboUnroutedSpResults.SelectedIndex = 0;
                return;
            }

            foreach (var (name, url) in _unroutedSpSiteResults)
                _cboUnroutedSpResults.Items.Add($"{name}  ({url})");

            // Try to re-select previously saved site
            if (!string.IsNullOrWhiteSpace(_unroutedSpSiteUrl))
            {
                for (int i = 0; i < _unroutedSpSiteResults.Count; i++)
                {
                    if (string.Equals(_unroutedSpSiteResults[i].Url, _unroutedSpSiteUrl,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _cboUnroutedSpResults.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (_cboUnroutedSpResults.Items.Count > 0)
            {
                _cboUnroutedSpResults.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _cboUnroutedSpResults.Items.Clear();
            _cboUnroutedSpResults.Items.Add($"Error: {ex.Message}");
            _cboUnroutedSpResults.SelectedIndex = 0;
        }
        finally
        {
            _btnUnroutedSpSearch.Enabled = true;
        }
    }

    private async Task UnroutedSiteSelectedAsync()
    {
        var idx = _cboUnroutedSpResults.SelectedIndex;
        if (idx < 0 || idx >= _unroutedSpSiteResults.Count) return;

        var (_, url) = _unroutedSpSiteResults[idx];
        _unroutedSpSiteUrl = url;
        _cboUnroutedSpLibrary.Items.Clear();
        _cboUnroutedSpLibrary.Items.Add("Loading libraries...");
        _cboUnroutedSpLibrary.SelectedIndex = 0;
        _unroutedSpDriveId = "";

        try
        {
            var sender = new GraphMailSender(_configManager, _logger);
            var storer = new GraphFileStorer(_configManager, sender, _logger);

            _unroutedSpSiteId = await storer.ResolveSiteIdAsync(url, CancellationToken.None);
            _unroutedSpLibraries = await storer.GetLibrariesAsync(_unroutedSpSiteId, CancellationToken.None);

            _cboUnroutedSpLibrary.Items.Clear();
            foreach (var (name, _) in _unroutedSpLibraries)
                _cboUnroutedSpLibrary.Items.Add(name);

            // Try to re-select previously saved library
            var cfg = _configManager.Config;
            if (!string.IsNullOrWhiteSpace(cfg.UnroutedSharePointLibraryName))
            {
                for (int i = 0; i < _unroutedSpLibraries.Count; i++)
                {
                    if (string.Equals(_unroutedSpLibraries[i].Name,
                        cfg.UnroutedSharePointLibraryName, StringComparison.OrdinalIgnoreCase))
                    {
                        _cboUnroutedSpLibrary.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (_cboUnroutedSpLibrary.Items.Count > 0)
            {
                _cboUnroutedSpLibrary.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            _cboUnroutedSpLibrary.Items.Clear();
            _cboUnroutedSpLibrary.Items.Add($"Error: {ex.Message}");
            _cboUnroutedSpLibrary.SelectedIndex = 0;
        }
    }

    private void UnroutedLibrarySelected()
    {
        var idx = _cboUnroutedSpLibrary.SelectedIndex;
        if (idx >= 0 && idx < _unroutedSpLibraries.Count)
            _unroutedSpDriveId = _unroutedSpLibraries[idx].DriveId;
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadAll()
    {
        var cfg = _configManager.Config;

        // Suffix rules
        _lvSuffix.Items.Clear();
        foreach (var r in cfg.SuffixRules)
            _lvSuffix.Items.Add(SuffixToItem(r));

        // Recipient rules
        _lvRecipient.Items.Clear();
        foreach (var r in cfg.FileRules)
            _lvRecipient.Items.Add(RecipientToItem(r));

        // Defaults
        _cboGlobalMode.SelectedItem   = cfg.GlobalMode.ToString();
        _cboNoMatch.SelectedItem      = cfg.NoMatchBehavior.ToString();
        _chkCreateFolders.Checked     = cfg.CreateMissingFolders;
        _chkPerEmailSubfolder.Checked = cfg.DefaultUsePerEmailSubfolder;
        _cboConflict.SelectedItem     = cfg.FileConflictBehavior.ToString();
        _cboSaveWhat.SelectedItem     = cfg.DefaultSaveWhat.ToString();
        _cboNoAttachment.SelectedItem = cfg.DefaultNoAttachmentBehavior.ToString();
        _cboFromSender.SelectedItem   = cfg.DefaultFromSenderHandling.ToString();

        _txtDefaultFilenameTemplate.Text   = cfg.DefaultFilenameTemplate;
        _txtDefaultSubjectDelimiter.Text   = cfg.DefaultSubjectDelimiter == " " ? "" : cfg.DefaultSubjectDelimiter;
        _txtFilenameSpaceReplacement.Text  = cfg.FilenameSpaceReplacement;

        // Catch-all
        _txtCatchAllUser.Text = cfg.GlobalCatchAllOneDriveUser;
        _txtCatchAllPath.Text = cfg.GlobalCatchAllFolderPath;

        // Unrouted
        _cboUnroutedAction.SelectedItem    = cfg.UnroutedAction.ToString();
        _txtUnroutedLocalPath.Text         = cfg.UnroutedLocalPath;
        _nudRetentionDays.Value            = Math.Clamp(cfg.UnroutedLocalRetentionDays, 0, 3650);
        _txtUnroutedOneDriveUser.Text      = cfg.UnroutedOneDriveUser;
        _txtUnroutedOneDrivePath.Text      = cfg.UnroutedOneDrivePath;
        _txtUnroutedAlertEmail.Text        = cfg.UnroutedAlertEmail;

        // Unrouted SharePoint — remember saved values for re-selection after search
        _unroutedSpSiteUrl = cfg.UnroutedSharePointSiteUrl;
        _unroutedSpSiteId  = cfg.UnroutedSharePointSiteId;
        _unroutedSpDriveId = cfg.UnroutedSharePointDriveId;
        _txtUnroutedSpFolder.Text = cfg.UnroutedSharePointFolderPath;
        if (!string.IsNullOrWhiteSpace(cfg.UnroutedSharePointSiteUrl))
        {
            _cboUnroutedSpResults.Items.Add(cfg.UnroutedSharePointSiteUrl);
            _cboUnroutedSpResults.SelectedIndex = 0;
        }
        if (!string.IsNullOrWhiteSpace(cfg.UnroutedSharePointLibraryName))
        {
            _cboUnroutedSpLibrary.Items.Add(cfg.UnroutedSharePointLibraryName);
            _cboUnroutedSpLibrary.SelectedIndex = 0;
        }

        UpdateUnroutedFields();
    }

    private void SaveAll()
    {
        var cfg = _configManager.Config;

        // Suffix rules
        cfg.SuffixRules = _lvSuffix.Items.Cast<ListViewItem>()
            .Select(i => (SuffixRule)i.Tag!).ToList();

        // Recipient rules
        cfg.FileRules = _lvRecipient.Items.Cast<ListViewItem>()
            .Select(i => (RecipientFileRule)i.Tag!).ToList();

        // Defaults
        cfg.GlobalMode              = Enum.Parse<RelayMode>(_cboGlobalMode.SelectedItem?.ToString() ?? "EmailRelay");
        cfg.NoMatchBehavior         = Enum.Parse<NoMatchBehavior>(_cboNoMatch.SelectedItem?.ToString() ?? "Relay");
        cfg.CreateMissingFolders    = _chkCreateFolders.Checked;
        cfg.DefaultUsePerEmailSubfolder = _chkPerEmailSubfolder.Checked;
        cfg.FileConflictBehavior    = Enum.Parse<FileConflictBehavior>(_cboConflict.SelectedItem?.ToString() ?? "Rename");
        cfg.DefaultSaveWhat         = Enum.Parse<SaveWhat>(_cboSaveWhat.SelectedItem?.ToString() ?? "AttachmentsOnly");
        cfg.DefaultNoAttachmentBehavior = Enum.Parse<NoAttachmentBehavior>(_cboNoAttachment.SelectedItem?.ToString() ?? "SaveAsEml");
        cfg.DefaultFromSenderHandling = Enum.Parse<FromSenderHandling>(_cboFromSender.SelectedItem?.ToString() ?? "Ignore");

        cfg.DefaultFilenameTemplate = _txtDefaultFilenameTemplate.Text.Trim();
        var delim = _txtDefaultSubjectDelimiter.Text; // preserve spaces exactly; empty = treat as space
        cfg.DefaultSubjectDelimiter = string.IsNullOrEmpty(delim) ? " " : delim;
        cfg.FilenameSpaceReplacement = _txtFilenameSpaceReplacement.Text.Length > 0
            ? _txtFilenameSpaceReplacement.Text[..1] : "";

        // Catch-all
        cfg.GlobalCatchAllOneDriveUser = _txtCatchAllUser.Text.Trim();
        cfg.GlobalCatchAllFolderPath   = _txtCatchAllPath.Text.Trim();

        // Unrouted
        cfg.UnroutedAction               = Enum.Parse<UnroutedAction>(_cboUnroutedAction.SelectedItem?.ToString() ?? "LocalFolder");
        cfg.UnroutedLocalPath            = _txtUnroutedLocalPath.Text.Trim();
        cfg.UnroutedLocalRetentionDays   = (int)_nudRetentionDays.Value;
        cfg.UnroutedOneDriveUser         = _txtUnroutedOneDriveUser.Text.Trim();
        cfg.UnroutedOneDrivePath         = _txtUnroutedOneDrivePath.Text.Trim();
        cfg.UnroutedAlertEmail           = _txtUnroutedAlertEmail.Text.Trim();

        // Unrouted SharePoint
        cfg.UnroutedSharePointSiteUrl     = _unroutedSpSiteUrl;
        cfg.UnroutedSharePointSiteId      = _unroutedSpSiteId;
        cfg.UnroutedSharePointDriveId     = _unroutedSpDriveId;
        cfg.UnroutedSharePointFolderPath  = _txtUnroutedSpFolder.Text.Trim();
        var libIdx = _cboUnroutedSpLibrary.SelectedIndex;
        cfg.UnroutedSharePointLibraryName = libIdx >= 0 && libIdx < _unroutedSpLibraries.Count
            ? _unroutedSpLibraries[libIdx].Name
            : (_cboUnroutedSpLibrary.SelectedItem?.ToString() ?? "");

        _configManager.Save(cfg);
        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Rule CRUD ─────────────────────────────────────────────────────────────

    private void AddSuffixRule()
    {
        using var editor = new FileRuleEditorForm(null, null, _configManager, _logger);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.SuffixResult != null)
            _lvSuffix.Items.Add(SuffixToItem(editor.SuffixResult));
    }

    private void EditSuffixRule()
    {
        if (_lvSuffix.SelectedItems.Count == 0) return;
        var item = _lvSuffix.SelectedItems[0];
        using var editor = new FileRuleEditorForm((SuffixRule)item.Tag!, null, _configManager, _logger);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.SuffixResult != null)
        {
            var idx = item.Index;
            _lvSuffix.Items.RemoveAt(idx);
            _lvSuffix.Items.Insert(idx, SuffixToItem(editor.SuffixResult));
        }
    }

    private void DeleteSuffixRule()
    {
        if (_lvSuffix.SelectedItems.Count == 0) return;
        if (MessageBox.Show("Delete this suffix rule?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _lvSuffix.Items.Remove(_lvSuffix.SelectedItems[0]);
    }

    private void AddRecipientRule()
    {
        using var editor = new FileRuleEditorForm(null, null, _configManager, _logger);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.RecipientResult != null)
            _lvRecipient.Items.Add(RecipientToItem(editor.RecipientResult));
    }

    private void EditRecipientRule()
    {
        if (_lvRecipient.SelectedItems.Count == 0) return;
        var item = _lvRecipient.SelectedItems[0];
        using var editor = new FileRuleEditorForm(null, (RecipientFileRule)item.Tag!, _configManager, _logger);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.RecipientResult != null)
        {
            var idx = item.Index;
            _lvRecipient.Items.RemoveAt(idx);
            _lvRecipient.Items.Insert(idx, RecipientToItem(editor.RecipientResult));
        }
    }

    private void DeleteRecipientRule()
    {
        if (_lvRecipient.SelectedItems.Count == 0) return;
        if (MessageBox.Show("Delete this recipient rule?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _lvRecipient.Items.Remove(_lvRecipient.SelectedItems[0]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ListViewItem SuffixToItem(SuffixRule r)
    {
        var suffixDisplay = string.IsNullOrWhiteSpace(r.Suffix) || r.Suffix == "*"
            ? "(wildcard)" : r.Suffix;
        var item = new ListViewItem(suffixDisplay) { Tag = r };
        item.SubItems.Add(r.BaseDomain);
        item.SubItems.Add(r.DestinationType.ToString());
        item.SubItems.Add(r.DestinationType == FileDestinationType.SmarthostRelay
            ? (r.UseGlobalSmarthost ? "(global smarthost)" : r.SmarthostOverrideHost)
            : r.FolderPath);
        item.SubItems.Add(r.Enabled ? "✓" : "—");
        return item;
    }

    private static ListViewItem RecipientToItem(RecipientFileRule r)
    {
        var item = new ListViewItem(r.ToAddress) { Tag = r };
        item.SubItems.Add(r.DestinationType.ToString());
        item.SubItems.Add(r.DestinationType switch
        {
            FileDestinationType.EmailRelay =>
                string.IsNullOrWhiteSpace(r.RelayVia) ? "(passthrough)" : r.RelayVia,
            FileDestinationType.SmarthostRelay =>
                r.UseGlobalSmarthost ? "(global smarthost)" : r.SmarthostOverrideHost,
            _ => $"{r.SiteUrl}{r.FolderPath}"
        });
        item.SubItems.Add(r.Enabled ? "✓" : "—");
        return item;
    }

    private void UpdateSuffixButtons()
    {
        bool sel = _lvSuffix.SelectedItems.Count > 0;
        _btnEditSuffix.Enabled = _btnDeleteSuffix.Enabled = sel;
    }

    private void UpdateRecipientButtons()
    {
        bool sel = _lvRecipient.SelectedItems.Count > 0;
        _btnEditRecipient.Enabled = _btnDeleteRecipient.Enabled = sel;
    }

    private void UpdateUnroutedFields()
    {
        var action = _cboUnroutedAction.SelectedItem?.ToString() ?? "";
        var isOneDrive  = action == "OneDriveRedirect";
        var isSp        = action == "SharePointRedirect";

        _txtUnroutedOneDriveUser.Enabled = isOneDrive;
        _txtUnroutedOneDrivePath.Enabled = isOneDrive;

        _lblUnroutedSp.Visible          = isSp;
        _txtUnroutedSpSearch.Enabled    = isSp;
        _btnUnroutedSpSearch.Enabled    = isSp;
        _cboUnroutedSpResults.Enabled   = isSp;
        _lblUnroutedSpLibrary.Visible   = isSp;
        _cboUnroutedSpLibrary.Enabled   = isSp;
        _txtUnroutedSpFolder.Enabled    = isSp;
    }

    private static TabPage LayoutRuleTab(
        TabPage page, ListView lv, Button[] btns, string hint)
    {
        var desc = new Label
        {
            Dock = DockStyle.Top, Height = 52,
            Padding = new Padding(6, 6, 6, 0),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.DimGray,
            Text = hint
        };

        lv.Dock = DockStyle.Fill;

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 40,
            Padding = new Padding(4, 4, 4, 4),
            WrapContents = false,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        foreach (var b in btns) pnlBtns.Controls.Add(b);

        page.Controls.Add(lv);
        page.Controls.Add(desc);
        page.Controls.Add(pnlBtns);
        return page;
    }

    private static ListView MakeListView(IEnumerable<(string Name, int Width)> cols)
    {
        var lv = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9)
        };
        foreach (var (name, width) in cols)
            lv.Columns.Add(name, width);
        return lv;
    }

    private static Button RuleBtn(string text) => new Button
    {
        Text = text,
        Size = new Size(90, 28),
        Margin = new Padding(4, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = Color.FromArgb(200, 200, 210) },
        UseVisualStyleBackColor = true,
        Font = new Font("Segoe UI", 9)
    };

    private static ComboBox AddCombo(Panel pnl, string label, ref int y, string[] items)
    {
        pnl.Controls.Add(new Label
        {
            Text = label, Location = new Point(12, y),
            AutoSize = true, Font = new Font("Segoe UI", 9)
        });
        y += 20;
        var cbo = new ComboBox
        {
            Location = new Point(12, y), Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        foreach (var item in items) cbo.Items.Add(item);
        cbo.SelectedIndex = 0;
        pnl.Controls.Add(cbo);
        y += 32;
        return cbo;
    }

    private static CheckBox AddCheck(Panel pnl, string text, ref int y)
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

    private static TextBox AddField(Panel pnl, string label, ref int y,
        int width = 560, string placeholder = "")
    {
        pnl.Controls.Add(new Label
        {
            Text = label, Location = new Point(12, y),
            AutoSize = true, Font = new Font("Segoe UI", 9)
        });
        y += 20;
        var tb = new TextBox
        {
            Location = new Point(12, y), Width = width,
            Font = new Font("Segoe UI", 9),
            PlaceholderText = placeholder
        };
        pnl.Controls.Add(tb);
        y += 32;
        return tb;
    }

    private static void AddSectionHeader(Panel pnl, string text, ref int y)
    {
        pnl.Controls.Add(new Label
        {
            Text = text, Location = new Point(12, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        y += 24;
    }
}
