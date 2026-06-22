using System.Net.Mail;
using Relay365.Core.Config;
using Relay365.Core.Logging;

namespace Relay365.Forms;

/// <summary>
/// Sends a test email through the local SMTP relay so you can verify routing,
/// file storage, and relay behaviour without an external mail client.
/// </summary>
public class TestSendForm : Form
{
    private readonly ConfigManager _configManager;
    private readonly RelayLogger _logger;

    private TextBox _txtFrom = null!;
    private TextBox _txtTo = null!;
    private TextBox _txtSubject = null!;
    private TextBox _txtBody = null!;
    private Label _lblAttachment = null!;
    private string _attachmentPath = "";
    private Button _btnSend = null!;
    private Label _lblStatus = null!;

    public TestSendForm(ConfigManager configManager, RelayLogger logger)
    {
        _configManager = configManager;
        _logger = logger;
        InitializeComponent();
        PreFill();
    }

    private void InitializeComponent()
    {
        Text = "Test Send";
        ClientSize = new Size(560, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var scroll = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };

        int y = 10;

        scroll.Controls.Add(new Label
        {
            Text = "Send a test email through the local relay (127.0.0.1 on the configured port).",
            Location = new Point(0, y), AutoSize = false, Width = 520, Height = 32,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9)
        });
        y += 36;

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
            Text = "This is a test email from 365 Relay."
        };
        scroll.Controls.Add(_txtBody);
        y += 108;

        // Attachment
        scroll.Controls.Add(new Label { Text = "Attachment (optional):", Location = new Point(0, y), AutoSize = true, Font = new Font("Segoe UI", 9) });
        y += 20;
        var btnBrowse = new Button
        {
            Text = "Browse…", Location = new Point(0, y), Size = new Size(90, 26),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true
        };
        _lblAttachment = new Label
        {
            Text = "(none)", Location = new Point(98, y + 4),
            AutoSize = false, Width = 420, ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 9)
        };
        btnBrowse.Click += (_, _) => BrowseAttachment();
        scroll.Controls.AddRange(new Control[] { btnBrowse, _lblAttachment });
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
        y += 28;

        // Send button + status
        _btnSend = new Button
        {
            Text = "Send Test Email",
            Location = new Point(0, y), Size = new Size(150, 32),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        _btnSend.Click += async (_, _) => await SendTestAsync();

        _lblStatus = new Label
        {
            Location = new Point(158, y + 8), AutoSize = false, Width = 360,
            Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray
        };

        scroll.Controls.AddRange(new Control[] { _btnSend, _lblStatus });

        Controls.Add(scroll);
    }

    private void PreFill()
    {
        var cfg = _configManager.Config;
        _txtFrom.Text    = string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)
                           ? "test@yourdomain.com" : cfg.FallbackSenderEmail;
        _txtTo.Text      = string.IsNullOrWhiteSpace(cfg.FallbackSenderEmail)
                           ? "recipient@yourdomain.com" : cfg.FallbackSenderEmail;
        _txtSubject.Text = "Test Relay Usage";
    }

    private void BrowseAttachment()
    {
        using var dlg = new OpenFileDialog { Title = "Select attachment", Filter = "All files|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _attachmentPath = dlg.FileName;
            _lblAttachment.Text = Path.GetFileName(_attachmentPath);
            _lblAttachment.ForeColor = Color.Black;
        }
    }

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
                Timeout = 15000
            };

            if (cfg.RequireSmtpAuth && !string.IsNullOrWhiteSpace(cfg.SmtpUsername))
            {
                smtp.Credentials = new System.Net.NetworkCredential(
                    cfg.SmtpUsername, cfg.SmtpPassword);
            }

            await smtp.SendMailAsync(msg);
            att?.Dispose();

            SetStatus("Sent successfully — check the activity log for routing result.", Color.FromArgb(40, 160, 40));
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
