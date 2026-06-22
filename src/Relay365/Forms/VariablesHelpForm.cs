namespace Relay365.Forms;

/// <summary>
/// Reference popup listing all supported %variable% tokens for path and filename templates.
/// </summary>
public class VariablesHelpForm : Form
{
    private static readonly (string Token, string Description, string Notes)[] Vars =
    {
        ("%from%",                  "Sender's full email address",                     ""),
        ("%fromupn%",               "Sender's username (part before @)",               ""),
        ("%fromdomain%",            "Sender's email domain",                           ""),
        ("%to%",                    "Recipient's full email address",                  ""),
        ("%toupn%",                 "Recipient's username (part before @)",            ""),
        ("%todomain%",              "Recipient's email domain",                        ""),
        ("%tobasedomain%",          "Base domain configured on the suffix rule",       "Suffix rules only"),
        ("%suffix%",                "Subdomain captured by wildcard suffix match",     "Wildcard suffix rules only"),
        ("%subject%",               "Full email subject line",                         ""),
        ("%subject[n]%",            "Subject word at index n (0-based)",               "e.g. %subject[0]%, %subject[1]%"),
        ("%subject[*]%",            "All subject words",                               "Folder path: creates subfolders · Filename: joins with delimiter"),
        ("%date%",                  "Current date  (YYYYMMDD)",                        ""),
        ("%datetime%",              "Current date+time  (YYYYMMDDHHmmss)",             ""),
        ("%originalbasefilename%",  "Original attachment filename without extension",  "Filename templates only"),
    };

    public VariablesHelpForm()
    {
        Text = "Path & Filename Variable Reference";
        ClientSize = new Size(800, 490);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(new Label
        {
            Text = "All tokens are case-insensitive.  Subject-word splitting uses the delimiter from global settings or the per-rule override.  " +
                   "Variables marked 'Suffix rules only' resolve to empty string in other contexts.",
            Location = new Point(8, 8), AutoSize = false,
            Width = 778, Height = 32,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f)
        });

        var lv = new ListView
        {
            Location = new Point(8, 46), Size = new Size(778, 385),
            View = View.Details, FullRowSelect = true, GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Segoe UI", 9), MultiSelect = false
        };
        lv.Columns.Add("Variable token", 210);
        lv.Columns.Add("Description", 300);
        lv.Columns.Add("Context / Notes", 248);

        foreach (var (token, desc, notes) in Vars)
        {
            var item = new ListViewItem(token) { Font = new Font("Consolas", 9) };
            item.SubItems.Add(desc);
            item.SubItems.Add(notes);
            // Amber tint for variables that only work in certain contexts
            if (!string.IsNullOrEmpty(notes))
                item.ForeColor = Color.FromArgb(140, 90, 0);
            lv.Items.Add(item);
        }
        Controls.Add(lv);

        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(800 - 112, 454),
            Size = new Size(100, 28),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }
}
