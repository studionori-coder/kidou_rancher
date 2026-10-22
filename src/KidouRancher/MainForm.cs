using KidouRancher.Models;
using KidouRancher.Services;
using CheckBoxState = System.Windows.Forms.VisualStyles.CheckBoxState;

namespace KidouRancher;

public sealed class MainForm : Form
{
    /// <summary>「起動する対象」の選択肢。並びはコンボボックスの表示順と一致させる。</summary>
    private enum LaunchScope
    {
        All,
        Folders,
        Files,
        Urls,
        Selected
    }

    private static readonly string[] LaunchScopeLabels = ["すべて", "フォルダーだけ", "ファイルだけ", "URLだけ", "選択した項目だけ"];

    private static readonly Color PendingColor = Color.FromArgb(120, 132, 140);
    private static readonly Color NormalColor = Color.FromArgb(44, 59, 68);
    private static readonly Color ValidColor = Color.FromArgb(28, 128, 76);
    private static readonly Color InvalidColor = Color.FromArgb(185, 73, 61);
    private static readonly Color DisabledColor = Color.FromArgb(160, 168, 174);
    private static readonly Color DropIndicatorColor = Color.FromArgb(28, 112, 93);

    // 起動項目一覧の列（表示順どおり）。各行のパス・URLは item.Name に持つ。
    // DisplayIndex で列の表示順を入れ替えると、オーナードロー時に先頭列のセル位置が正しく渡されないため使わない
    private const int TitleColumn = 0;
    private const int PathColumn = 1;
    private const int KindColumn = 2;
    private const int StatusColumn = 3;

    // 一覧内のドラッグ＆ドロップによる並び替えで使う、アプリ内専用のデータ形式
    private const string ProjectDragFormat = "KidouRancher.ProjectReorder";
    private const string ItemsDragFormat = "KidouRancher.ItemsReorder";

    private readonly ProjectStore _store = new();
    private readonly BackupService _backupService = new();
    private readonly PreferencesStore _preferencesStore = new();
    private readonly ExplorerTabLauncher _launcher = new();
    private readonly ShellIconCache _iconCache = new();
    private readonly ListBox _projectList = new();
    private readonly ListView _folderList = new();
    private readonly TextBox _folderDetail = new();
    private readonly Label _folderHeading = new();
    private readonly Button _launchButton = new();
    private readonly ComboBox _launchScopeBox = new();
    private readonly ToolTip _toolTip = new() { InitialDelay = 400, AutoPopDelay = 15000 };
    private readonly ContextMenuStrip _addItemMenu = new();
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
    private bool _populatingFolders;
    private bool _suppressNextItemCheck;

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
        RefreshProjects(_preferencesStore.Load().LastSelectedProjectId);
        // 起動直後から ↑↓ でプロジェクトを選び、Enter で一括起動できるようにする
        ActiveControl = _projectList;
        FormClosing += (_, _) => SaveSelectedProject();
        FormClosed += (_, _) => _iconCache.Dispose();
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
        backupButtons.Controls.Add(CreateButton("読み込み", (_, _) => ImportSettings(), "書き出した JSON ファイルから設定を元に戻す（今の設定は置き換わる）"));
        backupButtons.Controls.Add(CreateButton("書き出し", (_, _) => ExportSettings(), "すべてのプロジェクトを JSON ファイルに保存する（バックアップ）"));
        backupButtons.Controls.Add(CreateButton("一括置き換え", (_, _) => ReplacePaths(), "パス・URLの文字をまとめて置き換える。ファイルサーバーの移転などに（Ctrl+H）"));
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
        _toolTip.SetToolTip(_projectList, "↑↓ 選択　Enter 一括起動　→ 右の一覧へ　F2 名前変更　Delete 削除　Ctrl+↑↓ / ドラッグ 並び替え");
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
        var folderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
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
        _folderList.SelectedIndexChanged += (_, _) =>
        {
            UpdateFolderMoveButtons();
            UpdateFolderDetail();
        };
        // 長いパスやURLでも内容を確認できるよう、一覧では途中を「…」で省略して末尾（ファイル名など）を残し、
        // マウスを重ねるとツールチップで、選択すると一覧の下の欄で全体を表示する
        // チェックを外した項目は一括起動しない（Space でも切り替え）
        _folderList.CheckBoxes = true;
        _folderList.ItemCheck += OnFolderListItemCheck;
        _folderList.ItemChecked += OnFolderListItemChecked;
        // ListView はダブルクリックでもチェックが切り替わるが、ここではダブルクリック＝起動なので打ち消す
        _folderList.MouseDown += (_, e) => _suppressNextItemCheck = e.Clicks > 1;
        _folderList.MouseUp += (_, _) => _suppressNextItemCheck = false;
        _folderList.OwnerDraw = true;
        _folderList.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _folderList.DrawSubItem += OnFolderListDrawSubItem;
        _folderList.ShowItemToolTips = true;
        _folderList.ItemSelectionChanged += (_, e) => _folderList.Invalidate(e.Item?.Bounds ?? _folderList.ClientRectangle);
        _folderList.GotFocus += (_, _) => _folderList.Invalidate();
        _folderList.Enter += (_, _) =>
        {
            // Tab などキーボードで入ったときだけ先頭を選ぶ（マウスのクリックはその行の選択に任せる）
            if (MouseButtons == MouseButtons.None && _folderList.Items.Count > 0 && _folderList.SelectedIndices.Count == 0)
            {
                SelectFolderAt(0);
            }
        };
        _folderList.LostFocus += (_, _) => _folderList.Invalidate();
        _folderList.ContextMenuStrip = CreateFolderContextMenu();
        _folderList.KeyDown += OnFolderListKeyDown;
        _folderList.DoubleClick += (_, _) => OnFolderListDoubleClick();
        _folderList.AllowDrop = true;
        _folderList.DragEnter += OnFolderListDragOver;
        _folderList.DragOver += OnFolderListDragOver;
        _folderList.DragLeave += (_, _) => SetFolderDropIndex(-1);
        _folderList.DragDrop += OnFolderListDragDrop;
        _folderList.ItemDrag += OnFolderListItemDrag;
        _folderList.Columns.Add("表題", 140);
        _folderList.Columns.Add("フォルダー / ファイル / URL", 300);
        _folderList.Columns.Add("種類", 80);
        _folderList.Columns.Add("状態", 200);
        // パスの列を残りの幅いっぱいに広げ、状態の列が切れたり横スクロールが出たりしないようにする
        _folderList.ClientSizeChanged += (_, _) => FitFolderColumns();
        folderLayout.Controls.Add(_folderList, 0, 1);

        _folderDetail.Dock = DockStyle.Fill;
        _folderDetail.Multiline = true;
        _folderDetail.ReadOnly = true;
        _folderDetail.WordWrap = true;
        _folderDetail.ScrollBars = ScrollBars.None;
        _folderDetail.ClientSizeChanged += (_, _) => UpdateFolderDetailScrollBar();
        _folderDetail.BorderStyle = BorderStyle.None;
        _folderDetail.BackColor = Color.FromArgb(244, 246, 247);
        _folderDetail.ForeColor = Color.FromArgb(44, 59, 68);
        _folderDetail.Margin = new Padding(0, 6, 0, 0);
        _folderDetail.PlaceholderText = "項目を選択すると、ここにパスやURLの全体を表示します";
        folderLayout.Controls.Add(_folderDetail, 0, 2);

        var folderButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0)
        };
        folderButtons.Controls.Add(CreateAddItemButton());
        folderButtons.Controls.Add(CreateButton("選択を削除", (_, _) => RemoveFolders(), "選択した項目を一覧から外す（Delete）"));
        ApplyButtonStyle(_moveFolderUpButton);
        ApplyButtonStyle(_moveFolderDownButton);
        _toolTip.SetToolTip(_moveFolderUpButton, "選択した項目を上へ（Ctrl+↑）。ドラッグ＆ドロップでも並び替えられます");
        _toolTip.SetToolTip(_moveFolderDownButton, "選択した項目を下へ（Ctrl+↓）。ドラッグ＆ドロップでも並び替えられます");
        _moveFolderUpButton.Text = "上へ";
        _moveFolderUpButton.Click += (_, _) => MoveSelectedFolder(-1);
        folderButtons.Controls.Add(_moveFolderUpButton);
        _moveFolderDownButton.Text = "下へ";
        _moveFolderDownButton.Click += (_, _) => MoveSelectedFolder(1);
        folderButtons.Controls.Add(_moveFolderDownButton);
        folderLayout.Controls.Add(folderButtons, 0, 3);

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
        _launchButton.Click += async (_, _) => await LaunchProjectAsync();
        footer.Controls.Add(_launchButton);

        // 一括起動の対象（すべて／種類別／一覧で選択した項目だけ）
        _launchScopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _launchScopeBox.Width = 150;
        _launchScopeBox.Font = new Font("Yu Gothic UI", 10F);
        _launchScopeBox.Margin = new Padding(0, 8, 10, 0);
        _launchScopeBox.Items.AddRange(LaunchScopeLabels);
        _launchScopeBox.SelectedIndex = (int)LaunchScope.All;
        footer.Controls.Add(_launchScopeBox);
        footer.Controls.Add(new Label
        {
            Text = "起動する対象",
            AutoSize = true,
            Margin = new Padding(0, 12, 6, 0),
            ForeColor = Color.FromArgb(64, 78, 88)
        });
        footer.Controls.Add(new Label
        {
            Text = "F1: キー操作の一覧",
            AutoSize = true,
            Margin = new Padding(0, 12, 24, 0),
            ForeColor = PendingColor
        });
        _toolTip.SetToolTip(_launchButton, "一括起動（Ctrl+Enter）");
        _toolTip.SetToolTip(_launchScopeBox, "Alt+1〜Alt+5 で切り替え（すべて / フォルダー / ファイル / URL / 選択した項目）");
        root.Controls.Add(footer, 0, 2);
    }

    private ContextMenuStrip CreateFolderContextMenu()
    {
        var menu = new ContextMenuStrip();
        var copy = new ToolStripMenuItem("パス / URL をコピー", null, (_, _) => CopySelectedFolderPaths()) { ShortcutKeyDisplayString = "Ctrl+C" };
        var paste = new ToolStripMenuItem("貼り付けて追加", null, (_, _) => PasteItems()) { ShortcutKeyDisplayString = "Ctrl+V" };
        var open = new ToolStripMenuItem("選択した項目を起動", null, (_, _) => OpenSelectedItems()) { ShortcutKeyDisplayString = "Enter" };
        var recheck = new ToolStripMenuItem("状態を再確認", null, (_, _) => RefreshFolders()) { ShortcutKeyDisplayString = "F5" };
        var title = new ToolStripMenuItem("表題を変更", null, (_, _) => EditSelectedTitle()) { ShortcutKeyDisplayString = "F2" };
        var toggle = new ToolStripMenuItem("一括起動の対象にする／外す", null, (_, _) => ToggleSelectedEnabled()) { ShortcutKeyDisplayString = "Space" };
        var delete = new ToolStripMenuItem("削除", null, (_, _) => RemoveFolders()) { ShortcutKeyDisplayString = "Delete" };
        menu.Items.AddRange(new ToolStripItem[] { copy, paste, open, toggle, title, recheck, new ToolStripSeparator(), delete });

        // 項目の無い場所で右クリックしたときは、選択が必要な操作だけ無効にする
        menu.Opening += (_, e) =>
        {
            var hasSelection = _folderList.SelectedItems.Count > 0;
            copy.Enabled = open.Enabled = delete.Enabled = toggle.Enabled = hasSelection;
            title.Enabled = _folderList.SelectedItems.Count == 1;
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
        else if (e.Modifiers == Keys.None && e.KeyCode is Keys.Left or Keys.Escape)
        {
            _projectList.Focus();
        }
        else if (e.KeyCode == Keys.Insert)
        {
            ShowAddItemMenuInList();
        }
        else if (e.KeyCode == Keys.F2)
        {
            EditSelectedTitle();
        }
        else if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None)
        {
            ToggleSelectedEnabled();
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
        _addItemMenu.Items.Add(new ToolStripMenuItem("フォルダー(&D)…", null, (_, _) => AddFolders()) { ShortcutKeyDisplayString = "Ctrl+D" });
        _addItemMenu.Items.Add(new ToolStripMenuItem("ファイル(&O)…", null, (_, _) => AddFiles()) { ShortcutKeyDisplayString = "Ctrl+O" });
        _addItemMenu.Items.Add(new ToolStripMenuItem("パス / URL を入力(&L)…", null, (_, _) => AddByInput()) { ShortcutKeyDisplayString = "Ctrl+L" });

        Button? button = null;
        button = CreateButton("追加 ▾", (_, _) => _addItemMenu.Show(button!, new Point(0, button!.Height)));
        _toolTip.SetToolTip(button, "追加（一覧で Insert、または Ctrl+D / Ctrl+O / Ctrl+L）");
        return button;
    }

    /// <summary>キーボード操作用：選択中の項目の下（無ければ一覧の左上）に「追加」メニューを出す。</summary>
    private void ShowAddItemMenuInList()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var anchor = _folderList.FocusedItem?.Bounds ?? Rectangle.Empty;
        _addItemMenu.Show(_folderList, new Point(anchor.Left + 8, anchor.IsEmpty ? 24 : anchor.Bottom));
        if (_addItemMenu.Items.Count > 0)
        {
            _addItemMenu.Items[0].Select();
        }
    }

    /// <summary>
    /// 画面のどこにフォーカスがあっても使えるショートカット。
    /// 一覧ごとの操作（Enter・Delete・F2 など）は各一覧の KeyDown で扱う。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Enter:
                _ = LaunchProjectAsync();
                return true;
            case Keys.Control | Keys.N:
                AddProject();
                return true;
            case Keys.Control | Keys.D:
                AddFolders();
                return true;
            case Keys.Control | Keys.O:
                AddFiles();
                return true;
            case Keys.Control | Keys.L:
                AddByInput();
                return true;
            case Keys.F1:
                ShowKeyboardHelp();
                return true;
            case Keys.Control | Keys.H:
                ReplacePaths();
                return true;
            case Keys.F6:
            case Keys.Shift | Keys.F6:
                if (_folderList.ContainsFocus)
                {
                    _projectList.Focus();
                }
                else
                {
                    FocusFolderList();
                }

                return true;
        }

        // Ctrl+1〜9：N番目のプロジェクトへ移動、Alt+1〜5：起動する対象の切り替え
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;
        if (key is >= Keys.D1 and <= Keys.D9)
        {
            var number = key - Keys.D1;
            if (modifiers == Keys.Control && number < _projectList.Items.Count)
            {
                _projectList.SelectedIndex = number;
                _projectList.Focus();
                return true;
            }

            if (modifiers == Keys.Alt && number < _launchScopeBox.Items.Count)
            {
                _launchScopeBox.SelectedIndex = number;
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>右の一覧へフォーカスを移す。未選択なら先頭を選び、すぐに ↑↓ で動けるようにする。</summary>
    private void FocusFolderList()
    {
        _folderList.Focus();
        if (_folderList.Items.Count > 0 && _folderList.SelectedIndices.Count == 0)
        {
            SelectFolderAt(0);
        }
    }

    private void SelectFolderAt(int index)
    {
        if (_folderList.Items.Count == 0)
        {
            return;
        }

        var item = _folderList.Items[Math.Clamp(index, 0, _folderList.Items.Count - 1)];
        _folderList.SelectedItems.Clear();
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    private void ShowKeyboardHelp()
    {
        const string help = """
            【どこでも】
              Ctrl+Enter　　一括起動
              Alt+1〜5　　　起動する対象（すべて / フォルダー / ファイル / URL / 選択した項目）
              Ctrl+1〜9　　 N 番目のプロジェクトへ移動
              F6　　　　　　左右の一覧を切り替え
              Ctrl+N　　　　新しいプロジェクト
              Ctrl+H　　　　パス・URL の一括置き換え
              Ctrl+D / Ctrl+O / Ctrl+L　フォルダー / ファイル / パス・URL を追加
              F1　　　　　　この一覧

            【プロジェクト一覧（左）】
              ↑↓　　　　　選択　　　　Enter　一括起動　　→　右の一覧へ
              F2　　　　　　名前変更　　Delete　削除
              Ctrl+↑↓　　 並び替え

            【起動項目一覧（右）】
              ↑↓ / Shift+↑↓　選択 / 範囲選択　　Ctrl+A　すべて選択
              Enter　　　　 選択した項目を起動　　← / Esc　左の一覧へ
              Insert　　　　追加メニュー　　Ctrl+V　貼り付けて追加
              Ctrl+C　　　　パス・URL をコピー　　Delete　削除
              Ctrl+↑↓　　 並び替え　　F5　状態を再確認
              F2　　　　　　表題を変更（表題の列のダブルクリックでも可）
              Space　　　　 一括起動の対象にする／外す（チェックの切り替え）
            """;
        MessageBox.Show(this, help, "キー操作の一覧", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
        {
            _ = LaunchProjectAsync();
        }
        else if (e.KeyCode == Keys.Right && e.Modifiers == Keys.None)
        {
            FocusFolderList();
        }
        else if (e.KeyCode == Keys.F2)
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

        _toolTip.SetToolTip(_moveProjectUpButton, "選択したプロジェクトを上へ（Ctrl+↑）。ドラッグ＆ドロップでも並び替えられます");
        _toolTip.SetToolTip(_moveProjectDownButton, "選択したプロジェクトを下へ（Ctrl+↓）。ドラッグ＆ドロップでも並び替えられます");
        _moveProjectUpButton.Text = "上へ";
        _moveProjectUpButton.Click += (_, _) => MoveSelectedProject(-1);
        ApplyButtonStyle(_moveProjectUpButton);
        _moveProjectDownButton.Text = "下へ";
        _moveProjectDownButton.Click += (_, _) => MoveSelectedProject(1);
        ApplyButtonStyle(_moveProjectDownButton);

        Button[] cells =
        [
            CreateButton("追加", (_, _) => AddProject(), "新しいプロジェクトを作る（Ctrl+N）"),
            CreateButton("名前変更", (_, _) => RenameProject(), "プロジェクト名を変更する（F2 / ダブルクリック）"),
            CreateButton("削除", (_, _) => DeleteProject(), "プロジェクトを削除する（Delete。確認あり）"),
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

    /// <summary>ボタンを作る。toolTip を渡すと、マウスを重ねたときに説明とショートカットキーを表示する。</summary>
    private Button CreateButton(string text, EventHandler onClick, string? toolTip = null)
    {
        var button = new Button { Text = text };
        ApplyButtonStyle(button);
        button.Click += onClick;
        if (toolTip is not null)
        {
            _toolTip.SetToolTip(button, toolTip);
        }

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

        // 指定のプロジェクトが無ければ（削除済みなど）先頭を選択する
        var selectedIndex = selectedProjectId is null
            ? -1
            : _settings.Projects.FindIndex(project => project.Id == selectedProjectId);
        if (selectedIndex < 0)
        {
            selectedIndex = Math.Min(_projectList.Items.Count - 1, 0);
        }

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
        _populatingFolders = true;
        _folderList.BeginUpdate();
        _folderList.Items.Clear();
        if (project is not null)
        {
            foreach (var folder in project.Folders)
            {
                // 有効かどうかの確認は非同期で行い、結果が出るまでは「確認中」と表示する
                var item = new ListViewItem(project.GetTitle(folder)) { Name = folder, ForeColor = PendingColor, UseItemStyleForSubItems = false, Checked = project.IsEnabled(folder) };
                item.SubItems.Add(folder, PendingColor, item.BackColor, item.Font);
                item.SubItems.Add(LaunchTarget.IsUrl(folder) ? LaunchTarget.DisplayName(LaunchTargetKind.Url) : string.Empty, PendingColor, item.BackColor, item.Font);
                item.SubItems.Add("確認中…", PendingColor, item.BackColor, item.Font);
                item.ToolTipText = BuildItemToolTip(item, folder);
                _folderList.Items.Add(item);
            }
        }

        _folderList.EndUpdate();
        _populatingFolders = false;
        foreach (ListViewItem item in _folderList.Items)
        {
            ApplyRowColors(item);
        }

        UpdateFolderDetail();
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
        if (!string.Equals(item.Name, folder, StringComparison.Ordinal))
        {
            return;
        }

        item.Tag = status;
        item.SubItems[KindColumn].Text = LaunchTarget.DisplayName(status.Kind);
        item.SubItems[StatusColumn].Text = $"{(status.IsValid ? "○" : "×")} {status.Text}";
        item.ToolTipText = BuildItemToolTip(item, folder);
        ApplyRowColors(item);
        UpdateFolderHeading();
    }

    /// <summary>
    /// 行の文字色を決める：起動しない（チェックなし）行は全体を灰色、無効な行は赤、確認中は薄い灰色。
    /// UseItemStyleForSubItems が false のときは各セルの色を個別に設定し、行ごと再描画する。
    /// </summary>
    private void ApplyRowColors(ListViewItem item)
    {
        var status = item.Tag as TargetStatus;
        var textColor = !item.Checked ? DisabledColor
            : status is null ? PendingColor
            : status.IsValid ? NormalColor
            : InvalidColor;
        var statusColor = !item.Checked || status is null ? textColor : status.IsValid ? ValidColor : InvalidColor;
        item.ForeColor = textColor;
        item.SubItems[TitleColumn].ForeColor = textColor;
        item.SubItems[PathColumn].ForeColor = textColor;
        item.SubItems[KindColumn].ForeColor = textColor;
        item.SubItems[StatusColumn].ForeColor = statusColor;
        if (_folderList.Items.Contains(item))
        {
            _folderList.Invalidate(item.Bounds);
        }
    }

    private void OnFolderListItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressNextItemCheck && !_populatingFolders)
        {
            e.NewValue = e.CurrentValue;
        }
    }

    /// <summary>チェックの切り替えを保存する。一覧は作り直さず、その行の見た目と見出しだけ更新する。</summary>
    private void OnFolderListItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        var project = SelectedProject;
        if (_populatingFolders || project is null || e.Item is null || string.IsNullOrEmpty(e.Item.Name))
        {
            return;
        }

        if (project.IsEnabled(e.Item.Name) == e.Item.Checked)
        {
            return;
        }

        project.SetEnabled(e.Item.Name, e.Item.Checked);
        try
        {
            _store.Save(_settings);
        }
        catch (Exception exception)
        {
            ShowError($"設定を保存できませんでした。\n{exception.Message}");
        }

        e.Item.ToolTipText = BuildItemToolTip(e.Item, e.Item.Name);
        ApplyRowColors(e.Item);
        UpdateFolderHeading();
    }

    /// <summary>選択中の項目の「起動する／しない」をまとめて切り替える。1件でもオフがあればすべてオンにする。</summary>
    private void ToggleSelectedEnabled()
    {
        var items = _folderList.SelectedItems.Cast<ListViewItem>().ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var enable = items.Any(item => !item.Checked);
        foreach (var item in items)
        {
            item.Checked = enable;
        }
    }

    private void OnFolderListDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || e.SubItem is null)
        {
            e.DrawDefault = true;
            return;
        }

        // 選択行の色：一覧にフォーカスがあれば強調色、無ければ控えめな灰色（HideSelection = false と同じ見え方）
        var selected = e.Item.Selected;
        var focused = selected && _folderList.Focused;
        var backColor = focused ? SystemColors.Highlight : selected ? Color.FromArgb(222, 227, 231) : _folderList.BackColor;
        var foreColor = focused ? SystemColors.HighlightText : e.SubItem.ForeColor;
        using (var brush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        // パスは途中を省略して末尾（ファイル名など）を残す。PathEllipsis は「\」区切りでしか働かず、
        // URLでは先頭（https:// やドメイン）が消えてしまうため、URLとその他の列は末尾を省略する
        var usePathEllipsis = e.ColumnIndex == PathColumn && !LaunchTarget.IsUrl(e.SubItem.Text);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
            | (usePathEllipsis ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis);
        var textLeft = e.Bounds.Left + 4;
        if (e.ColumnIndex == 0 && _folderList.CheckBoxes)
        {
            // チェックボックスはオーナードローでは自分で描く（クリック判定は ListView 標準のまま、行の左端）
            var state = e.Item.Checked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
            var glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
            var glyphPoint = new Point(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - glyph.Height) / 2);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, glyphPoint, state);
            textLeft = glyphPoint.X + glyph.Width + 6;
        }

        if (e.ColumnIndex == PathColumn)
        {
            // 種類が一目で分かるよう、Windows と同じアイコンをパスの前に出す（起動しない行は淡く）
            var icon = _iconCache.Get(e.Item.Name, (e.Item.Tag as TargetStatus)?.Kind);
            if (icon is not null)
            {
                var iconPoint = new Point(textLeft, e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2);
                if (e.Item.Checked)
                {
                    e.Graphics.DrawImage(icon, iconPoint.X, iconPoint.Y, icon.Width, icon.Height);
                }
                else
                {
                    ControlPaint.DrawImageDisabled(e.Graphics, icon, iconPoint.X, iconPoint.Y, backColor);
                }

                textLeft += icon.Width + 6;
            }
        }

        var textBounds = Rectangle.FromLTRB(textLeft, e.Bounds.Top, e.Bounds.Right - 2, e.Bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font ?? _folderList.Font, textBounds, foreColor, flags);
    }

    /// <summary>選択中の項目のパス・URLを、一覧の下の欄に省略せずに表示する（複数選択時は1行に1件）。</summary>
    private void UpdateFolderDetail()
    {
        var paths = SelectedFolderPaths();
        _folderDetail.Text = string.Join(Environment.NewLine, paths);
        UpdateFolderDetailScrollBar();
    }

    /// <summary>欄に収まらないときだけスクロールバーを出す（空のときや短いときは表示しない）。</summary>
    private void UpdateFolderDetailScrollBar()
    {
        var width = Math.Max(1, _folderDetail.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        var needed = TextRenderer.MeasureText(
            _folderDetail.Text,
            _folderDetail.Font,
            new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        var scrollBars = _folderDetail.Text.Length > 0 && needed > _folderDetail.ClientSize.Height
            ? ScrollBars.Vertical
            : ScrollBars.None;
        if (_folderDetail.ScrollBars != scrollBars)
        {
            _folderDetail.ScrollBars = scrollBars;
        }
    }

    /// <summary>項目にマウスを重ねたときの説明。パス・URLの全体、表題、種類、状態と、使えるキーを出す。</summary>
    private static string BuildItemToolTip(ListViewItem item, string folder)
    {
        var lines = new List<string> { folder };
        if (item.SubItems[TitleColumn].Text is { Length: > 0 } title)
        {
            lines.Insert(0, $"表題: {title}");
        }

        if (item.Tag is TargetStatus)
        {
            lines.Add($"種類: {item.SubItems[KindColumn].Text}　状態: {item.SubItems[StatusColumn].Text}");
        }

        if (!item.Checked)
        {
            lines.Add("チェックなし: 一括起動では開きません（選択して起動すれば開きます）");
        }

        lines.Add("Enter / ダブルクリック: 起動　Space: 一括起動の対象にする／外す　F2: 表題を変更　Ctrl+C: コピー　Delete: 削除");
        return string.Join("\n", lines);
    }

    /// <summary>表題の列をダブルクリックしたら表題の変更、それ以外は起動。</summary>
    private void OnFolderListDoubleClick()
    {
        var hit = _folderList.HitTest(_folderList.PointToClient(MousePosition));
        if (hit.Location == ListViewHitTestLocations.StateImage)
        {
            // チェックボックスの素早い2回クリックは、チェックの操作として扱い起動しない
            return;
        }

        if (hit.Item is not null && hit.SubItem is not null && hit.Item.SubItems.IndexOf(hit.SubItem) == TitleColumn)
        {
            EditSelectedTitle();
            return;
        }

        OpenSelectedItems();
    }

    /// <summary>選択中の項目（1件）の表題を変更する。空欄にすると表題を消す。</summary>
    private void EditSelectedTitle()
    {
        var project = SelectedProject;
        if (project is null || _folderList.SelectedItems.Count != 1)
        {
            return;
        }

        var path = _folderList.SelectedItems[0].Name;
        var input = PromptForName("表題の変更", "表題（空欄にすると表題なし）", project.GetTitle(path));
        if (input is null)
        {
            return;
        }

        project.SetTitle(path, input);
        SaveAndRefresh(project.Id, [path]);
        _folderList.Focus();
    }

    private void FitFolderColumns()
    {
        if (_folderList.Columns.Count <= StatusColumn)
        {
            return;
        }

        var otherColumnsWidth = _folderList.Columns[KindColumn].Width + _folderList.Columns[StatusColumn].Width + _folderList.Columns[TitleColumn].Width;
        // 項目が増えて縦スクロールバーが出ても横スクロールバーが出ないよう、その幅を常に空けておく
        var available = _folderList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - otherColumnsWidth - 4;
        _folderList.Columns[PathColumn].Width = Math.Max(160, available);
    }

    private void UpdateFolderHeading()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _folderHeading.Text = "起動する項目";
            return;
        }

        var items = _folderList.Items.Cast<ListViewItem>().ToArray();
        var notes = new List<string>();
        var invalidCount = items.Count(item => item.Tag is TargetStatus { IsValid: false });
        var disabledCount = items.Count(item => !item.Checked);
        if (invalidCount > 0)
        {
            notes.Add($"無効 {invalidCount} 件");
        }

        if (disabledCount > 0)
        {
            notes.Add($"起動しない {disabledCount} 件");
        }

        _folderHeading.Text = notes.Count > 0
            ? $"{project.Name} の起動項目（{string.Join("・", notes)}）"
            : $"{project.Name} の起動項目";
    }

    private void SaveSelectedProject()
    {
        var preferences = _preferencesStore.Load();
        preferences.LastSelectedProjectId = SelectedProject?.Id;
        _preferencesStore.Save(preferences);
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
        _projectList.Focus();
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

        // 削除後は同じ位置（末尾なら一つ上）のプロジェクトを選び、続けて操作できるようにする
        var index = _settings.Projects.IndexOf(project);
        _settings.Projects.Remove(project);
        var neighbor = _settings.Projects.Count == 0 ? null : _settings.Projects[Math.Min(index, _settings.Projects.Count - 1)];
        SaveAndRefresh(neighbor?.Id);
        _projectList.Focus();
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

        var requested = items.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray();
        foreach (var item in requested)
        {
            if (!project.Folders.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                project.Folders.Add(item);
            }
        }

        // 追加した項目（登録済みだったものも）を選択して、続けて並び替えや起動ができるようにする
        SaveAndRefresh(project.Id, requested);
        _folderList.Focus();
    }

    private void RemoveFolders()
    {
        var project = SelectedProject;
        if (project is null || _folderList.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedPaths = _folderList.SelectedItems.Cast<ListViewItem>().Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstIndex = _folderList.SelectedIndices.Cast<int>().Min();
        project.Folders.RemoveAll(folder => selectedPaths.Contains(folder));
        project.RemoveUnusedItemSettings();
        SaveAndRefresh(project.Id);

        // 削除した位置の項目を選び、続けて Delete や ↑↓ で操作できるようにする
        _folderList.Focus();
        SelectFolderAt(firstIndex);
    }

    private string[] SelectedFolderPaths() =>
        _folderList.SelectedItems.Cast<ListViewItem>().Select(item => item.Name).ToArray();

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

    /// <summary>一覧で選択した項目を起動する（Enter・ダブルクリック・右クリックメニュー）。</summary>
    private void OpenSelectedItems()
    {
        var paths = SelectedFolderPaths();
        if (paths.Length > 0)
        {
            _ = LaunchItemsAsync(paths, kind: null, reportMissing: true);
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

    /// <summary>「▶ 一括起動」：「起動する対象」で選んだ範囲を起動する。</summary>
    private async Task LaunchProjectAsync()
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        var scope = (LaunchScope)Math.Max(0, _launchScopeBox.SelectedIndex);
        if (scope == LaunchScope.Selected)
        {
            var selected = SelectedFolderPaths();
            if (selected.Length == 0)
            {
                MessageBox.Show(this, "起動する項目を一覧で選択してください。", "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await LaunchItemsAsync(selected, kind: null, reportMissing: true);
            return;
        }

        LaunchTargetKind? kind = scope switch
        {
            LaunchScope.Folders => LaunchTargetKind.Folder,
            LaunchScope.Files => LaunchTargetKind.File,
            LaunchScope.Urls => LaunchTargetKind.Url,
            _ => null
        };
        // チェックを外した項目（起動しない）は一括起動の対象にしない
        var enabled = project.Folders.Where(project.IsEnabled).ToArray();
        if (enabled.Length == 0)
        {
            MessageBox.Show(this, "起動する項目がありません。一覧のチェックを付けた項目だけが一括起動の対象になります。", "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await LaunchItemsAsync(enabled, kind, reportMissing: false);
    }

    /// <summary>
    /// 項目を起動する。フォルダーはエクスプローラーの1ウィンドウにタブでまとめて開き、続いてファイルとURLを順に開く。
    /// kind を指定するとその種類だけを起動する。reportMissing が true なら、見つからない項目も最後に知らせる。
    /// </summary>
    private async Task LaunchItemsAsync(string[] items, LaunchTargetKind? kind, bool reportMissing)
    {
        if (!_launchButton.Enabled)
        {
            // 起動処理の実行中は重ねて起動しない
            return;
        }

        _launchButton.Enabled = false;
        try
        {
            // 存在確認はスリープ中のドライブやファイルサーバーで時間がかかることがあるため、UIスレッド外で並行して行う
            var targets = await Task.Run(() => items.AsParallel().AsOrdered().Select(item => (Path: item, Kind: LaunchTarget.Classify(item))).ToArray());
            var plan = LaunchTarget.Plan(targets, kind);
            if (plan.IsEmpty)
            {
                var what = kind is null ? "項目" : LaunchTarget.DisplayName(kind.Value);
                MessageBox.Show(this, $"起動できる{what}が見つかりません。パスやURLを確認してください。", "一括起動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (plan.Folders.Count > 0)
            {
                var folders = plan.Folders.ToArray();
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

            var errors = OpenItems(plan.Others);
            if (reportMissing)
            {
                errors.InsertRange(0, plan.Missing.Select(path => $"見つかりません: {path}"));
            }

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

    /// <summary>パス・URLの一括置き換え。選択中の項目があれば、そのドライブ／サーバーの部分を初期値にする。</summary>
    private void ReplacePaths()
    {
        if (_settings.Projects.Count == 0)
        {
            return;
        }

        var selected = SelectedFolderPaths().FirstOrDefault();
        var initial = selected is not null && !LaunchTarget.IsUrl(selected) ? Path.GetPathRoot(selected) ?? string.Empty : string.Empty;
        using var dialog = new ReplacePathsDialog(_settings.Projects, SelectedProject, initial, Font);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var count = dialog.TargetProjects.ToArray().Sum(project => project.ReplaceInItems(dialog.OldText, dialog.NewText));
        SaveAndRefresh(SelectedProject?.Id);
        MessageBox.Show(this, $"{count} 件を置き換えました。", "一括置き換え", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    .Where(item => selectedFolderPaths.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var selectedItem in selectedItems)
                {
                    selectedItem.Selected = true;
                }

                if (selectedItems.FirstOrDefault() is { } firstItem)
                {
                    firstItem.Focused = true;
                    firstItem.EnsureVisible();
                }
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