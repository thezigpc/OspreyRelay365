using Relay365.Core.Config;

namespace Relay365.Forms;

/// <summary>
/// Edits the per-sender routing table.
/// Optional overrides: From: address → specific 365 mailbox.
/// Addresses not in the table use normal passthrough / fallback logic.
/// </summary>
public class SenderRoutesForm : Form
{
    private readonly ConfigManager _configManager;
    private DataGridView _grid = null!;
    private Button _btnAdd = null!;
    private Button _btnRemove = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    public SenderRoutesForm(ConfigManager configManager)
    {
        _configManager = configManager;
        InitializeComponent();
        LoadRoutes();
    }

    private void InitializeComponent()
    {
        Text = "Sender Routing Rules";
        Size = new Size(640, 440);
        MinimumSize = new Size(520, 360);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        // Description
        var lblDesc = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(10, 8, 10, 0),
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(60, 60, 80),
            Text = "Optional: force specific From: addresses to send via a particular 365 mailbox.\r\n" +
                   "Addresses not listed here use normal From→From passthrough (or the fallback sender).\r\n" +
                   "Tip: use the exact address the device puts in its MAIL FROM / From: header."
        };

        // Grid
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(220, 220, 230),
            RowHeadersVisible = false,
            Font = new Font("Segoe UI", 9),
            EditMode = DataGridViewEditMode.EditOnEnter
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "From",
            HeaderText = "From: Address  (device sends this)",
            FillWeight = 50
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Via",
            HeaderText = "Send Via  (365 mailbox to use)",
            FillWeight = 50
        });

        // Placeholder row styling
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(30, 30, 30);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 250);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 255);

        // Bottom buttons
        var pnlButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.FromArgb(245, 245, 248)
        };

        _btnAdd = Btn("+ Add Row", 0);
        _btnRemove = Btn("− Remove Row", 1);
        _btnSave = Btn("Save", 3);
        _btnCancel = Btn("Cancel", 4);

        _btnAdd.Click += (_, _) =>
        {
            _grid.Rows.Add("", "");
            _grid.CurrentCell = _grid.Rows[^1].Cells[0];
            _grid.BeginEdit(true);
        };

        _btnRemove.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (!row.IsNewRow)
                    _grid.Rows.Remove(row);
        };

        _btnSave.Click += (_, _) => SaveAndClose();
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlButtons.Controls.AddRange(
            new Control[] { _btnAdd, _btnRemove, _btnSave, _btnCancel });

        // Layout (add in reverse-dock order)
        Controls.Add(_grid);
        Controls.Add(lblDesc);
        Controls.Add(pnlButtons);
    }

    private static Button Btn(string text, int idx) => new Button
    {
        Text = text,
        Location = idx < 2
            ? new Point(8 + idx * 126, 8)                     // left side
            : new Point(640 - (5 - idx) * 126 - 16, 8),       // right side
        Size = new Size(118, 30),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderColor = Color.FromArgb(200, 200, 210) },
        UseVisualStyleBackColor = true,
        Font = new Font("Segoe UI", 9)
    };

    private void LoadRoutes()
    {
        var routes = _configManager.Config.SenderRoutes;
        foreach (var kv in routes)
            _grid.Rows.Add(kv.Key, kv.Value);
    }

    private void SaveAndClose()
    {
        _grid.EndEdit();

        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var from = (row.Cells["From"].Value?.ToString() ?? "").Trim();
            var via  = (row.Cells["Via"].Value?.ToString()  ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(via))
                routes[from] = via;
        }

        var cfg = _configManager.Config;
        cfg.SenderRoutes = routes;
        _configManager.Save(cfg);

        DialogResult = DialogResult.OK;
        Close();
    }
}
