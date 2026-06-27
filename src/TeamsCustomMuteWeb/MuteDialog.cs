using System.Drawing;
using System.Windows.Forms;

namespace TeamsCustomMute;

/// <summary>Modal dialog to choose a chat (typed or from favorites) and a mute duration.</summary>
public sealed class MuteDialog : Form
{
    private readonly TableLayoutPanel _root;
    private readonly ComboBox _chatBox;
    private readonly ComboBox _recentBox;
    private readonly ComboBox _durationBox;
    private readonly NumericUpDown _customHours;
    private readonly FlowLayoutPanel _customRow;
    private readonly CheckBox _saveFavorite;

    public string ChatName => _chatBox.Text.Trim();
    public TimeSpan Duration { get; private set; }
    public bool SaveToFavorites => _saveFavorite.Checked;

    private static readonly (string Label, TimeSpan? Span)[] Presets =
    {
        ("1 hour", TimeSpan.FromHours(1)),
        ("4 hours", TimeSpan.FromHours(4)),
        ("8 hours", TimeSpan.FromHours(8)),
        ("1 day", TimeSpan.FromDays(1)),
        ("1 week", TimeSpan.FromDays(7)),
        ("Custom (hours)\u2026", null),
    };

    public MuteDialog(IEnumerable<string> favorites, string? prefillChat = null, IEnumerable<string>? recentChats = null)
    {
        Text = "Mute a Teams chat";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        const int fieldWidth = 360;

        _root = new TableLayoutPanel
        {
            Location = new Point(0, 0),
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        var root = _root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fieldWidth));

        var chatLabel = new Label
        {
            Text = "Chat (type the name as shown in Teams, or pick a favorite):",
            AutoSize = true,
            MaximumSize = new Size(fieldWidth, 0),
            Margin = new Padding(0, 0, 0, 3),
        };
        _chatBox = new ComboBox
        {
            Width = fieldWidth,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDown, // editable + suggestions
            Margin = new Padding(0, 0, 0, 12),
        };
        _chatBox.Items.AddRange(favorites.Cast<object>().ToArray());
        if (!string.IsNullOrWhiteSpace(prefillChat))
            _chatBox.Text = prefillChat;

        var recentLabel = new Label
        {
            Text = "Or pick a recent chat:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _recentBox = new ComboBox
        {
            Width = fieldWidth,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 12),
        };
        var recentList = (recentChats ?? Enumerable.Empty<string>()).ToList();
        _recentBox.Items.Add(recentList.Count > 0
            ? "\u2014 pick a recent chat \u2014"
            : "(no recent chats found)");
        foreach (var c in recentList)
            _recentBox.Items.Add(c);
        _recentBox.SelectedIndex = 0;
        _recentBox.Enabled = recentList.Count > 0;
        _recentBox.SelectedIndexChanged += (_, _) =>
        {
            if (_recentBox.SelectedIndex > 0 && _recentBox.SelectedItem is string chat)
                _chatBox.Text = chat;
        };

        var durationLabel = new Label
        {
            Text = "Mute for:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _durationBox = new ComboBox
        {
            Width = fieldWidth,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 12),
        };
        foreach (var p in Presets)
            _durationBox.Items.Add(p.Label);
        _durationBox.SelectedIndex = 0;
        _durationBox.SelectedIndexChanged += (_, _) => UpdateCustomVisibility();

        _customRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 12),
            Visible = false,
            WrapContents = false,
        };
        var customLabel = new Label
        {
            Text = "Hours:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        };
        _customHours = new NumericUpDown
        {
            Width = 80, Minimum = 1, Maximum = 8760, Value = 2,
            Margin = new Padding(0),
        };
        _customRow.Controls.Add(customLabel);
        _customRow.Controls.Add(_customHours);

        _saveFavorite = new CheckBox
        {
            Text = "Add this chat to favorites",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
            WrapContents = false,
        };
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 32),
            Padding = new Padding(8, 4, 8, 4),
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(6, 0, 0, 0),
        };
        var ok = new Button
        {
            Text = "Mute",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 32),
            Padding = new Padding(8, 4, 8, 4),
            DialogResult = DialogResult.OK,
            Margin = new Padding(0),
        };
        ok.Click += OnOk;
        buttonRow.Controls.Add(cancel);
        buttonRow.Controls.Add(ok);

        root.Controls.Add(chatLabel);
        root.Controls.Add(_chatBox);
        root.Controls.Add(recentLabel);
        root.Controls.Add(_recentBox);
        root.Controls.Add(durationLabel);
        root.Controls.Add(_durationBox);
        root.Controls.Add(_customRow);
        root.Controls.Add(_saveFavorite);
        root.Controls.Add(buttonRow);

        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ResizeToContent();
    }

    private void ResizeToContent()
    {
        // Force a layout pass so the auto-size panel reports its real size; PreferredSize
        // alone under-measures before layout, which clipped the buttons.
        _root.PerformLayout();
        PerformLayout();
        ClientSize = new Size(
            Math.Max(_root.Width, 404),
            _root.Height + 12);
    }

    private void UpdateCustomVisibility()
    {
        _customRow.Visible = Presets[_durationBox.SelectedIndex].Span is null;
        if (IsHandleCreated)
            ResizeToContent();
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChatName))
        {
            MessageBox.Show(this, "Please enter a chat name.", "Teams Custom Mute",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var preset = Presets[_durationBox.SelectedIndex].Span;
        Duration = preset ?? TimeSpan.FromHours((double)_customHours.Value);
    }
}
