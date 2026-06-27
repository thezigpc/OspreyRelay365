using OspreyRelay.Core.Config;

namespace OspreyRelay.App.Forms;

public class FtpUserEditorForm : Form
{
    public FtpUserConfig? Result { get; private set; }

    private readonly FtpUserConfig? _edit;
    private TextBox _txtUsername   = null!;
    private TextBox _txtPassword   = null!;
    private CheckBox _chkAnyPass  = null!;
    private TextBox _txtNotes     = null!;
    private Button _btnOk         = null!;
    private Button _btnCancel     = null!;

    public FtpUserEditorForm(FtpUserConfig? edit = null)
    {
        _edit = edit;
        InitializeComponent();
        if (edit != null) PopulateFromEdit(edit);
    }

    private void InitializeComponent()
    {
        Text            = _edit == null ? "Add FTP User" : "Edit FTP User";
        Size            = new Size(380, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        var pnl = new TableLayoutPanel
        {
            Dock       = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 5,
            Padding     = new Padding(12),
            AutoSize    = true
        };
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        _txtUsername = new TextBox { Dock = DockStyle.Fill };
        AddRow(pnl, row++, "Username:", _txtUsername);

        _txtPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        AddRow(pnl, row++, "Password:", _txtPassword);

        _chkAnyPass = new CheckBox { Text = "Accept any password", AutoSize = true };
        _chkAnyPass.CheckedChanged += (_, _) => _txtPassword.Enabled = !_chkAnyPass.Checked;
        AddRow(pnl, row++, "", _chkAnyPass);

        _txtNotes = new TextBox { Dock = DockStyle.Fill };
        AddRow(pnl, row++, "Notes (label):", _txtNotes);

        _btnOk     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Size = new Size(80, 28) };
        _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
        _btnOk.Click += (_, _) => TrySave();
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock          = DockStyle.Bottom,
            Height        = 38,
            Padding       = new Padding(0, 4, 8, 0)
        };
        btnRow.Controls.AddRange(new Control[] { _btnCancel, _btnOk });

        Controls.Add(pnl);
        Controls.Add(btnRow);
    }

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

    private void PopulateFromEdit(FtpUserConfig user)
    {
        _txtUsername.Text    = user.Username;
        _txtPassword.Text    = user.Password;
        _chkAnyPass.Checked  = user.AcceptAnyPassword;
        _txtNotes.Text       = user.Notes;
        _txtPassword.Enabled = !user.AcceptAnyPassword;
    }

    private void TrySave()
    {
        var username = _txtUsername.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show("Username is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = new FtpUserConfig
        {
            Username          = username,
            Password          = _chkAnyPass.Checked ? "" : _txtPassword.Text,
            AcceptAnyPassword = _chkAnyPass.Checked,
            Notes             = _txtNotes.Text.Trim()
        };

        DialogResult = DialogResult.OK;
    }
}
