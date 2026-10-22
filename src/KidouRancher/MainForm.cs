using KidouRancher.Models;
using KidouRancher.Services;

namespace KidouRancher;

public sealed class MainForm : Form
{
    private static readonly Color PendingColor = Color.FromArgb(120, 132, 140);
    private static readonly Color NormalColor = Color.FromArgb(44, 59, 68);
    private static readonly Color ValidColor = Color.FromArgb(28, 128, 76);
    private static readonly Color InvalidColor = Color.FromArgb(185, 73, 61);
    private static readonly Color DropIndicatorColor = Color.FromArgb(28, 112, 93);

    // 一覧内のドラッグ＆ドロップによる並び替えで使う、アプリ内専用のデータ形式
    private const string ProjectDragFormat = "KidouRancher.ProjectReorder";
    private const string ItemsDragFormat = "KidouRancher.ItemsReorder";

    private readonly ProjectStore _store = new();
    private readonly BackupService _backupService = new();
    private readonly PreferencesStore _preferencesStore = new();
    private readonly ExplorerTabLauncher _launcher = new();
    private readonly ListBox _projectList = new();
    private readonly ListView _folderList = new();
    private readonly Label _folderHeading = new();
    private readonly Button _launchButton = new();
    private readonly Button _moveFolderUpButton = new();
    private readonly Button _moveFolderDownButton = new();
    private readonly Button _moveProjectUpButton = new();
    private readonly Button _moveProjectDownButton = new();
    private LauncherSettings _settings;
    private bool _updatingSelection;
    private int _folderStatusVersion;
    private CancellationTokenSource? _statusCheckCancellation;
    private int _projectDragStartIndex = -1;
    private Point _projectDragStartPoint;
    private int _projectDropIndex = -1;
    private int _folderDropIndex = -1;

    public MainForm()
    {
        Text = "起動ランチャー";
        MinimumSize = new Size(760, 480);
        Size = new Size(940, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9F);
        BackColor = Color.FromArgb(248, 249, 250);

        try
        {
            _settings = _store.Load();
        }
        catch (Exception exception)
        {
            _settings = new LauncherSettings();
            Shown += (_, _) => MessageBox.Show(
                this,
                $"保存済み設定を読み込めませんでした。設定ファイルを確認してください。\n\n{exception.Message}",
                "設定の読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        BuildLayout();
        RefreshProjects();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "プロジェクトランチャー",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 19F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 43, 54),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var backupButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 13, 0, 0)
        };
        backupButtons.Controls.Add(CreateButton("読み込み", (_, _) => ImportSettings()));
        backupButtons.Controls.Add(CreateButton("書き出し", (_, _) => ExportSettings()));
        header.Controls.Add(backupButtons, 1, 0);
        root.Controls.Add(header, 0, 0);

        var content = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Size = new Size(700, 400),
            BackColor = Color.FromArgb(222, 227, 231),
            SplitterDistance = 280,
            Panel1MinSize = 210,
            Panel2MinSize = 320
        };
        root.Controls.Add(content, 0, 1);

        var projectPanel = CreatePanel();
        content.Panel1.Controls.Add(projectPanel);
        var projectLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        projectLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        projectPanel.Controls.Add(projectLayout);
        projectLayout.Controls.Add(new Label
        {
            Text = "プロジェクト",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(64, 78, 88),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _projectList.Dock = DockStyle.Fill;
        _projectList.BorderStyle = BorderStyle.None;
        _projectList.IntegralHeight = false;
        _projectList.Font = new Font("Yu Gothic UI", 10F);
        _projectList.SelectedIndexChanged += (_, _) =>
        {
            UpdateProjectMoveButtons();
            RefreshFolders();
        };
        _projectList.DoubleClick += (_, _) => RenameProject();
        _projectList.KeyDown += OnProjectListKeyDown;
        _projectList.ContextMenuStrip = CreateProjectContextMenu();
        _projectList.AllowDrop = true;
        _projectList.MouseDown += OnProjectListMouseDown;
        _projectList.MouseMove += OnProjectListMouseMove;
        _projectList.DragOver += OnProjectListDragOver;
        _projectList.DragLeave += (_, _) => SetProjectDropIndex(-1);
        _projectList.DragDrop += OnProjectListDragDrop;
        projectLayout.Controls.Add(_projectList, 0, 1);
        projectLayout.Controls.Add(CreateProjectButtons(), 0, 2);

        var folderPanel = CreatePanel();
        folderPanel.Padding = new Padding(18);
        content.Panel2.Controls.Add(folderPanel);
        var folderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        folderPanel.Controls.Add(folderLayout);
        _folderHeading.Dock = DockStyle.Fill;
        _folderHeading.Font = new Font("Yu Gothic UI", 11F, FontStyle.Bold);
        _folderHeading.ForeColor = Color.FromArgb(64, 78, 88);
        _folderHeading.TextAlign = ContentAlignment.MiddleLeft;
        folderLayout.Controls.Add(_folderHeading, 0, 0);

        _folderList.Dock = DockStyle.Fill;
        _folderList.View = View.Details;
        _folderList.FullRowSelect = true;
        _folderList.MultiSelect = true;
        _folderList.HideSelection = false;
        _folderList.BorderStyle = BorderStyle.FixedSingle;
        _folderList.SelectedIndexChanged += (_, _) => UpdateFolderMoveButtons();
        _folderList.ContextMenuStrip = CreateFolderContextMenu();
        _folderList.KeyDown += OnFolderListKeyDown;
        _folderList.DoubleClick += (_, _) => OpenSelectedItems();
        _folderList.AllowDrop = true;
        _folderList.DragEnter += OnFolderListDragOver;
        _folderList.DragOver += OnFolderListDragOver;
        _folderList.DragLeave += (_, _) => SetFolderDropIndex(-1);
        _folderList.DragDrop += OnFolderListDragDrop;
        _folderList.ItemDrag += OnFolderListItemDrag;
        _folderList.Columns.Add("フォルダー / ファイル / URL", 360);
        _folderList.Columns.Add("種類", 70);
        _folderList.Columns.Add("状態", 170);
        folderLayout.Controls.Add(_folderList, 0, 1);

        var folderButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0)
        };
        folderButtons.Controls.Add(CreateAddItemButton());
        folderButtons.Controls.Add(CreateButton("選択を削除", (_, _) => RemoveFolders()));
        ApplyButtonStyle(_moveFolderUpButton);
        ApplyButtonStyle(_moveFolderDownButton);
        _moveFolderUpButton.Text = "上へ";
        _moveFolderUpButton.Click += (_, _) => MoveSelectedFolder(-1);
        folderButtons.Controls.Add(_moveFolderUpButton);
        _moveFolderDownButton.Text = "下へ";
        _moveFolderDownButton.Click += (_, _) => MoveSelectedFolder(1);
        folderButtons.Controls.Add(_moveFolderDownButton);
        folderLayout.Controls.Add(folderButtons, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        _launchButton.Text = "▶  一括起動";
        _launchButton.Width = 150;
        _launchButton.Height = 40;
        _launchButton.FlatStyle = FlatStyle.Flat;
        _launchButton.FlatAppearance.BorderSize = 0;
        _launchButton.BackColor = Color.FromArgb(28, 112, 93);
        _launchButton.ForeColor = Color.White;
        _launchButton.Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold);
        _launchButton.Click += async (_, _) => await LaunchSelectedAsync();
        footer.Controls.Add(_launchButton);
        root.Controls.Add(footer, 0, 2);
    }

    private ContextMenuStrip CreateFolderContextMenu()
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem("パス / URL をコピー", null, (_, _) => CopySelectedFolderPaths()) { ShortcutKeyDisplayString = "Ctrl+C" };
        var paste = new ToolStripMenuItem("貼り付けて追加", null, (_, _) => PasteItems()) { ShortcutKeyDisplayString = "Ctrl+V" };
        var open = new ToolStripMenuItem("開く", null, (_, _) => OpenSelectedItems()) { ShortcutKeyDisplayString = "Enter" };
        var recheck = new ToolStripMenuItem("状態を再確認", null, (_, _) => RefreshFolders()) { ShortcutKeyDisplayString = "F5" };
        var delete = new ToolStripMenuItem("削除", null, (_, _) => RemoveFolders()) { ShortcutKeyDisplayString = "Delete" };
        menu.Items.AddRange(new ToolStripItem[] { copy, paste, open, recheck, new ToolStripSeparator(), delete });

        // 項目の無い場所で右クリックしたときは、選択が必要な操作だけ無効にする
        menu.Opening += (_, e) =>
        {
            var hasSelection = _folderList.SelectedItems.Count > 0;
            copy.Enabled = open.Enabled = delete.Enabled = hasSelection;
            e.Cancel = SelectedProject is null;
        };
        return menu;
    }

    private void OnFolderListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedFolderPaths();
        }
        else if (e.Control && e.KeyCode == Keys.V)
        {
            PasteItems();
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            foreach (ListViewItem item in _folderList.Items)
            {
                item.Selected = true;
            }
        }
        else if (e.KeyCode == Keys.Enter)
        {
            OpenSelectedItems();
        }
        else if (e.KeyCode == Keys.Delete)
        {
            RemoveFolders();
        }
        else if (e.KeyCode == Keys.F5)
        {
            RefreshFolders();
        }
        else if (e.Control && e.KeyCode is Keys.Up or Keys.Down)
        {
            MoveSelectedFolder(e.KeyCode == Keys.Up ? -1 : 1);
        }
        else
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private Button CreateAddItemButton()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("フォルダー…", null, (_, _) => AddFolders());
        menu.Items.Add("ファイル…", null, (_, _) => AddFiles());
        menu.Items.Add("パス / URL を入力…", null, (_, _) => AddByInput());

        Button? button = null;
        button = CreateButton("追加 ▾", (_, _) => menu.Show(button!, new Point(0, button!.Height)));
        return button;
    }

    private ContextMenuStrip CreateProjectContextMenu()
    {
        var menu = new ContextMenuStrip();
        var rename = new ToolStripMenuItem("名前変更", null, (_, _) => RenameProject()) { ShortcutKeyDisplayString = "F2" };
        var up = new ToolStripMenuItem("上へ", null, (_, _) => MoveSelectedProject(-1)) { ShortcutKeyDisplayString = "Ctrl+↑" };
        var down = new ToolStripMenuItem("下へ", null, (_, _) => MoveSelectedProject(1)) { ShortcutKeyDisplayString = "Ctrl+↓" };
        var delete = new ToolStripMenuItem("削除", null, (_, _) => DeleteProject()) { ShortcutKeyDisplayString = "Delete" };
        menu.Items.AddRange(new ToolStripItem[] { rename, up, down, new ToolStripSeparator(), delete });
        menu.Opening += (_, e) =>
        {
            up.Enabled = _moveProjectUpButton.Enabled;
            down.Enabled = _moveProjectDownButton.Enabled;
            e.Cancel = SelectedProject is null;
        };
        return menu;
    }

    private void OnProjectListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2)
        {
            RenameProject();
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteProject();
        }
        else if (e.Control && e.KeyCode is Keys.Up or Keys.Down)
        {
            MoveSelectedProject(e.KeyCode == Keys.Up ? -1 : 1);
        }
        else
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void OnProjectListMouseDown(object? sender, MouseEventArgs e)
    {
        var index = _projectList.IndexFromPoint(e.Location);
        // 右クリックでもその行を選択してから右クリックメニューを出す
        if (e.Button == MouseButtons.Right && index != ListBox.NoMatches)
        {
            _projectList.SelectedIndex = index;
        }

        _projectDragStartIndex = e.Button == MouseButtons.Left ? index : -1;
        _projectDragStartPoint = e.Location;
    }

    private void OnProjectListMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _projectDragStartIndex < 0)
        {
            return;
        }

        // クリックのわずかなブレではドラッグを始めない
        var dragSize = SystemInformation.DragSize;
        var dragArea = new Rectangle(
            _projectDragStartPoint.X - dragSize.Width / 2,
            _projectDragStartPoint.Y - dragSize.Height / 2,
            dragSize.Width,
            dragSize.Height);
        if (dragArea.Contains(e.Location))
        {
            return;
        }

        var index = _projectDragStartIndex;
        _projectDragStartIndex = -1;
        _projectList.DoDragDrop(new DataObject(ProjectDragFormat, index.ToString()), DragDropEffects.Move);
        SetProjectDropIndex(-1);
    }

    private void OnProjectListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(ProjectDragFormat) != true)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = DragDropEffects.Move;
        SetProjectDropIndex(GetProjectInsertionIndex(_projectList.PointToClient(new Point(e.X, e.Y))));
    }

    private void OnProjectListDragDrop(object? sender, DragEventArgs e)
    {
        SetProjectDropIndex(-1);
        if (!int.TryParse(e.Data?.GetData(ProjectDragFormat) as string, out var fromIndex)
            || fromIndex < 0
            || fromIndex >= _settings.Projects.Count)
        {
            return;
        }

        var project = _settings.Projects[fromIndex];
        var insertIndex = GetProjectInsertionIndex(_projectList.PointToClient(new Point(e.X, e.Y)));
        ListReorder.Move(_settings.Projects, [fromIndex], insertIndex);
        SaveAndRefresh(project.Id);
    }

    /// <summary>マウス位置から、ドロップしたときの挿入位置（0〜項目数）を求める。行の下半分なら次の行の前に入れる。</summary>
    private int GetProjectInsertionIndex(Point point)
    {
        var count = _projectList.Items.Count;
        if (count == 0)
        {
            return 0;
        }

        var index = _projectList.IndexFromPoint(point);
        if (index == ListBox.NoMatches)
        {
            return point.Y < _projectList.GetItemRectangle(0).Top ? 0 : count;
        }

        var bounds = _projectList.GetItemRectangle(index);
        return point.Y >= bounds.Top + bounds.Height / 2 ? index + 1 : index;
    }

    private void SetProjectDropIndex(int index)
    {
        if (index == _projectDropIndex)
        {
            return;
        }

        _projectDropIndex = index;
        var count = _projectList.Items.Count;
        int? y = index < 0 || count == 0
            ? null
            : index < count ? _projectList.GetItemRectangle(index).Top : _projectList.GetItemRectangle(count - 1).Bottom - 1;
        DrawDropIndicator(_projectList, y);
    }

    private void OnFolderListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Button != MouseButtons.Left || SelectedProject is null || _folderList.SelectedIndices.Count == 0)
        {
            return;
        }

        var indices = string.Join(",", _folderList.SelectedIndices.Cast<int>());
        _folderList.DoDragDrop(new DataObject(ItemsDragFormat, indices), DragDropEffects.Move);
        SetFolderDropIndex(-1);
    }

    private void OnFolderListDragOver(object? sender, DragEventArgs e)
    {
        if (SelectedProject is null || e.Data is null)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        // 一覧内での並び替え
        if (e.Data.GetDataPresent(ItemsDragFormat))
        {
            e.Effect = DragDropEffects.Move;
            SetFolderDropIndex(GetFolderInsertionIndex(_folderList.PointToClient(new Point(e.X, e.Y))));
            return;
        }

        // エクスプローラーやブラウザーからの追加
        var canAdd = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effect = canAdd ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnFolderListDragDrop(object? sender, DragEventArgs e)
    {
        SetFolderDropIndex(-1);
        if (e.Data?.GetData(ItemsDragFormat) is string indicesText)
        {
            MoveDraggedFolders(indicesText, GetFolderInsertionIndex(_folderList.PointToClient(new Point(e.X, e.Y))));
            return;
        }

        // エクスプローラーからはファイル・フォルダー、ブラウザー等からはURLやパスの文字列がドロップされる
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddItems(paths);
        }
        else if (e.Data?.GetData(DataFormats.UnicodeText) is string text)
        {
            var items = ParseItems(text);
            if (items.Length == 0)
            {
                ShowError("ドロップされた内容はパスやURLではありません。");
                return;
            }

            AddItems(items);
        }
    }

    private void MoveDraggedFolders(string indicesText, int insertIndex)
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        var indices = indicesText.Split(',')
            .Select(text => int.TryParse(text, out var index) ? index : -1)
            .Where(index => index >= 0 && index < project.Folders.Count)
            .ToArray();
        if (indices.Length == 0)
        {
            return;
        }

        var movedPaths = indices.Select(index => project.Folders[index]).ToArray();
        ListReorder.Move(project.Folders, indices, insertIndex);
        SaveAndRefresh(project.Id, movedPaths);
    }

    private int GetFolderInsertionIndex(Point point)
    {
        var count = _folderList.Items.Count;
        if (count == 0)
        {
            return 0;
        }

        var item = _folderList.GetItemAt(Math.Max(point.X, 1), point.Y);
        if (item is null)
        {
            // 最後の行より下なら末尾、それ以外（見出し付近など）は表示中の先頭行の前
            return point.Y >= _folderList.Items[count - 1].Bounds.Bottom ? count : _folderList.TopItem?.Index ?? 0;
        }

        var bounds = item.Bounds;
        return point.Y >= bounds.Top + bounds.Height / 2 ? item.Index + 1 : item.Index;
    }

    private void SetFolderDropIndex(int index)
    {
        if (index == _folderDropIndex)
        {
            return;
        }

        _folderDropIndex = index;
        var count = _folderList.Items.Count;
        int? y = index < 0 || count == 0
            ? null
            : index < count ? _folderList.Items[index].Bounds.Top : _folderList.Items[count - 1].Bounds.Bottom - 1;
        DrawDropIndicator(_folderList, y);
    }

    /// <summary>ドロップ先を示す線を描く（y が null なら消す）。一時的な描画なので、再描画で消える。</summary>
    private static void DrawDropIndicator(Control control, int? y)
    {
        control.Invalidate();
        control.Update();
        if (y is null)
        {
            return;
        }

        var lineY = Math.Clamp(y.Value, 1, Math.Max(1, control.ClientSize.Height - 2));
        using var graphics = control.CreateGraphics();
        using var pen = new Pen(DropIndicatorColor, 2);
        graphics.DrawLine(pen, 0, lineY, control.ClientSize.Width, lineY);
    }

    private TableLayoutPanel CreateProjectButtons()
    {
        // 左ペインは幅が狭いため、ボタンを3列×2段の格子に並べて幅に合わせて伸縮させる
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0, 5, 0, 0)
        };
        for (var column = 0; column < 3; column++)
        {
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        }

        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _moveProjectUpButton.Text = "上へ";
        _moveProjectUpButton.Click += (_, _) => MoveSelectedProject(-1);
        ApplyButtonStyle(_moveProjectUpButton);
        _moveProjectDownButton.Text = "下へ";
        _moveProjectDownButton.Click += (_, _) => MoveSelectedProject(1);
        ApplyButtonStyle(_moveProjectDownButton);

        Button[] cells =
        [
            CreateButton("追加", (_, _) => AddProject()),
            CreateButton("名前変更", (_, _) => RenameProject()),
            CreateButton("削除", (_, _) => DeleteProject()),
            _moveProjectUpButton,
            _moveProjectDownButton
        ];
        for (var index = 0; index < cells.Length; index++)
        {
            var button = cells[index];
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Padding = Padding.Empty;
            button.Margin = new Padding(0, 0, index % 3 == 2 ? 0 : 6, 6);
            buttons.Controls.Add(button, index % 3, index / 3);
        }

        return buttons;
    }

    private static Panel CreatePanel() => new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(14),
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text };
        ApplyButtonStyle(button);
        button.Click += onClick;
        return button;
    }

    private static void ApplyButtonStyle(Button button)
    {
        button.AutoSize = true;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(242, 245, 246);
        button.ForeColor = Color.FromArgb(40, 57, 68);
        button.Margin = new Padding(0, 0, 7, 0);
        button.Padding = new Padding(7, 0, 7, 0);
        button.FlatAppearance.BorderColor = Color.FromArgb(220, 226, 229);
    }

    private LauncherProject? SelectedProject => _projectList.SelectedItem as LauncherProject;

    private void RefreshProjects(Guid? selectedProjectId = null)
    {
        _updatingSelection = true;
        _projectList.BeginUpdate();
        _projectList.Items.Clear();
        foreach (var project in _settings.Projects)
        {
            _projectList.Items.Add(project);
        }

        var selectedIndex = selectedProjectId is null
            ? Math.Min(_projectList.Items.Count - 1, 0)
            : _settings.Projects.FindIndex(project => project.Id == selectedProjectId);
        if (selectedIndex >= 0)
        {
            _projectList.SelectedIndex = selectedIndex;
        }

        _projectList.EndUpdate();
        _updatingSelection = false;
        UpdateProjectMoveButtons();
        RefreshFolders();
    }

    private void RefreshFolders()
    {
        if (_updatingSelection)
        {
            return;
        }

        var project = SelectedProject;
        var version = ++_folderStatusVersion;
        _statusCheckCancellation?.Cancel();
        _statusCheckCancellation?.Dispose();
        _statusCheckCancellation = new CancellationTokenSource();
        _folderList.BeginUpdate();
        _folderList.Items.Clear();
        if (project is not null)
        {
            foreach (var folder in project.Folders)
            {
                // 有効かどうかの確認は非同期で行い、結果が出るまでは「確認中」と表示する
                var item = new ListViewItem(folder) { ForeColor = PendingColor, UseItemStyleForSubItems = false };
                item.SubItems.Add(LaunchTarget.IsUrl(folder) ? LaunchTarget.DisplayName(LaunchTargetKind.Url) : string.Empty, PendingColor, item.BackColor, item.Font);
                item.SubItems.Add("確認中…", PendingColor, item.BackColor, item.Font);
                _folderList.Items.Add(item);
            }
        }

        _folderList.EndUpdate();
        UpdateFolderHeading();
        _launchButton.Enabled = project is { Folders.Count: > 0 };
        UpdateFolderMoveButtons();
        if (project is { Folders.Count: > 0 })
        {
            _ = UpdateFolderStatusesAsync(version, project.Folders.ToArray(), _statusCheckCancellation.Token);
        }
    }

    private async Task UpdateFolderStatusesAsync(int version, string[] folders, CancellationToken cancellationToken)
    {
        try
        {
            // 項目ごとに並行して確認し、結果が出たものから順に表示する。
            // フォルダー・ファイルは存在確認（スリープ中のドライブや応答の遅いファイルサーバーがあるためUIスレッド外で行う）、
            // URLは実際にアクセスして確認する
            await Task.WhenAll(folders.Select(async (folder, index) =>
            {
                var status = LaunchTarget.IsUrl(folder)
                    ? await TargetStatusChecker.CheckUrlAsync(folder, cancellationToken)
                    : await Task.Run(() => TargetStatusChecker.CheckPath(folder), cancellationToken);
                if (version == _folderStatusVersion && !IsDisposed)
                {
                    ApplyFolderStatus(index, folder, status);
                }
            }));
        }
        catch (OperationCanceledException)
        {
            // 一覧が更新されたため、古い確認結果は破棄する
        }
    }

    private void ApplyFolderStatus(int index, string folder, TargetStatus status)
    {
        if (index >= _folderList.Items.Count)
        {
            return;
        }

        var item = _folderList.Items[index];
        if (!string.Equals(item.Text, folder, StringComparison.Ordinal))
        {
            return;
        }

        item.Tag = status;
        item.ForeColor = status.IsValid ? NormalColor : InvalidColor;
        item.SubItems[1].Text = LaunchTarget.DisplayName(status.Kind);
        item.SubItems[1].ForeColor = item.ForeColor;
        item.SubItems[2].Text = $"{(status.IsValid ? "○" : "×")} {status.Text}";
        item.SubItems[2].ForeColor = status.IsValid ? ValidColor : InvalidColor;
        UpdateFolderHeading();
    }

    private void UpdateFolderHeading()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _folderHeading.Text = "起動する項目";
            return;
        }

        var invalidCount = _folderList.Items.Cast<ListViewItem>().Count(item => item.Tag is TargetStatus { IsValid: false });
        _folderHeading.Text = invalidCount > 0
            ? $"{project.Name} の起動項目（無効 {invalidCount} 件）"
            : $"{project.Name} の起動項目";
    }

    private void UpdateProjectMoveButtons()
    {
        var selectedIndex = _projectList.SelectedIndex;
        _moveProjectUpButton.Enabled = selectedIndex > 0;
        _moveProjectDownButton.Enabled = selectedIndex >= 0 && selectedIndex < _settings.Projects.Count - 1;
    }

    private void UpdateFolderMoveButtons()
    {
        var project = SelectedProject;
        var canMove = project is not null && _folderList.SelectedIndices.Count == 1;
        var selectedIndex = canMove ? _folderList.SelectedIndices[0] : -1;
        _moveFolderUpButton.Enabled = canMove && selectedIndex > 0;
        _moveFolderDownButton.Enabled = canMove && selectedIndex < project!.Folders.Count - 1;
    }

    private void AddProject()
    {
        var name = PromptForName("新しいプロジェクト", "プロジェクト名");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (_settings.Projects.Any(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("同じ名前のプロジェクトがすでにあります。");
            return;
        }

        var project = new LauncherProject { Name = name };
        _settings.Projects.Add(project);
        SaveAndRefresh(project.Id);
    }

    private void RenameProject()
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        var name = PromptForName("プロジェクト名の変更", "プロジェクト名", project.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (_settings.Projects.Any(other => other.Id != project.Id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("同じ名前のプロジェクトがすでにあります。");
            return;
        }

        project.Name = name;
        SaveAndRefresh(project.Id);
    }

    private void DeleteProject()
    {
        var project = SelectedProject;
        if (project is null || MessageBox.Show(this, $"「{project.Name}」を削除しますか？", "プロジェクトの削除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _settings.Projects.Remove(project);
        SaveAndRefresh();
    }

    private void MoveSelectedProject(int direction)
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        var currentIndex = _settings.Projects.IndexOf(project);
        var targetIndex = currentIndex + direction;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= _settings.Projects.Count)
        {
            return;
        }

        (_settings.Projects[currentIndex], _settings.Projects[targetIndex]) = (_settings.Projects[targetIndex], _settings.Projects[currentIndex]);
        SaveAndRefresh(project.Id);
    }

    private void AddFolders()
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog { Description = "追加するフォルダーを選択してください。", Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AddItems(dialog.SelectedPaths.Length > 0 ? dialog.SelectedPaths : [dialog.SelectedPath]);
    }

    private void AddFiles()
    {
        if (SelectedProject is null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "追加するファイルを選択してください。",
            Filter = "すべてのファイル (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddItems(dialog.FileNames);
        }
    }

    private void AddByInput()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var input = PromptForName("パス / URL を入力", @"パスまたはURL（例: \\server\share\資料、C:\work\見積.xlsx、https://example.com）");
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var item = LaunchTarget.NormalizeInput(input);
        if (item is null)
        {
            ShowError("パスまたはURLの形式が正しくありません。");
            return;
        }

        AddItems([item]);
    }

    private void PasteItems()
    {
        if (SelectedProject is null)
        {
            return;
        }

        try
        {
            // エクスプローラーでコピーしたファイル・フォルダー、またはパス・URLの文字列（複数行可）を追加する
            if (Clipboard.ContainsFileDropList())
            {
                AddItems(Clipboard.GetFileDropList().Cast<string>());
                return;
            }

            var items = Clipboard.ContainsText() ? ParseItems(Clipboard.GetText()) : Array.Empty<string>();
            if (items.Length == 0)
            {
                ShowError("クリップボードにパスまたはURLがありません。");
                return;
            }

            AddItems(items);
        }
        catch (Exception exception)
        {
            ShowError($"クリップボードを読み取れませんでした。\n{exception.Message}");
        }
    }

    /// <summary>複数行の文字列からパス・URLを取り出す（パスやURLとして解釈できない行は無視する）。</summary>
    private static string[] ParseItems(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => LaunchTarget.NormalizeInput(line, completeUrlScheme: false))
            .OfType<string>()
            .ToArray();

    private void AddItems(IEnumerable<string> items)
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()))
        {
            if (!project.Folders.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                project.Folders.Add(item);
            }
        }

        SaveAndRefresh(project.Id);
    }

    private void RemoveFolders()
    {
        var project = SelectedProject;
        if (project is null || _folderList.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedPaths = _folderList.SelectedItems.Cast<ListViewItem>().Select(item => item.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        project.Folders.RemoveAll(folder => selectedPaths.Contains(folder));
        SaveAndRefresh(project.Id);
    }

    private string[] SelectedFolderPaths() =>
        _folderList.SelectedItems.Cast<ListViewItem>().Select(item => item.Text).ToArray();

    private void CopySelectedFolderPaths()
    {
        var paths = SelectedFolderPaths();
        if (paths.Length == 0)
        {
            return;
        }

        try
        {
            // 複数選択時は1行に1パスずつ並べてコピーする
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }
        catch (Exception exception)
        {
            ShowError($"クリップボードにコピーできませんでした。\n{exception.Message}");
        }
    }

    private void OpenSelectedItems()
    {
        var paths = SelectedFolderPaths();
        if (paths.Length == 0)
        {
            return;
        }

        var errors = OpenItems(paths.Select(path => (path, LaunchTarget.Classify(path))).ToArray());
        if (errors.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>フォルダーは個別のウィンドウ、ファイル・URLは既定のアプリで開き、開けなかった項目の説明を返す。</summary>
    private static List<string> OpenItems(IEnumerable<(string Path, LaunchTargetKind Kind)> targets)
    {
        var errors = new List<string>();
        foreach (var (path, kind) in targets)
        {
            if (kind == LaunchTargetKind.Missing)
            {
                errors.Add($"見つかりません: {path}");
                continue;
            }

            try
            {
                LaunchTarget.Open(path, kind);
            }
            catch (Exception exception)
            {
                errors.Add($"開けませんでした: {path}（{exception.Message}）");
            }
        }

        return errors;
    }

    private void MoveSelectedFolder(int direction)
    {
        var project = SelectedProject;
        if (project is null || _folderList.SelectedIndices.Count != 1)
        {
            return;
        }

        var currentIndex = _folderList.SelectedIndices[0];
        var targetIndex = currentIndex + direction;
        if (targetIndex < 0 || targetIndex >= project.Folders.Count)
        {
            return;
        }

        (project.Folders[currentIndex], project.Folders[targetIndex]) = (project.Folders[targetIndex], project.Folders[currentIndex]);
        SaveAndRefresh(project.Id, [project.Folders[targetIndex]]);
    }

    private async Task LaunchSelectedAsync()
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        _launchButton.Enabled = false;
        try
        {
            // 存在確認はスリープ中のドライブやファイルサーバーで時間がかかることがあるため、UIスレッド外で並行して行う
            var items = project.Folders.ToArray();
            var targets = await Task.Run(() => items.AsParallel().AsOrdered().Select(item => (Path: item, Kind: LaunchTarget.Classify(item))).ToArray());
            var folders = targets.Where(target => target.Kind == LaunchTargetKind.Folder).Select(target => target.Path).ToArray();
            var others = targets.Where(target => target.Kind is LaunchTargetKind.File or LaunchTargetKind.Url).ToArray();
            if (folders.Length == 0 && others.Length == 0)
            {
                MessageBox.Show(this, "起動できる項目が見つかりません。パスやURLを確認してください。", "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // フォルダーをエクスプローラーのタブで開いてから、ファイルとURLを順に開く（見つからない項目はスキップ）
            if (folders.Length > 0)
            {
                var result = await _launcher.OpenFoldersAsync(folders, CancellationToken.None);
                if (!result.Success)
                {
                    var remaining = (result.RemainingFolders ?? folders).ToArray();
                    if (remaining.Length == 0)
                    {
                        MessageBox.Show(this, result.Message, "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        var prompt = result.Message + $"\n\n未起動のフォルダー {remaining.Length} 件を個別のウィンドウで開きますか？";
                        if (MessageBox.Show(this, prompt, "一括起動", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        {
                            ExplorerTabLauncher.OpenIndividually(remaining);
                        }
                    }
                }
            }

            var errors = OpenItems(others);
            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _launchButton.Enabled = SelectedProject is { Folders.Count: > 0 };
        }
    }

    private void ExportSettings()
    {
        const string defaultFileName = "kidou-rancher-backup.json";
        var preferences = _preferencesStore.Load();
        using var dialog = new SaveFileDialog
        {
            Title = "設定を書き出す",
            Filter = "JSONファイル (*.json)|*.json",
            FileName = defaultFileName,
            DefaultExt = "json"
        };

        // 前回の書き出し先を初期値にする（フォルダーが無くなっていれば既定に戻す）
        var lastDirectory = Path.GetDirectoryName(preferences.LastExportPath);
        if (!string.IsNullOrEmpty(lastDirectory) && Directory.Exists(lastDirectory))
        {
            dialog.InitialDirectory = lastDirectory;
            dialog.FileName = Path.GetFileName(preferences.LastExportPath) is { Length: > 0 } lastFileName ? lastFileName : defaultFileName;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _backupService.Export(_settings, dialog.FileName);
            preferences.LastExportPath = dialog.FileName;
            _preferencesStore.Save(preferences);
        }
        catch (Exception exception)
        {
            ShowError($"設定を書き出せませんでした。\n{exception.Message}");
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog { Title = "設定を読み込む", Filter = "JSONファイル (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var importedSettings = _backupService.Import(dialog.FileName);
            if (MessageBox.Show(this, $"プロジェクト {importedSettings.Projects.Count} 件で現在の設定を置き換えますか？", "設定の読み込み", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            _store.Save(importedSettings);
            _settings = importedSettings;
            RefreshProjects();
        }
        catch (Exception exception)
        {
            ShowError($"設定を読み込めませんでした。\n{exception.Message}");
        }
    }

    private void SaveAndRefresh(Guid? selectedProjectId = null, IReadOnlyCollection<string>? selectedFolderPaths = null)
    {
        try
        {
            _store.Save(_settings);
            RefreshProjects(selectedProjectId);
            if (selectedFolderPaths is { Count: > 0 })
            {
                var selectedItems = _folderList.Items.Cast<ListViewItem>()
                    .Where(item => selectedFolderPaths.Contains(item.Text, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.Selected = true;
                }

                selectedItems.FirstOrDefault()?.EnsureVisible();
            }
        }
        catch (Exception exception)
        {
            ShowError($"設定を保存できませんでした。\n{exception.Message}");
        }
    }

    private string? PromptForName(string title, string label, string? initialValue = null)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(480, 126),
            Font = Font
        };
        var prompt = new Label { Text = label, Left = 16, Top = 16, Width = 448 };
        var input = new TextBox { Left = 16, Top = 42, Width = 448, Text = initialValue ?? string.Empty };
        var save = new Button { Text = "決定", DialogResult = DialogResult.OK, Left = 304, Top = 82, Width = 76 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 388, Top = 82, Width = 76 };
        dialog.Controls.AddRange([prompt, input, save, cancel]);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
}