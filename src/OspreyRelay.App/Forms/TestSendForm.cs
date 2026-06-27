using System.Net.Mail;
using System.Text.Json;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;

namespace OspreyRelay.App.Forms;

/// <summary>
/// Sends a test email through the local SMTP relay so you can verify routing,
/// file storage, and relay behaviour without an external mail client.
/// Fields are automatically saved on each send and restored on next open.
/// </summary>
public class TestSendForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger   _logger;

    private TextBox _txtFrom    = null!;
    private TextBox _txtTo      = null!;
    private TextBox _txtSubject = null!;
    private TextBox _txtBody    = null!;
    private Label   _lblAttachment = null!;
    private string  _attachmentPath = "";
    private Button  _btnSend   = null!;
    private Button  _btnReset  = null!;
    private Label   _lblStatus = null!;

    private static string TemplatePath =>
        Path.Combine(ConfigManager.GetConfigDir(), "testsend-template.json");

    public TestSendForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger        = logger;
        InitializeComponent();
        LoadTemplate();
    }

    private void InitializeComponent()
    {
        Text            = "Test Send";
        ClientSize      = new Size(560, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        FormClosing += (_, _) => SaveTemplate();

        var scroll = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };

        int y = 10;

        scroll.Controls.Add(new Label
        {
            Text = "Send a test email through the local relay (127.0.0.1 on the configured port).\n" +
                   "Fields are saved automatically after each send.",
            Location = new Point(0, y), AutoSize = false, Width = 520, Height = 40,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9)
        });
        y += 44;

        _txtFrom    = Field(scroll, "From:", ref y, placeholder: "copier@yourdomain.com");
        _txtTo      = Field(scroll, "To:", ref y, placeholder: "user@yourdomain.com");
        _txtSubject = Field(scroll, "Subject:", ref y, placeholder: "Test Relay Usage");

        scroll.Controls.Add(new Label { Text = "Body:", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        _txtBody = new TextBox
        {
            Location = new Point(0, y), Width = 520, Height = 100,
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 9),
            Text = "This is a test email from Osprey Relay for M365."
        };
        scroll.Controls.Add(_txtBody);
        y += 108;

        // Attachment row
        scroll.Controls.Add(new Label { Text = "Attachment (optional):", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        var btnBrowse = new Button
        {
            Text = "Browse…", Location = new Point(0, y), Size = new Size(90, 26),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true
        };
        var btnClearAtt = new Button
        {
            Text = "Clear", Location = new Point(96, y), Size = new Size(56, 26),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            ForeColor = Color.DimGray
        };
        _lblAttachment = new Label
        {
            Text = "(none)", Location = new Point(160, y + 4),
            AutoSize = false, Width = 360, ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 9)
        };
        btnBrowse.Click   += (_, _) => BrowseAttachment();
        btnClearAtt.Click += (_, _) => ClearAttachment();
        scroll.Controls.AddRange(new Control[] { btnBrowse, btnClearAtt, _lblAttachment });
        y += 36;

        // Port info
        var cfg = _configManager.Config;
        scroll.Controls.Add(new Label
        {
            Text = $"Relay SMTP port: {cfg.RelayPort}   " +
                   (cfg.RequireSmtpAuth ? $"Auth required (user: {cfg.SmtpUsername})" : "No auth required"),
            Location = new Point(0, y), AutoSize = true,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        });
        y += 30;

        // Bottom row: Send | Reset | status
        _btnSend = new Button
        {
            Text = "Send Test Email",
            Location = new Point(0, y), Size = new Size(150, 32),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        _btnReset = new Button
        {
            Text = "Reset",
            Location = new Point(158, y), Size = new Size(80, 32),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.DimGray
        };
        _lblStatus = new Label
        {
            Location = new Point(246, y + 8), AutoSize = false, Width = 300,
            Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray
        };

        _btnSend.Click  += async (_, _) => await SendTestAsync();
        _btnReset.Click += (_, _) => ResetToDefaults();

        scroll.Controls.AddRange(new Control[] { _btnSend, _btnReset, _lblStatus });

        Controls.Add(scroll);
    }

    // ── Template persistence ──────────────────────────────────────────────────

    private sealed record TestSendTemplate(
        string From,
        string To,
        string Subject,
        string Body,
        string AttachmentPath);

    private void LoadTemplate()
    {
        try
        {
            if (File.Exists(TemplatePath))
            {
                var json = File.ReadAllText(TemplatePath);
                var t    = JsonSerializer.Deserialize<TestSendTemplate>(json);
                if (t != null)
                {
                    _txtFrom.Text    = t.From;
                    _txtTo.Text      = t.To;
                    _txtSubject.Text = t.Subject;
                    _txtBody.Text    = t.Body;
                    if (!string.IsNullOrWhiteSpace(t.AttachmentPath))
                        SetAttachment(t.AttachmentPath);
                    return;
                }
            }
        }
        catch { /* corrupt template — fall through to defaults */ }

        ApplyDefaults();
    }

    private void SaveTemplate()
    {
        try
        {
            var t    = new TestSendTemplate(_txtFrom.Text, _txtTo.Text, _txtSubject.Text, _txtBody.Text, _attachmentPath);
            var json = JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TemplatePath, json);
        }
        catch { /* non-fatal */ }
    }

    private void ResetToDefaults()
    {
        ApplyDefaults();
        ClearAttachment();
        try { if (File.Exists(TemplatePath)) File.Delete(TemplatePath); } catch { }
        SetStatus("Reset to defaults.", Color.DimGray);
    }

    private void ApplyDefaults()
    {
        var cfg = _configManager.Config;
        _txtFrom.Text    = string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)
                           ? "test@yourdomain.com" : cfg.FallbackSenderEmail;
        _txtTo.Text      = string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)
                           ? "recipient@yourdomain.com" : cfg.FallbackSenderEmail;
        _txtSubject.Text = "Test Relay Usage";
        _txtBody.Text    = "This is a test email from Osprey Relay for M365.";
    }

    // ── Attachment helpers ────────────────────────────────────────────────────

    private void BrowseAttachment()
    {
        var initial = string.IsNullOrWhiteSpace(_attachmentPath)
            ? null
            : Path.GetDirectoryName(_attachmentPath);
        using var picker = new PathPickerDialog("Select Attachment", initial, folderOnly: false);
        if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedFile != null)
            SetAttachment(picker.SelectedFile);
    }

    private void SetAttachment(string path)
    {
        _attachmentPath          = path;
        _lblAttachment.Text      = Path.GetFileName(path);
        _lblAttachment.ForeColor = File.Exists(path) ? Color.Black : Color.DarkOrange;
    }

    private void ClearAttachment()
    {
        _attachmentPath          = "";
        _lblAttachment.Text      = "(none)";
        _lblAttachment.ForeColor = Color.DimGray;
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    private async Task SendTestAsync()
    {
        var from    = _txtFrom.Text.Trim();
        var to      = _txtTo.Text.Trim();
        var subject = _txtSubject.Text.Trim();
        var body    = _txtBody.Text;

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            SetStatus("From and To are required.", Color.DarkOrange);
            return;
        }

        _btnSend.Enabled = false;
        SetStatus("Sending…", Color.DimGray);

        try
        {
            var cfg = _configManager.Config;
            using var msg = new MailMessage(from, to, subject, body);
            msg.IsBodyHtml = false;

            Attachment? att = null;
            if (!string.IsNullOrWhiteSpace(_attachmentPath) && File.Exists(_attachmentPath))
            {
                att = new Attachment(_attachmentPath);
                msg.Attachments.Add(att);
            }

            using var smtp = new SmtpClient("127.0.0.1", cfg.RelayPort)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 120_000
            };

            if (cfg.RequireSmtpAuth && !string.IsNullOrWhiteSpace(cfg.SmtpUsername))
            {
                smtp.Credentials = new System.Net.NetworkCredential(
                    cfg.SmtpUsername, cfg.SmtpPassword);
            }

            await smtp.SendMailAsync(msg);
            att?.Dispose();

            SaveTemplate();

            SetStatus("Sent — check the activity log for routing result.", Color.FromArgb(40, 160, 40));
            _logger.Info($"[TestSend] from={from} to={to} subject='{subject}'" +
                         (string.IsNullOrWhiteSpace(_attachmentPath) ? "" : $" attachment={Path.GetFileName(_attachmentPath)}"));
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", Color.DarkRed);
            _logger.Error($"[TestSend] failed: {ex.Message}");
        }
        finally
        {
            _btnSend.Enabled = true;
        }
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text      = text;
        _lblStatus.ForeColor = color;
    }

    private static TextBox Field(Panel pnl, string label, ref int y, string placeholder = "")
    {
        pnl.Controls.Add(new Label { Text = label, Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        var tb = new TextBox { Location = new Point(0, y), Width = 520, Font = new Font("Segoe UI", 9), PlaceholderText = placeholder };
        pnl.Controls.Add(tb);
        y += 30;
        return tb;
    }
}
