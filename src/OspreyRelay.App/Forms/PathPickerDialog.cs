namespace OspreyRelay.App.Forms;

/// <summary>
/// Shell-free folder/file picker built entirely on WinForms TreeView + ListView.
/// Works on Windows Server where explorer.exe is not running.
///
/// FolderOnly = true  → folder picker (returns SelectedFolder)
/// FolderOnly = false → file picker with file list panel (returns SelectedFile)
/// </summary>
public class PathPickerDialog : Form
{
    // ── Public results ────────────────────────────────────────────────────────
    public string? SelectedFolder { get; private set; }
    public string? SelectedFile   { get; private set; }

    // ── Configuration ─────────────────────────────────────────────────────────
    private readonly bool   _folderOnly;
    private readonly string _title;
    private readonly string _initialPath;

    // ── Controls ──────────────────────────────────────────────────────────────
    private TreeView  _tree   = null!;
    private ListView  _files  = null!;    // folder-only mode: hidden
    private TextBox   _txtPath = null!;
    private Button    _btnOk = null!, _btnCancel = null!;
    private Label     _lblStatus = null!;

    private static readonly ImageList TreeIcons = BuildTreeIcons();

    public PathPickerDialog(
        string title,
        string? initialPath = null,
        bool folderOnly = true)
    {
        _title       = title;
        _initialPath = initialPath ?? "";
        _folderOnly  = folderOnly;
        InitializeComponent();
        LoadDrives();
        if (!string.IsNullOrWhiteSpace(_initialPath))
            TryExpandToPath(_initialPath);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text            = _title;
        Size            = new Size(_folderOnly ? 500 : 800, 520);
        MinimumSize     = new Size(_folderOnly ? 400 : 600, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;

        _tree = new TreeView
        {
            Dock        = DockStyle.Fill,
            ImageList   = TreeIcons,
            ShowLines   = true,
            ShowPlusMinus = true,
            HideSelection = false,
            Font        = new Font("Segoe UI", 9)
        };
        _tree.BeforeExpand     += OnBeforeExpand;
        _tree.AfterSelect      += OnTreeSelect;

        _files = new ListView
        {
            Dock        = DockStyle.Fill,
            View        = View.List,
            MultiSelect = false,
            Font        = new Font("Segoe UI", 9),
            Visible     = !_folderOnly
        };
        _files.SelectedIndexChanged += OnFileSelect;
        _files.DoubleClick          += (_, _) => AcceptSelection();

        _txtPath = new TextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = false,
            Font      = new Font("Segoe UI", 9),
            Text      = _initialPath
        };
        _txtPath.TextChanged += (_, _) => _btnOk.Enabled = !string.IsNullOrWhiteSpace(_txtPath.Text);

        _lblStatus = new Label
        {
            Dock      = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Font      = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Text      = _folderOnly
                ? "Navigate to a folder and click OK, or type the path below."
                : "Navigate to a folder, select a file, and click OK."
        };

        _btnOk = new Button
        {
            Text     = "OK",
            Size     = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true,
            Enabled  = !string.IsNullOrWhiteSpace(_initialPath)
        };
        _btnCancel = new Button
        {
            Text     = "Cancel",
            Size     = new Size(88, 30),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = true
        };
        _btnOk.Click     += (_, _) => AcceptSelection();
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        // Layout
        var pnlBottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 40,
            ColumnCount = 4, RowCount = 1,
            Padding = new Padding(6, 4, 6, 4)
        };
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));  // label
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // path box
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));  // OK
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));  // Cancel
        pnlBottom.Controls.Add(new Label { Text = "Path:", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleRight }, 0, 0);
        pnlBottom.Controls.Add(_txtPath, 1, 0);
        pnlBottom.Controls.Add(_btnOk, 2, 0);
        pnlBottom.Controls.Add(_btnCancel, 3, 0);

        var pnlHint = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 4, 4, 0) };
        pnlHint.Controls.Add(_lblStatus);

        if (_folderOnly)
        {
            Controls.Add(_tree);
        }
        else
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill
            };
            split.Panel1.Controls.Add(_tree);
            split.Panel2.Controls.Add(_files);
            Controls.Add(split);
        }

        Controls.Add(pnlHint);
        Controls.Add(pnlBottom);
    }

    // ── Drive / folder loading ────────────────────────────────────────────────

    private void LoadDrives()
    {
        _tree.BeginUpdate();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                var node = MakeNode(label, drive.RootDirectory.FullName, 0);
                TryAddPlaceholder(node);
                _tree.Nodes.Add(node);
            }
            catch { /* skip inaccessible drives */ }
        }
        _tree.EndUpdate();
    }

    private static TreeNode MakeNode(string label, string path, int imageIndex) =>
        new(label) { Tag = path, ImageIndex = imageIndex, SelectedImageIndex = imageIndex };

    private static void TryAddPlaceholder(TreeNode parent)
    {
        try
        {
            if (Directory.EnumerateDirectories((string)parent.Tag!).Any())
                parent.Nodes.Add(new TreeNode("…") { Tag = "" }); // lazy-load placeholder
        }
        catch { /* can't enumerate — no placeholder */ }
    }

    private void OnBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node!;
        if (node.Nodes.Count == 1 && node.Nodes[0].Tag is string s && s == "")
        {
            node.Nodes.Clear();
            ExpandNode(node);
        }
    }

    private void ExpandNode(TreeNode parent)
    {
        var path = (string)parent.Tag!;
        try
        {
            _tree.BeginUpdate();
            foreach (var dir in Directory.GetDirectories(path)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                var child = MakeNode(name, dir, 1);
                TryAddPlaceholder(child);
                parent.Nodes.Add(child);
            }
        }
        catch { /* skip inaccessible */ }
        finally { _tree.EndUpdate(); }
    }

    private void OnTreeSelect(object? sender, TreeViewEventArgs e)
    {
        var path = e.Node?.Tag as string;
        if (string.IsNullOrEmpty(path)) return;

        _txtPath.Text = path;
        SelectedFolder = path;

        if (!_folderOnly)
            PopulateFiles(path);

        _btnOk.Enabled = true;
    }

    private void PopulateFiles(string folderPath)
    {
        _files.Items.Clear();
        try
        {
            foreach (var file in Directory.GetFiles(folderPath)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(Path.GetFileName(file)) { Tag = file };
                _files.Items.Add(item);
            }
        }
        catch { /* skip inaccessible */ }
    }

    private void OnFileSelect(object? sender, EventArgs e)
    {
        if (_files.SelectedItems.Count == 0) return;
        var filePath = (string)_files.SelectedItems[0].Tag!;
        _txtPath.Text  = filePath;
        SelectedFile   = filePath;
        _btnOk.Enabled = true;
    }

    // ── Accept ────────────────────────────────────────────────────────────────

    private void AcceptSelection()
    {
        var typed = _txtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(typed)) return;

        if (_folderOnly)
        {
            SelectedFolder = typed;
        }
        else
        {
            if (File.Exists(typed))
                SelectedFile = typed;
            else if (Directory.Exists(typed))
            { /* they typed a folder path — don't accept, nudge */ return; }
            else
                SelectedFile = typed; // accept typed path even if not yet existing
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Navigate to initial path ──────────────────────────────────────────────

    private void TryExpandToPath(string targetPath)
    {
        try
        {
            // Find which drive node matches
            foreach (TreeNode driveNode in _tree.Nodes)
            {
                var drivePath = (string)driveNode.Tag!;
                if (!targetPath.StartsWith(drivePath, StringComparison.OrdinalIgnoreCase)) continue;

                var segments = targetPath[drivePath.Length..]
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

                var current = driveNode;
                driveNode.Expand();

                foreach (var seg in segments)
                {
                    var child = current.Nodes.Cast<TreeNode>()
                        .FirstOrDefault(n => string.Equals(
                            n.Text, seg, StringComparison.OrdinalIgnoreCase));
                    if (child == null) break;
                    child.Expand();
                    current = child;
                }

                _tree.SelectedNode = current;
                current.EnsureVisible();
                break;
            }
        }
        catch { /* best-effort */ }
    }

    // ── Icons ─────────────────────────────────────────────────────────────────

    private static ImageList BuildTreeIcons()
    {
        var il = new ImageList { ImageSize = new Size(16, 16) };
        // Drive icon (index 0): simple grey square with letter
        il.Images.Add(CreateIcon(Color.SteelBlue));   // 0 = drive
        il.Images.Add(CreateIcon(Color.Goldenrod));    // 1 = folder
        return il;
    }

    private static Bitmap CreateIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.FillRectangle(new SolidBrush(color), 2, 3, 12, 10);
        return bmp;
    }
}
