using System.Globalization;
using StackPlanner.Core;

namespace CoreDllFunctionTester;

/// <summary>
/// 托盘四角点设置弹窗。
/// </summary>
public sealed class PalletCornersDialog : Form
{
    private readonly TextBox[,] _inputs = new TextBox[4, 3];

    public PalletCornersDialog(IReadOnlyList<PalletCornerPoint> initialCorners)
    {
        if (initialCorners is null || initialCorners.Count != 4)
            throw new ArgumentException("初始角点必须包含 4 个点。", nameof(initialCorners));

        Text = "托盘尺寸设置（四角点 3D 坐标）";
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = 760;
        Height = 360;
        MinimumSize = new Size(760, 360);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            AutoScroll = true,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tipLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "请输入托盘四个角点坐标（mm）：X、Y 用于计算托盘边界，Z 取四角平均作为 PlaceFloorZ。",
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        AddHeaderCell(grid, 0, "角点");
        AddHeaderCell(grid, 1, "X (mm)");
        AddHeaderCell(grid, 2, "Y (mm)");
        AddHeaderCell(grid, 3, "Z (mm)");

        string[] names = ["角点1", "角点2", "角点3", "角点4"];
        for (int row = 0; row < 4; row++)
        {
            var nameLabel = new Label
            {
                Text = names[row],
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
            };
            grid.Controls.Add(nameLabel, 0, row + 1);

            var corner = initialCorners[row];
            _inputs[row, 0] = CreateValueBox(corner.Xmm);
            _inputs[row, 1] = CreateValueBox(corner.Ymm);
            _inputs[row, 2] = CreateValueBox(corner.Zmm);
            grid.Controls.Add(_inputs[row, 0], 1, row + 1);
            grid.Controls.Add(_inputs[row, 1], 2, row + 1);
            grid.Controls.Add(_inputs[row, 2], 3, row + 1);
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
        };

        var okButton = new Button
        {
            Text = "确定",
            AutoSize = true,
            DialogResult = DialogResult.OK,
        };
        okButton.Click += OnOkClicked;

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        root.Controls.Add(tipLabel, 0, 0);
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public PalletCornerPoint[] GetCorners()
    {
        var corners = new PalletCornerPoint[4];
        for (int row = 0; row < 4; row++)
        {
            corners[row] = new PalletCornerPoint
            {
                Xmm = ParseDouble(_inputs[row, 0].Text, row, "X"),
                Ymm = ParseDouble(_inputs[row, 1].Text, row, "Y"),
                Zmm = ParseDouble(_inputs[row, 2].Text, row, "Z"),
            };
        }

        return corners;
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        try
        {
            _ = GetCorners();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }

    private static TextBox CreateValueBox(double value)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Text = value.ToString("F3", CultureInfo.InvariantCulture),
        };
    }

    private static void AddHeaderCell(TableLayoutPanel grid, int column, string title)
    {
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
        };
        grid.Controls.Add(label, column, 0);
    }

    private static double ParseDouble(string text, int row, string axis)
    {
        string input = text.Trim();
        if (TryParseFiniteDouble(input, out var value))
            return value;

        throw new ArgumentException($"角点{row + 1} 的 {axis} 不是有效数字：{text}");
    }

    private static bool TryParseFiniteDouble(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            return double.IsFinite(value);

        value = 0;
        return false;
    }
}
