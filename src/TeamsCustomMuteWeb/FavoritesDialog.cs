using System.Windows.Forms;

namespace TeamsCustomMute;

/// <summary>Simple add/remove manager for the favorite chat names.</summary>
public sealed class FavoritesDialog : Form
{
    private readonly ListBox _list;
    private readonly TextBox _input;

    public FavoritesDialog()
    {
        Text = "Manage favorite chats";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new System.Drawing.Size(360, 320);

        var hint = new Label
        {
            Text = "Type the chat name exactly as it appears in Teams:",
            AutoSize = true, Left = 12, Top = 12,
        };

        _input = new TextBox { Left = 12, Top = 35, Width = 250 };
        var add = new Button { Text = "Add", Left = 270, Top = 33, Width = 78 };
        add.Click += (_, _) => AddCurrent();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddCurrent(); e.SuppressKeyPress = true; }
        };

        _list = new ListBox { Left = 12, Top = 70, Width = 336, Height = 195 };

        var remove = new Button { Text = "Remove selected", Left = 12, Top = 275, Width = 130 };
        remove.Click += (_, _) => RemoveSelected();

        var close = new Button { Text = "Close", Left = 273, Top = 275, Width = 75, DialogResult = DialogResult.OK };

        Controls.AddRange(new Control[] { hint, _input, add, _list, remove, close });
        AcceptButton = add;
        CancelButton = close;

        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var fav in FavoritesStore.Load())
            _list.Items.Add(fav);
    }

    private void AddCurrent()
    {
        var name = _input.Text.Trim();
        if (name.Length == 0)
            return;

        FavoritesStore.Add(name);
        _input.Clear();
        Reload();
        _input.Focus();
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not string selected)
            return;

        var remaining = FavoritesStore.Load()
            .Where(s => !s.Equals(selected, StringComparison.OrdinalIgnoreCase));
        FavoritesStore.Save(remaining);
        Reload();
    }
}
