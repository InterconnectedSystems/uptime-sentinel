namespace SiteMonitor.Gui;

/// <summary>Small helpers for building forms in code (no designer files).</summary>
internal static class FormHelpers
{
    public static TableLayoutPanel Grid(int columns = 2)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            AutoSize = true,
            Padding = new Padding(12),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 1; i < columns; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    public static T AddRow<T>(this TableLayoutPanel t, string label, T control, string? tip = null, ToolTip? toolTip = null) where T : Control
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(l, 0, t.RowCount - 1);
        t.Controls.Add(control, 1, t.RowCount - 1);
        if (tip is not null && toolTip is not null)
        {
            toolTip.SetToolTip(l, tip);
            toolTip.SetToolTip(control, tip);
        }
        return control;
    }

    public static Control AddFull(this TableLayoutPanel t, Control control)
    {
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(control, 0, t.RowCount - 1);
        t.SetColumnSpan(control, t.ColumnCount);
        return control;
    }

    public static NumericUpDown Number(decimal min, decimal max, decimal value, int width = 100) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = width,
        Anchor = AnchorStyles.Left,
        ThousandsSeparator = true,
    };

    /// <summary>OK/Cancel button row docked to the bottom of a dialog.</summary>
    public static FlowLayoutPanel DialogButtons(Form form, Func<bool> onOk)
    {
        var ok = new Button { Text = "OK", Width = 90 };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (onOk())
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        panel.Controls.Add(cancel);
        panel.Controls.Add(ok);
        return panel;
    }

    public static void ConfigureDialog(Form f, string title, int width)
    {
        f.Text = title;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowInTaskbar = false;
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Font = new Font("Segoe UI", 9F);
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        f.MinimumSize = new Size(width, 0);
    }

    public static void Error(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, "SiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
