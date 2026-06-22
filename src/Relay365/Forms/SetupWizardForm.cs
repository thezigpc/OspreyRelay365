using System.Text.Json;
using Azure.Identity;
using Microsoft.Graph;
using Relay365.Core.Config;
using Relay365.Core.Graph;
using Relay365.Core.Logging;

namespace Relay365.Forms;

/// <summary>
/// Multi-page wizard for Azure AD app registration only.
/// Pages: Welcome → (Manual: Credentials | Programmatic: AdminSignIn → AppRegPicker) → TestSave
/// Operational settings (port, auth, fallback sender, smarthost) live in RelaySettingsForm.
/// </summary>
public class SetupWizardForm : Form
{
    private enum Page { Welcome, ManualCreds, AdminSignIn, AppRegPicker, TestSave }

    // ── State ────────────────────────────────────────────────────────────────
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;
    private Page _current = Page.Welcome;
    private bool _useManual = true;
    private string _adminAccessToken = "";
    private AppRegistrationInfo? _selectedReg;

    // ── Page panels ──────────────────────────────────────────────────────────
    private Panel _pnlWelcome = null!;
    private Panel _pnlManualCreds = null!;
    private Panel _pnlAdminSignIn = null!;
    private Panel _pnlAppRegPicker = null!;
    private Panel _pnlTestSave = null!;

    // ── Welcome ──────────────────────────────────────────────────────────────
    private RadioButton _rdoManual = null!;
    private RadioButton _rdoProgrammatic = null!;

    // ── Manual credentials ───────────────────────────────────────────────────
    private TextBox _txtTenantId = null!;
    private TextBox _txtClientId = null!;
    private TextBox _txtClientSecret = null!;

    // ── Admin sign-in ────────────────────────────────────────────────────────
    private TextBox _txtAdminTenantId = null!;
    private TextBox _txtSetupClientId = null!;
    private Button _btnSignIn = null!;
    private Label _lblSignInStatus = null!;

    // ── App reg picker ───────────────────────────────────────────────────────
    private ListView _lvApps = null!;
    private Button _btnSearchApps = null!;
    private Button _btnCreateApp = null!;
    private Button _btnRegenerateSecret = null!;
    private Button _btnDeleteApp = null!;
    private Button _btnUpdatePermissions = null!;
    private Label _lblPickerStatus = null!;

    // ── Test & save ──────────────────────────────────────────────────────────
    private RichTextBox _rtbTestResults = null!;
    private Button _btnRunTest = null!;

    // ── Navigation ───────────────────────────────────────────────────────────
    private Button _btnBack = null!;
    private Button _btnNext = null!;
    private Button _btnCancel = null!;
    private Label _lblStep = null!;
    private Panel _pnlHeader = null!;
    private Label _lblHeaderTitle = null!;
    private Label _lblHeaderSub = null!;

    public SetupWizardForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger = logger;
        InitializeComponent();
        PrePopulate();
        ShowPage(Page.Welcome);
    }

    // ── Layout ───────────────────────────────────────────────────────────────
    private void InitializeComponent()
    {
        Text = "Configure App";
        ClientSize = new Size(640, 540);
        MinimumSize = new Size(600, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // Header
        _pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Color.FromArgb(30, 30, 60)
        };
        _lblHeaderTitle = new Label
        {
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(16, 10),
            AutoSize = true
        };
        _lblHeaderSub = new Label
        {
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(180, 190, 220),
            Location = new Point(18, 42),
            AutoSize = true
        };
        _pnlHeader.Controls.AddRange(new Control[] { _lblHeaderTitle, _lblHeaderSub });

        // Navigation bar
        var pnlNav = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.FromArgb(245, 245, 248)
        };
        _lblStep = new Label
        {
            Location = new Point(12, 16),
            AutoSize = true,
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray
        };
        _btnCancel = NavButton("Cancel", 3);
        _btnNext = NavButton("Next →", 2);
        _btnBack = NavButton("← Back", 1);
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnNext.Click += async (_, _) => await OnNextAsync();
        _btnBack.Click += (_, _) => OnBack();
        pnlNav.Controls.AddRange(new Control[] { _lblStep, _btnCancel, _btnNext, _btnBack });

        // Content area
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 8)
        };

        BuildWelcomePage();
        BuildManualCredsPage();
        BuildAdminSignInPage();
        BuildAppRegPickerPage();
        BuildTestSavePage();

        foreach (var p in AllPages())
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            pnlContent.Controls.Add(p);
        }

        Controls.Add(pnlContent);
        Controls.Add(pnlNav);
        Controls.Add(_pnlHeader);
    }

    private static Button NavButton(string text, int pos) => new Button
    {
        Text = text,
        Size = new Size(110, 32),
        Location = new Point(640 - pos * 120 - 12, 10),
        FlatStyle = FlatStyle.Flat,
        UseVisualStyleBackColor = true
    };

    // ── Page builders ─────────────────────────────────────────────────────────
    private void BuildWelcomePage()
    {
        _pnlWelcome = new Panel();
        var lbl = new Label
        {
            Text = "How would you like to connect to Microsoft 365?",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Location = new Point(0, 8)
        };
        _rdoManual = new RadioButton
        {
            Text = "Manual — I already have an app registration (Tenant ID, Client ID, Secret)",
            Location = new Point(0, 48),
            AutoSize = true,
            Checked = true
        };
        _rdoProgrammatic = new RadioButton
        {
            Text = "Automatic — Sign in as Global Admin and create or reuse a registration",
            Location = new Point(0, 78),
            AutoSize = true
        };
        var note = new Label
        {
            Text = "Automatic setup requires a Global Admin account and one additional\n" +
                   "\"setup\" app registration (public client, no secret). See the help link\n" +
                   "in the next step for instructions.",
            Location = new Point(18, 110),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f)
        };
        var settingsNote = new Label
        {
            Text = "Relay port, authentication, fallback sender, and smarthost failover\n" +
                   "are configured in Settings after completing this wizard.",
            Location = new Point(0, 170),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5f)
        };
        _pnlWelcome.Controls.AddRange(new Control[] { lbl, _rdoManual, _rdoProgrammatic, note, settingsNote });
    }

    private void BuildManualCredsPage()
    {
        _pnlManualCreds = new Panel();
        int y = 4;
        _txtTenantId = AddField(_pnlManualCreds, "Tenant ID (Directory ID):", ref y);
        _txtClientId = AddField(_pnlManualCreds, "Application (Client) ID:", ref y);
        _txtClientSecret = AddField(_pnlManualCreds, "Client Secret:", ref y, password: true);

        var info = new Label
        {
            Text = "Required Application permissions (with admin consent):\n" +
                   "  • Mail.Send  — for email relay\n" +
                   "  • Files.ReadWrite.All  — for OneDrive file storage\n" +
                   "  • Sites.ReadWrite.All  — for SharePoint file storage\n\n" +
                   "Mail.Send alone is sufficient for email-relay-only setups.",
            Location = new Point(0, y + 8),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f)
        };
        _pnlManualCreds.Controls.Add(info);
    }

    private void BuildAdminSignInPage()
    {
        _pnlAdminSignIn = new Panel();
        int y = 4;
        _txtAdminTenantId = AddField(_pnlAdminSignIn, "Tenant ID:", ref y);
        _txtSetupClientId = AddField(_pnlAdminSignIn, "Setup App Client ID:", ref y);

        var helpLink = new LinkLabel
        {
            Text = "How to create the Setup App (one-time step)",
            Location = new Point(0, y),
            AutoSize = true
        };
        helpLink.LinkClicked += (_, _) => ShowSetupAppHelp();
        y += 30;

        _btnSignIn = new Button
        {
            Text = "Sign In as Global Admin…",
            Location = new Point(0, y),
            Size = new Size(220, 34),
            UseVisualStyleBackColor = true
        };
        _btnSignIn.Click += async (_, _) => await SignInAsync();
        y += 48;

        _lblSignInStatus = new Label
        {
            Location = new Point(0, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Text = "Not signed in."
        };

        _pnlAdminSignIn.Controls.AddRange(
            new Control[] { helpLink, _btnSignIn, _lblSignInStatus });
    }

    private void BuildAppRegPickerPage()
    {
        _pnlAppRegPicker = new Panel();

        _lvApps = new ListView
        {
            Location = new Point(0, 0),
            Size = new Size(580, 220),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _lvApps.Columns.Add("Display Name", 220);
        _lvApps.Columns.Add("Client ID", 180);
        _lvApps.Columns.Add("Secret Expires", 110);
        _lvApps.SelectedIndexChanged += (_, _) =>
        {
            bool sel = _lvApps.SelectedItems.Count > 0;
            _btnRegenerateSecret.Enabled = sel;
            _btnDeleteApp.Enabled = sel;
            _btnUpdatePermissions.Enabled = sel;
            UpdatePickerSelection();
        };

        var btnRow = new FlowLayoutPanel
        {
            Location = new Point(0, 228),
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _btnSearchApps = new Button { Text = "Refresh List", Size = new Size(110, 30) };
        _btnSearchApps.Click += async (_, _) => await SearchAppsAsync();

        _btnCreateApp = new Button { Text = "Create New…", Size = new Size(110, 30) };
        _btnCreateApp.Click += async (_, _) => await CreateAppAsync();

        _btnRegenerateSecret = new Button
        {
            Text = "Regen Secret",
            Size = new Size(120, 30),
            Enabled = false
        };
        _btnRegenerateSecret.Click += async (_, _) => await RegenSecretAsync();

        _btnDeleteApp = new Button
        {
            Text = "Delete…",
            Size = new Size(110, 30),
            Enabled = false
        };
        _btnDeleteApp.Click += async (_, _) => await DeleteSelectedAsync();

        _btnUpdatePermissions = new Button
        {
            Text = "Update Permissions",
            Size = new Size(150, 30),
            Enabled = false
        };
        _btnUpdatePermissions.Click += async (_, _) => await UpdatePermissionsForSelectedAsync();

        btnRow.Controls.AddRange(new Control[]
        {
            _btnSearchApps, _btnCreateApp, _btnRegenerateSecret,
            _btnDeleteApp, _btnUpdatePermissions
        });

        _lblPickerStatus = new Label
        {
            Location = new Point(0, 268),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        _pnlAppRegPicker.Controls.AddRange(
            new Control[] { _lvApps, btnRow, _lblPickerStatus });
    }

    private void BuildTestSavePage()
    {
        _pnlTestSave = new Panel();
        _btnRunTest = new Button
        {
            Text = "Test Connection to Microsoft 365",
            Location = new Point(0, 4),
            Size = new Size(260, 34),
            UseVisualStyleBackColor = true
        };
        _btnRunTest.Click += async (_, _) => await RunTestAsync();

        _rtbTestResults = new RichTextBox
        {
            Location = new Point(0, 50),
            Size = new Size(580, 300),
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.FixedSingle
        };

        _pnlTestSave.Controls.AddRange(new Control[] { _btnRunTest, _rtbTestResults });
    }

    // ── Pre-populate from existing config ─────────────────────────────────────
    private void PrePopulate()
    {
        var cfg = _configManager.Config;
        _txtTenantId.Text = cfg.TenantId;
        _txtClientId.Text = cfg.ClientId;
        _txtClientSecret.Text = cfg.ClientSecret;
        _txtAdminTenantId.Text = cfg.TenantId;
        _txtSetupClientId.Text = cfg.SetupClientId;
    }

    // ── Navigation logic ──────────────────────────────────────────────────────
    private async Task OnNextAsync()
    {
        _btnNext.Enabled = false;
        try
        {
            switch (_current)
            {
                case Page.Welcome:
                    _useManual = _rdoManual.Checked;
                    ShowPage(_useManual ? Page.ManualCreds : Page.AdminSignIn);
                    break;

                case Page.ManualCreds:
                    if (!ValidateManualCreds()) break;
                    ShowPage(Page.TestSave);
                    break;

                case Page.AdminSignIn:
                    if (string.IsNullOrWhiteSpace(_adminAccessToken))
                    { ShowError("Please sign in first."); break; }
                    await SearchAppsAsync();
                    ShowPage(Page.AppRegPicker);
                    break;

                case Page.AppRegPicker:
                    if (_selectedReg == null)
                    { ShowError("Select an app registration or create a new one."); break; }
                    if (string.IsNullOrWhiteSpace(_selectedReg.ClientSecret)
                        && (_selectedReg.AppId != _configManager.Config.ClientId
                            || string.IsNullOrWhiteSpace(_configManager.Config.ClientSecret)))
                    {
                        ShowError("No client secret is available for this registration.\n\n" +
                                  "Click 'Regen Secret' to generate a new one before proceeding.");
                        break;
                    }
                    ShowPage(Page.TestSave);
                    break;

                case Page.TestSave:
                    Save();
                    DialogResult = DialogResult.OK;
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _btnNext.Enabled = true;
        }
    }

    private void OnBack()
    {
        var prev = (_current, _useManual) switch
        {
            (Page.ManualCreds, _)   => Page.Welcome,
            (Page.AdminSignIn, _)   => Page.Welcome,
            (Page.AppRegPicker, _)  => Page.AdminSignIn,
            (Page.TestSave, true)   => Page.ManualCreds,
            (Page.TestSave, false)  => Page.AppRegPicker,
            _                       => Page.Welcome
        };
        ShowPage(prev);
    }

    private void ShowPage(Page page)
    {
        _current = page;
        foreach (var p in AllPages()) p.Visible = false;
        GetPanel(page).Visible = true;

        (_lblHeaderTitle.Text, _lblHeaderSub.Text) = page switch
        {
            Page.Welcome       => ("Configure App", "Link 365 Relay to a Microsoft 365 app registration"),
            Page.ManualCreds   => ("Azure Credentials", "Enter your app registration details"),
            Page.AdminSignIn   => ("Admin Sign-In", "Sign in to auto-create or update a registration"),
            Page.AppRegPicker  => ("App Registration", "Select an existing registration or create one"),
            Page.TestSave      => ("Test & Save", "Verify the connection and save your configuration"),
            _                  => ("Configure App", "")
        };

        var pages = _useManual
            ? new[] { Page.Welcome, Page.ManualCreds, Page.TestSave }
            : new[] { Page.Welcome, Page.AdminSignIn, Page.AppRegPicker, Page.TestSave };

        var idx = Array.IndexOf(pages, page);
        _lblStep.Text = idx >= 0 ? $"Step {idx + 1} of {pages.Length}" : "";

        _btnBack.Enabled = page != Page.Welcome;
        _btnNext.Text = page == Page.TestSave ? "Save & Close" : "Next →";
    }

    // ── Sign-in ───────────────────────────────────────────────────────────────
    private async Task SignInAsync()
    {
        _btnSignIn.Enabled = false;
        _lblSignInStatus.Text = "Opening browser for sign-in…";
        _lblSignInStatus.ForeColor = Color.Gray;

        try
        {
            var tenantId = _txtAdminTenantId.Text.Trim();
            var clientId = _txtSetupClientId.Text.Trim();

            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Enter both Tenant ID and Setup App Client ID.");

            var credential = new InteractiveBrowserCredential(
                new InteractiveBrowserCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = clientId,
                    RedirectUri = new Uri("http://localhost"),
                    LoginHint = null
                });

            var scopes = new[]
            {
                "https://graph.microsoft.com/Application.ReadWrite.All",
                "https://graph.microsoft.com/AppRoleAssignment.ReadWrite.All"
            };

            var token = await credential.GetTokenAsync(
                new Azure.Core.TokenRequestContext(scopes));

            _adminAccessToken = token.Token;
            _lblSignInStatus.Text = "Signed in successfully.";
            _lblSignInStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            _lblSignInStatus.Text = $"Sign-in failed: {ex.Message}";
            _lblSignInStatus.ForeColor = Color.Red;
        }
        finally
        {
            _btnSignIn.Enabled = true;
        }
    }

    // ── App registration management ───────────────────────────────────────────
    private async Task SearchAppsAsync()
    {
        _lblPickerStatus.Text = "Searching…";
        _btnSearchApps.Enabled = false;
        try
        {
            var manager = BuildAdminManager();
            var apps = await manager.SearchExistingAsync();

            _lvApps.Items.Clear();
            foreach (var a in apps)
            {
                var item = new ListViewItem(a.DisplayName) { Tag = a };
                item.SubItems.Add(a.AppId);
                item.SubItems.Add(a.SecretExpiry.HasValue
                    ? a.SecretExpiry.Value.ToString("yyyy-MM-dd")
                    : "—");
                _lvApps.Items.Add(item);
            }

            _lblPickerStatus.Text = apps.Count == 0
                ? "No existing 365Relay registrations found. Create one below."
                : $"{apps.Count} registration(s) found.";

            // Auto-select the registration that matches the saved config
            var savedClientId = _configManager.Config.ClientId;
            if (!string.IsNullOrWhiteSpace(savedClientId))
            {
                foreach (ListViewItem item in _lvApps.Items)
                {
                    if (string.Equals(((AppRegistrationInfo)item.Tag!).AppId,
                        savedClientId, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                        item.EnsureVisible();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _lblPickerStatus.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _btnSearchApps.Enabled = true;
        }

        UpdatePickerSelection();
    }

    private async Task CreateAppAsync()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter a display name for the new app registration:",
            "Create App Registration",
            "365Relay");

        if (string.IsNullOrWhiteSpace(name)) return;

        _lblPickerStatus.Text = "Creating registration…";
        Cursor = Cursors.WaitCursor;
        try
        {
            var manager = BuildAdminManager();
            var info = await manager.CreateAsync(name);
            _selectedReg = info;

            var item = new ListViewItem(info.DisplayName) { Tag = info };
            item.SubItems.Add(info.AppId);
            item.SubItems.Add(info.SecretExpiry?.ToString("yyyy-MM-dd") ?? "—");
            _lvApps.Items.Add(item);
            item.Selected = true;

            _lblPickerStatus.Text = "Created. Secret has been captured.";
        }
        catch (Exception ex)
        {
            ShowError($"Create failed: {ex.Message}");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task RegenSecretAsync()
    {
        if (_lvApps.SelectedItems.Count == 0) return;
        var info = (AppRegistrationInfo)_lvApps.SelectedItems[0].Tag!;

        if (MessageBox.Show(
            $"Regenerate the client secret for '{info.DisplayName}'?\n" +
            "The old secret will stop working immediately.",
            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var manager = BuildAdminManager();
            _selectedReg = await manager.RegenerateSecretAsync(info);
            _lblPickerStatus.Text = "Secret regenerated. New secret captured.";
        }
        catch (Exception ex)
        {
            ShowError($"Regenerate failed: {ex.Message}");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task UpdatePermissionsForSelectedAsync()
    {
        if (_lvApps.SelectedItems.Count == 0) return;
        var info = (AppRegistrationInfo)_lvApps.SelectedItems[0].Tag!;

        if (string.IsNullOrWhiteSpace(info.ServicePrincipalId))
        {
            ShowError("Service principal ID not found for this registration.\n" +
                      "Try clicking 'Refresh List' then select the app again.");
            return;
        }

        _lblPickerStatus.Text = "Updating permissions…";
        Cursor = Cursors.WaitCursor;
        try
        {
            var manager = BuildAdminManager();
            await manager.UpdatePermissionsAsync(info.ServicePrincipalId);
            _lblPickerStatus.Text = "Permissions updated — Files.ReadWrite.All and Sites.ReadWrite.All granted.";
        }
        catch (Exception ex)
        {
            ShowError($"Permission update failed: {ex.Message}");
            _lblPickerStatus.Text = "Permission update failed.";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_lvApps.SelectedItems.Count == 0) return;
        var item = _lvApps.SelectedItems[0];
        var info = (AppRegistrationInfo)item.Tag!;

        if (MessageBox.Show(
            $"Permanently delete the app registration '{info.DisplayName}'?\n\n" +
            "This removes it from Azure AD and cannot be undone.",
            "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var manager = BuildAdminManager();
            await manager.DeleteAsync(info.ObjectId);
            _lvApps.Items.Remove(item);
            if (_selectedReg?.ObjectId == info.ObjectId)
                _selectedReg = null;
            _lblPickerStatus.Text = $"'{info.DisplayName}' deleted.";
        }
        catch (Exception ex)
        {
            ShowError($"Delete failed: {ex.Message}");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void UpdatePickerSelection()
    {
        if (_lvApps.SelectedItems.Count == 0) return;
        _selectedReg = (AppRegistrationInfo)_lvApps.SelectedItems[0].Tag!;

        if (!string.IsNullOrWhiteSpace(_selectedReg.ClientSecret))
        {
            _lblPickerStatus.ForeColor = Color.Green;
            _lblPickerStatus.Text = "Secret captured — ready to proceed.";
        }
        else if (_selectedReg.AppId == _configManager.Config.ClientId
                 && !string.IsNullOrWhiteSpace(_configManager.Config.ClientSecret))
        {
            _lblPickerStatus.ForeColor = Color.DimGray;
            _lblPickerStatus.Text = "Using previously saved secret for this app.";
        }
        else
        {
            _lblPickerStatus.ForeColor = Color.DarkOrange;
            _lblPickerStatus.Text = "No secret available — click 'Regen Secret' before proceeding.";
        }
    }

    private AppRegistrationManager BuildAdminManager()
    {
        if (string.IsNullOrWhiteSpace(_adminAccessToken))
            throw new InvalidOperationException("Not signed in.");
        var graphClient = Relay365.Core.Graph.GraphClientFactory.CreateWithToken(_adminAccessToken);
        return new AppRegistrationManager(graphClient, _logger);
    }

    // ── Test connection ───────────────────────────────────────────────────────
    private async Task RunTestAsync()
    {
        _btnRunTest.Enabled = false;
        _rtbTestResults.Clear();

        var ok  = Color.FromArgb(80, 210, 80);
        var warn = Color.FromArgb(230, 180, 60);
        var err  = Color.FromArgb(230, 80, 80);
        var dim  = Color.FromArgb(160, 160, 180);

        void Log(string msg, Color? col = null)
        {
            _rtbTestResults.SelectionColor = col ?? Color.FromArgb(200, 200, 200);
            _rtbTestResults.AppendText(msg + Environment.NewLine);
        }

        try
        {
            var cfg = BuildConfigFromInputs();

            if (!cfg.IsConfigured)
            {
                Log("✗ Configuration is incomplete — fill in Tenant ID, Client ID and Client Secret.", err);
                return;
            }

            // Step 1: acquire an OAuth token — proves tenant ID + client ID + secret are correct.
            Log("Step 1: Authenticating with Microsoft 365…", dim);
            var credential = new Azure.Identity.ClientSecretCredential(
                cfg.TenantId, cfg.ClientId, cfg.ClientSecret);

            Exception? lastAuthEx = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await credential.GetTokenAsync(
                        new Azure.Core.TokenRequestContext(
                            new[] { "https://graph.microsoft.com/.default" }));
                    lastAuthEx = null;
                    break;
                }
                catch (Azure.Identity.AuthenticationFailedException ex)
                {
                    lastAuthEx = ex;
                    if (attempt < 3)
                    {
                        Log($"  Auth attempt {attempt} failed — Azure AD may still be propagating.", warn);
                        Log($"  Retrying in 10 seconds… (attempt {attempt + 1}/3)", dim);
                        await Task.Delay(10_000);
                    }
                }
            }
            if (lastAuthEx != null) throw lastAuthEx;

            Log("✓ Credentials accepted (Tenant ID, Client ID, Secret are valid).", ok);

            // Step 2: check fallback sender mailbox if configured
            Log("", dim);
            Log("Step 2: Verifying fallback sender mailbox…", dim);
            if (!string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail))
            {
                try
                {
                    var client = Relay365.Core.Graph.GraphClientFactory.Create(cfg);
                    var user = await client.Users[cfg.FallbackSenderEmail].GetAsync(req =>
                        req.QueryParameters.Select = new[] { "displayName", "mail", "id" });
                    Log($"✓ Mailbox found: {user?.DisplayName} <{user?.Mail}>", ok);
                }
                catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode == 403)
                {
                    Log($"  Mailbox lookup skipped — app lacks User.Read.All permission.", warn);
                    Log($"  This is fine: Mail.Send is all that's needed to relay.", warn);
                }
            }
            else
            {
                Log("  No fallback sender configured — configure one in Settings.", dim);
                Log("  (Without a fallback sender, emails from external addresses may be rejected.)", dim);
            }

            Log("", dim);
            Log("─────────────────────────────────────────", dim);
            Log("Configuration looks good. Click Save & Close to finish.", ok);
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            Log($"✗ Authentication failed: {ex.Message}", err);
            Log("  Check your Tenant ID, Client ID, and Client Secret.", err);
        }
        catch (Exception ex)
        {
            Log($"✗ Test failed: {ex.Message}", err);
        }
        finally
        {
            _btnRunTest.Enabled = true;
        }
    }

    // ── Validation & save ─────────────────────────────────────────────────────
    private bool ValidateManualCreds()
    {
        if (string.IsNullOrWhiteSpace(_txtTenantId.Text) ||
            string.IsNullOrWhiteSpace(_txtClientId.Text) ||
            string.IsNullOrWhiteSpace(_txtClientSecret.Text))
        {
            ShowError("All three fields are required.");
            return false;
        }
        return true;
    }

    private void Save()
    {
        var cfg = BuildConfigFromInputs();
        _configManager.Save(cfg);
    }

    private RelayConfig BuildConfigFromInputs()
    {
        // Deep-clone the existing config so all non-wizard settings (routing rules,
        // unrouted config, operational settings, etc.) are preserved.
        var existing = _configManager.Config;
        var options  = new JsonSerializerOptions { WriteIndented = false };
        var clone    = JsonSerializer.Deserialize<RelayConfig>(
                           JsonSerializer.Serialize(existing, options), options)!;
        // Copy non-serialized (JsonIgnore) fields
        clone.ClientSecret       = existing.ClientSecret;
        clone.SmtpPassword       = existing.SmtpPassword;
        clone.SmarthostPassword  = existing.SmarthostPassword;

        // Overlay only the Azure AD credentials that this wizard owns
        if (_useManual)
        {
            clone.TenantId = _txtTenantId.Text.Trim();
            clone.ClientId = _txtClientId.Text.Trim();
            clone.ClientSecret = _txtClientSecret.Text.Trim();
            // AppRegistrationName/ObjectId not touched in manual mode
        }
        else
        {
            clone.TenantId = _txtAdminTenantId.Text.Trim();
            clone.ClientId = _selectedReg?.AppId ?? "";
            clone.ClientSecret = !string.IsNullOrWhiteSpace(_selectedReg?.ClientSecret)
                ? _selectedReg!.ClientSecret
                : existing.ClientSecret;
            clone.AppRegistrationName      = _selectedReg?.DisplayName ?? "";
            clone.AppRegistrationObjectId  = _selectedReg?.ObjectId ?? "";
            clone.SetupClientId            = _txtSetupClientId.Text.Trim();
        }

        return clone;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static TextBox AddField(Panel parent, string label, ref int y,
        bool password = false)
    {
        parent.Controls.Add(new Label
        {
            Text = label,
            Location = new Point(0, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9)
        });
        y += 22;
        var tb = new TextBox
        {
            Location = new Point(0, y),
            Width = 560,
            UseSystemPasswordChar = password,
            Font = new Font("Segoe UI", 9)
        };
        parent.Controls.Add(tb);
        y += 32;
        return tb;
    }

    private IEnumerable<Panel> AllPages() => new[]
    {
        _pnlWelcome, _pnlManualCreds, _pnlAdminSignIn,
        _pnlAppRegPicker, _pnlTestSave
    };

    private Panel GetPanel(Page p) => p switch
    {
        Page.Welcome      => _pnlWelcome,
        Page.ManualCreds  => _pnlManualCreds,
        Page.AdminSignIn  => _pnlAdminSignIn,
        Page.AppRegPicker => _pnlAppRegPicker,
        Page.TestSave     => _pnlTestSave,
        _                 => _pnlWelcome
    };

    private static void ShowError(string msg) =>
        MessageBox.Show(msg, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static void ShowSetupAppHelp()
    {
        MessageBox.Show(
            "To create the one-time Setup App in Azure AD:\n\n" +
            "1. Go to portal.azure.com → Azure Active Directory → App registrations → New registration\n" +
            "2. Name it anything (e.g., '365Relay Setup')\n" +
            "3. Supported account types: Accounts in this org only\n" +
            "4. Click Register\n" +
            "5. Go to Authentication → Add a platform → Mobile and desktop\n" +
            "6. Add redirect URI: http://localhost\n" +
            "7. Enable 'Allow public client flows' → Save\n" +
            "8. Go to API permissions → Add a permission → Microsoft Graph → Delegated\n" +
            "9. Add: Application.ReadWrite.All  and  AppRoleAssignment.ReadWrite.All\n" +
            "10. Click 'Grant admin consent for [tenant]' → Yes\n" +
            "11. Copy the Application (client) ID from the Overview page and paste it here.",
            "Creating the Setup App",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
