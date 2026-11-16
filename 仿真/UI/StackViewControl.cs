using System.Drawing.Drawing2D;

namespace CoreDllFunctionTester.Simulation.UI;

/// <summary>
/// 等轴测可视化控件：绘制托盘、已堆箱子和运动中的箱子。只负责绘制，不包含仿真逻辑。
/// 投影口径：先将平面坐标 (x, y) 相对托盘中心得到 (u, v) = (-dx, -dy)（基准视角再逆时针旋转 180°），
/// 再投影：屏幕 X = (u - v) * cos30，屏幕 Y = (u + v) / 2 - z。
/// </summary>
public sealed class StackViewControl : Control
{
    private const double Cos30 = 0.86602540378;
    private const double BaseViewRotationDeg = 180;

    /// <summary>坐标轴原点的 Y 坐标（Core 坐标 Y=0），X 取托盘 X 最小边，即原点为 (PalletXMin, 0, 0)。</summary>
    private const double AxisOriginYMm = 0;

    private static readonly Color[] Palette =
    {
        Color.FromArgb(242, 150, 110),
        Color.FromArgb(120, 160, 235),
        Color.FromArgb(110, 200, 150),
        Color.FromArgb(165, 140, 225),
        Color.FromArgb(235, 205, 100),
        Color.FromArgb(100, 190, 210),
    };

    private readonly Dictionary<string, Color> _colorByDimension = new(StringComparer.Ordinal);
    private SimulationScene? _scene;
    private List<SimulationBoxItem> _placed = new();
    private SimulationPose? _moving;
    private int _totalBoxes;
    private double _centerX;
    private double _centerY;
    private double _scale = 1;
    private double _viewRotationDeg = BaseViewRotationDeg;
    private PointF _origin;

    /// <summary>创建等轴测可视化控件。</summary>
    public StackViewControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(238, 242, 248);
    }

    /// <summary>设置仿真场景（托盘范围与总箱数），并清空已堆箱子。</summary>
    public void SetScene(SimulationScene scene)
    {
        _scene = scene;
        _totalBoxes = scene.Boxes.Count;
        _placed = new List<SimulationBoxItem>();
        _moving = null;
        RecalculateLayout();
        Invalidate();
    }

    /// <summary>设置已堆垛成功的箱子。</summary>
    public void SetPlacedBoxes(IEnumerable<SimulationBoxItem> boxes)
    {
        _placed = boxes.ToList();
        Invalidate();
    }

    /// <summary>设置运动中的箱子位姿，传入 null 表示清除。</summary>
    public void SetMovingPose(SimulationPose? pose)
    {
        _moving = pose;
        Invalidate();
    }

    /// <summary>设置视角旋转角度（度），0 为默认视角，正值逆时针。</summary>
    public void SetViewRotation(double angleDeg)
    {
        _viewRotationDeg = BaseViewRotationDeg + angleDeg;
        RecalculateLayout();
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_scene is null)
        {
            using var hintBrush = new SolidBrush(Color.Gray);
            g.DrawString("暂无规划数据", Font, hintBrush, 10, 10);
            return;
        }

        DrawPallet(g, _scene);

        var renderList = new List<RenderBox>(_placed.Count + 1);
        foreach (var box in _placed)
            renderList.Add(new RenderBox(box, box.XMm, box.YMm, box.BottomZMm, false));
        if (_moving is not null)
            renderList.Add(new RenderBox(_moving.Box, _moving.XMm, _moving.YMm, _moving.BottomZMm, true));

        // 画家算法：远处（x+y+z 小）先画，近处后画，保证堆叠遮挡关系正确。
        foreach (var item in renderList.OrderBy(Depth))
            DrawBox(g, item);

        // 坐标轴叠加在最上层，避免被箱子遮挡。
        DrawAxes(g, _scene);

        using var textBrush = new SolidBrush(Color.DimGray);
        g.DrawString($"已堆 {_placed.Count} / {_totalBoxes}    视角：等轴测（仅可视化，无物理）", Font, textBrush, 10, 8);
    }

    private void RecalculateLayout()
    {
        if (_scene is null)
            return;

        _centerX = (_scene.PalletXMinMm + _scene.PalletXMaxMm) / 2;
        _centerY = (_scene.PalletYMinMm + _scene.PalletYMaxMm) / 2;
        double zMax = Math.Max(_scene.StackMaxHeightMm, 1);
        var (ox, oy) = GetAxisOrigin(_scene);
        double length = GetAxisLength(_scene);

        // 参与包围盒计算的点：托盘底面与顶面四角、坐标轴原点及三个坐标轴端点，保证坐标轴不被裁切。
        var points = new List<(double X, double Y, double Z)>();
        foreach (var (x, y) in GetPalletCorners(_scene))
        {
            points.Add((x, y, 0));
            points.Add((x, y, zMax));
        }
        points.Add((ox, oy, 0));
        points.Add((ox + length, oy, 0));
        points.Add((ox, oy + length, 0));
        points.Add((ox, oy, length));

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            var (sx, sy) = RawProject(p.X, p.Y, p.Z);
            minX = Math.Min(minX, sx);
            maxX = Math.Max(maxX, sx);
            minY = Math.Min(minY, sy);
            maxY = Math.Max(maxY, sy);
        }

        double availableWidth = Math.Max(1, ClientSize.Width - 40);
        double availableHeight = Math.Max(1, ClientSize.Height - 40);
        double boundWidth = Math.Max(1e-6, maxX - minX);
        double boundHeight = Math.Max(1e-6, maxY - minY);
        _scale = Math.Min(availableWidth / boundWidth, availableHeight / boundHeight);

        // 让包围盒中心落在控件中心。
        _origin = new PointF(
            (float)(ClientSize.Width / 2.0 - (minX + maxX) / 2 * _scale),
            (float)(ClientSize.Height / 2.0 - (minY + maxY) / 2 * _scale));
    }

    /// <summary>坐标轴原点：托盘 X 最小边与 Y=AxisOriginYMm 处，即 Core 坐标 (PalletXMin, 0, 0)。</summary>
    private static (double X, double Y) GetAxisOrigin(SimulationScene scene)
        => (scene.PalletXMinMm, AxisOriginYMm);

    /// <summary>坐标轴长度取托盘较大边长的 25%，保证在任意托盘尺寸下都清晰可见。</summary>
    private static double GetAxisLength(SimulationScene scene)
        => Math.Max(scene.PalletXMaxMm - scene.PalletXMinMm, scene.PalletYMaxMm - scene.PalletYMinMm) * 0.25;

    /// <summary>托盘四个角点（底面平面坐标）。</summary>
    private static (double X, double Y)[] GetPalletCorners(SimulationScene scene)
    {
        var corners = new[]
        {
            (scene.PalletXMinMm, scene.PalletYMinMm),
            (scene.PalletXMaxMm, scene.PalletYMinMm),
            (scene.PalletXMaxMm, scene.PalletYMaxMm),
            (scene.PalletXMinMm, scene.PalletYMaxMm),
        };

        if (Math.Abs(scene.PalletTiltDeg) <= 0.0001)
            return corners;

        double centerX = (scene.PalletXMinMm + scene.PalletXMaxMm) / 2;
        double centerY = (scene.PalletYMinMm + scene.PalletYMaxMm) / 2;
        return corners
            .Select(corner => RotatePointAround(corner.Item1, corner.Item2, centerX, centerY, scene.PalletTiltDeg))
            .ToArray();
    }

    private static (double X, double Y) RotatePointAround(
        double x,
        double y,
        double centerX,
        double centerY,
        double angleDeg)
    {
        double radians = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double dx = x - centerX;
        double dy = y - centerY;
        return (
            centerX + dx * cos - dy * sin,
            centerY + dx * sin + dy * cos);
    }

    /// <summary>未缩放、未平移的投影结果，仅用于计算包围盒。</summary>
    private (double X, double Y) RawProject(double x, double y, double z)
    {
        var (u, v) = RotateView(x, y);
        return ((u - v) * Cos30, (u + v) * 0.5 - z);
    }

    /// <summary>将世界坐标先经过视角映射（RotateView），再做等轴测投影。</summary>
    private PointF Project(double x, double y, double z)
    {
        var (u, v) = RotateView(x, y);
        double screenX = _origin.X + (u - v) * Cos30 * _scale;
        double screenY = _origin.Y + ((u + v) * 0.5 - z) * _scale;
        return new PointF((float)screenX, (float)screenY);
    }

    /// <summary>视角映射：相对托盘中心偏移 (dx, dy) 旋转后得到 (u, v)。</summary>
    private (double U, double V) RotateView(double x, double y)
    {
        double dx = x - _centerX;
        double dy = y - _centerY;
        double radians = _viewRotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    /// <summary>计算视线深度 (u + v) 对世界 X、Y 的梯度符号，梯度为正的一侧朝向观察者。</summary>
    private (double Gx, double Gy) GetViewDepthGradient()
    {
        var (u0, v0) = RotateView(0, 0);
        var (ux, vx) = RotateView(1, 0);
        var (uy, vy) = RotateView(0, 1);
        return ((ux + vx) - (u0 + v0), (uy + vy) - (u0 + v0));
    }

    private void DrawPallet(Graphics g, SimulationScene scene)
    {
        var corners = GetPalletCorners(scene)
            .Select(corner => Project(corner.X, corner.Y, 0))
            .ToArray();

        using var fillBrush = new SolidBrush(Color.FromArgb(200, 210, 222));
        using var edgePen = new Pen(Color.FromArgb(90, 105, 125), 1.5f);
        g.FillPolygon(fillBrush, corners);
        g.DrawPolygon(edgePen, corners);
    }

    private void DrawBox(Graphics g, RenderBox item)
    {
        var box = item.Box;
        var corners = GetBoxFootprintCorners(box, item.X, item.Y);
        double z0 = item.BottomZ;
        double z1 = item.BottomZ + box.HeightMm;

        // 只绘制朝向观察者的三个面：顶面，以及由视线方向决定的两个侧面。
        var (gx, gy) = GetViewDepthGradient();
        var visibleEdges = GetVisibleEdges(corners, gx, gy);
        var topFace = corners.Select(c => Project(c.X, c.Y, z1)).ToArray();
        var sideFaceA = BuildSideFace(corners, visibleEdges[0], z0, z1);
        var sideFaceB = BuildSideFace(corners, visibleEdges[1], z0, z1);

        Color baseColor = GetColor(box);
        using var topBrush = new SolidBrush(ControlPaint.Light(baseColor));
        using var sideXBrush = new SolidBrush(baseColor);
        using var sideYBrush = new SolidBrush(ControlPaint.Dark(baseColor));
        using var edgePen = new Pen(
            item.Moving ? Color.OrangeRed : Color.FromArgb(70, 70, 70),
            item.Moving ? 2.5f : 1f);

        g.FillPolygon(topBrush, topFace);
        g.FillPolygon(sideXBrush, sideFaceA);
        g.FillPolygon(sideYBrush, sideFaceB);
        g.DrawPolygon(edgePen, topFace);
        g.DrawPolygon(edgePen, sideFaceA);
        g.DrawPolygon(edgePen, sideFaceB);
    }

    /// <summary>按箱体姿态角计算底面四角点（逆时针）。</summary>
    private static (double X, double Y)[] GetBoxFootprintCorners(SimulationBoxItem box, double centerX, double centerY)
    {
        double halfWidth = box.WidthMm / 2;
        double halfLength = box.LengthMm / 2;
        double radians = box.OrientationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        var localCorners = new[]
        {
            (-halfWidth, -halfLength),
            (halfWidth, -halfLength),
            (halfWidth, halfLength),
            (-halfWidth, halfLength),
        };

        return localCorners
            .Select(corner =>
            {
                double rx = corner.Item1 * cos - corner.Item2 * sin;
                double ry = corner.Item1 * sin + corner.Item2 * cos;
                return (centerX + rx, centerY + ry);
            })
            .ToArray();
    }

    /// <summary>从四条边中选择最朝向观察者的两条边。</summary>
    private static int[] GetVisibleEdges((double X, double Y)[] corners, double gx, double gy)
    {
        var scored = new List<(int EdgeIndex, double Score)>(4);
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            // corners 为逆时针顺序，外法线 = (dy, -dx)
            double nx = dy;
            double ny = -dx;
            double score = nx * gx + ny * gy;
            scored.Add((i, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(2)
            .Select(x => x.EdgeIndex)
            .ToArray();
    }

    /// <summary>由底面边构建对应的侧面四边形。</summary>
    private PointF[] BuildSideFace((double X, double Y)[] corners, int edgeIndex, double z0, double z1)
    {
        var a = corners[edgeIndex];
        var b = corners[(edgeIndex + 1) % corners.Length];
        return new[]
        {
            Project(a.X, a.Y, z0),
            Project(b.X, b.Y, z0),
            Project(b.X, b.Y, z1),
            Project(a.X, a.Y, z1),
        };
    }

    /// <summary>在 Core 坐标原点 (PalletXMin, 0, 0) 处绘制 X、Y、Z 三个方向的坐标轴。</summary>
    private void DrawAxes(Graphics g, SimulationScene scene)
    {
        double length = GetAxisLength(scene);
        var (ox, oy) = GetAxisOrigin(scene);

        var origin = Project(ox, oy, 0);
        var xEnd = Project(ox + length, oy, 0);
        var yEnd = Project(ox, oy + length, 0);
        var zEnd = Project(ox, oy, length);

        DrawAxis(g, origin, xEnd, Color.Red, "X");
        DrawAxis(g, origin, yEnd, Color.ForestGreen, "Y");
        DrawAxis(g, origin, zEnd, Color.RoyalBlue, "Z");

        // 原点标记
        using var originBrush = new SolidBrush(Color.Black);
        g.FillEllipse(originBrush, origin.X - 3, origin.Y - 3, 6, 6);
    }

    /// <summary>绘制带箭头的单个坐标轴及其标签。</summary>
    private void DrawAxis(Graphics g, PointF from, PointF to, Color color, string label)
    {
        using var pen = new Pen(color, 2f)
        {
            CustomEndCap = new AdjustableArrowCap(4, 5),
        };
        g.DrawLine(pen, from, to);

        using var labelFont = new Font(Font, FontStyle.Bold);
        using var labelBrush = new SolidBrush(color);
        g.DrawString(label, labelFont, labelBrush, to.X + 4, to.Y - 8);
    }

    private double Depth(RenderBox item)
    {
        // 使用旋转后的坐标计算深度，与投影口径保持一致。
        var (u, v) = RotateView(item.X, item.Y);
        return u + v + item.BottomZ + item.Box.HeightMm / 2;
    }

    private Color GetColor(SimulationBoxItem box)
    {
        // 同规格箱子使用同一颜色，便于区分不同规格。
        string key = $"{box.LengthMm:0}x{box.WidthMm:0}x{box.HeightMm:0}";
        if (!_colorByDimension.TryGetValue(key, out var color))
        {
            color = Palette[_colorByDimension.Count % Palette.Length];
            _colorByDimension[key] = color;
        }

        return color;
    }

    private sealed record RenderBox(SimulationBoxItem Box, double X, double Y, double BottomZ, bool Moving);
}
