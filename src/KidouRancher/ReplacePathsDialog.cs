using KidouRancher.Models;

namespace KidouRancher;

/// <summary>
/// 項目のパス・URLの一括置き換え（ファイルサーバーの移転など）。
/// 入力に合わせて、置き換わる項目を変更前・変更後で一覧表示してから実行する。
/// </summary>
public sealed class ReplacePathsDialog : Form
{
    private readonly IReadOnlyList<LauncherProject> _allProjects;
    private readonly LauncherProject? _currentProject;
    private readonly TextBox _oldText = new();
    private readonly TextBox _newText = new();
    private readonly RadioButton _currentOnly = new();
    private readonly RadioButton _allProjectsOption = new();
    private readonly ListView _preview = new();
    private readonly Label _summary = new();
    private readonly Button _replaceButton = new();

    public ReplacePathsDialog(IReadOnlyList<LauncherProject> allProjects, LauncherProject? currentProject, string initialOldText, Font font)
    {
        _allProjects = allProjects;
        _currentProject = currentProject;

        Text = "パス・URLの一括置き換え";
        Font = font;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 460);
        MinimumSize = new Size(520, 400);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "置き換える文字(&F)", AutoSize = true, Margin = new Padding(0, 7, 8, 0) }, 0, 0);
        _oldText.Dock = DockStyle.Fill;
        _oldText.Text = initialOldText;
        _oldText.PlaceholderText = @"例: \\旧サーバー\share";
        layout.Controls.Add(_oldText, 1, 0);

        layout.Controls.Add(new Label { Text = "新しい文字(&T)", AutoSize = true, Margin = new Padding(0, 7, 8, 0) }, 0, 1);
        _newText.Dock = DockStyle.Fill;
        _newText.PlaceholderText = @"例: \\192.168.1.20\share";
        layout.Controls.Add(_newText, 1, 1);

        var scope = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        _currentOnly.Text = currentProject is null ? "このプロジェクトだけ(&C)" : $"「{currentProject.Name}」だけ(&C)";
        _currentOnly.AutoSize = true;
        _currentOnly.Enabled = currentProject is not null;
        _currentOnly.Checked = currentProject is not null;
        _allProjectsOption.Text = "すべてのプロジェクト(&A)";
        _allProjectsOption.AutoSize = true;
        _allProjectsOption.Checked = currentProject is null;
        scope.Controls.Add(_currentOnly);
        scope.Controls.Add(_allProjectsOption);
        layout.Controls.Add(new Label { Text = "対象", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 2);
        layout.Controls.Add(scope, 1, 2);

        _preview.Dock = DockStyle.Fill;
        _preview.View = View.Details;
        _preview.FullRowSelect = true;
        _preview.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _preview.Columns.Add("プロジェクト", 110);
        _preview.Columns.Add("変更前", 240);
        _preview.Columns.Add("変更後", 240);
        layout.Controls.Add(_preview, 0, 3);
        layout.SetColumnSpan(_preview, 2);

        _summary.AutoSize = true;
        _summary.Margin = new Padding(0, 6, 0, 6);
        layout.Controls.Add(_summary, 0, 4);
        layout.SetColumnSpan(_summary, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        _replaceButton.Text = "置き換える";
        _replaceButton.DialogResult = DialogResult.OK;
        _replaceButton.AutoSize = true;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_replaceButton);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = _replaceButton;
        CancelButton = cancel;

        _oldText.TextChanged += (_, _) => UpdatePreview();
        _newText.TextChanged += (_, _) => UpdatePreview();
        _currentOnly.CheckedChanged += (_, _) => UpdatePreview();
        Shown += (_, _) =>
        {
            _oldText.Focus();
            _oldText.SelectAll();
        };
        UpdatePreview();
    }

    public string OldText => _oldText.Text;

    public string NewText => _newText.Text;

    public bool AllProjects => _allProjectsOption.Checked;

    /// <summary>置き換えの対象になるプロジェクト。</summary>
    public IEnumerable<LauncherProject> TargetProjects =>
        AllProjects || _currentProject is null ? _allProjects : new[] { _currentProject };

    private void UpdatePreview()
    {
        _preview.BeginUpdate();
        _preview.Items.Clear();
        var count = 0;
        if (OldText.Length > 0)
        {
            foreach (var project in TargetProjects)
            {
                foreach (var item in project.Folders)
                {
                    var replaced = item.Replace(OldText, NewText, StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(replaced, item, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    count++;
                    var row = new ListViewItem(project.Name);
                    row.SubItems.Add(item);
                    row.SubItems.Add(replaced);
                    row.ToolTipText = $"{item}\n→ {replaced}";
                    _preview.Items.Add(row);
                }
            }
        }

        _preview.ShowItemToolTips = true;
        _preview.EndUpdate();
        _summary.Text = OldText.Length == 0
            ? "置き換える文字を入力すると、変わる項目をここに表示します（大文字・小文字は区別しません）。"
            : count == 0
                ? "置き換わる項目はありません。"
                : $"{count} 件を置き換えます。表題と「起動しない」の設定も新しいパスに引き継ぎます。";
        _replaceButton.Enabled = count > 0;
    }
}
