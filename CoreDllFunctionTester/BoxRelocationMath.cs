namespace CoreDllFunctionTester;

/// <summary>
/// 箱子重定位算法内核（纯数学模型，无业务工程依赖）。
/// 可直接复制到 Robot 工程复用。
/// </summary>
public static class BoxRelocationMath
{
    public readonly record struct PalletCorner3(
        double Xmm,
        double Ymm,
        double Zmm);

    public readonly record struct PalletFrame(
        double LengthMm,
        double WidthMm,
        double FloorZMm,
        double CenterXmm,
        double CenterYmm,
        double YawDeg,
        double AxisXX,
        double AxisXY,
        double AxisYX,
        double AxisYY);

    public readonly record struct RelativeBoxPose(
        double Xmm,
        double Ymm,
        int OrientationDeg);

    public readonly record struct WorldBoxPose(
        double Xmm,
        double Ymm,
        int OrientationDeg);

    /// <summary>
    /// 由托盘四角点构建重定位框架。
    /// 角点顺序约定：1->2 为托盘长度方向（+X），2->3 为宽度方向（+Y）。
    /// </summary>
    public static PalletFrame BuildFrame(IReadOnlyList<PalletCorner3> corners)
    {
        if (corners is null || corners.Count != 4)
            throw new ArgumentException("托盘角点必须提供 4 个。", nameof(corners));

        PalletCorner3 c1 = corners[0];
        PalletCorner3 c2 = corners[1];
        PalletCorner3 c3 = corners[2];
        PalletCorner3 c4 = corners[3];

        ValidateCorner(c1, nameof(corners));
        ValidateCorner(c2, nameof(corners));
        ValidateCorner(c3, nameof(corners));
        ValidateCorner(c4, nameof(corners));

        double edge12 = Distance2D(c1, c2);
        double edge23 = Distance2D(c2, c3);
        double edge34 = Distance2D(c3, c4);
        double edge41 = Distance2D(c4, c1);
        if (edge12 <= 0 || edge23 <= 0 || edge34 <= 0 || edge41 <= 0)
            throw new ArgumentException("托盘角点不能重合，边长必须大于 0。", nameof(corners));

        double lengthMm = (edge12 + edge34) / 2.0;
        double widthMm = (edge23 + edge41) / 2.0;
        double centerX = (c1.Xmm + c2.Xmm + c3.Xmm + c4.Xmm) / 4.0;
        double centerY = (c1.Ymm + c2.Ymm + c3.Ymm + c4.Ymm) / 4.0;
        double floorZ = (c1.Zmm + c2.Zmm + c3.Zmm + c4.Zmm) / 4.0;

        // X 轴由 1->2 确定，Y 轴取其左手垂线，并按 1->4 修正方向。
        double axisXX = (c2.Xmm - c1.Xmm) / edge12;
        double axisXY = (c2.Ymm - c1.Ymm) / edge12;
        double axisYX = -axisXY;
        double axisYY = axisXX;

        double edge14X = c4.Xmm - c1.Xmm;
        double edge14Y = c4.Ymm - c1.Ymm;
        double dot = edge14X * axisYX + edge14Y * axisYY;
        if (dot < 0)
        {
            axisYX = -axisYX;
            axisYY = -axisYY;
        }

        double yawDeg = Math.Atan2(axisXY, axisXX) * 180.0 / Math.PI;

        return new PalletFrame(
            LengthMm: lengthMm,
            WidthMm: widthMm,
            FloorZMm: floorZ,
            CenterXmm: centerX,
            CenterYmm: centerY,
            YawDeg: yawDeg,
            AxisXX: axisXX,
            AxisXY: axisXY,
            AxisYX: axisYX,
            AxisYY: axisYY);
    }

    /// <summary>
    /// 将相对托盘中心坐标重定位到世界坐标。
    /// </summary>
    public static WorldBoxPose RelocateToWorld(RelativeBoxPose relativePose, in PalletFrame frame)
    {
        double worldX = frame.CenterXmm
            + relativePose.Xmm * frame.AxisXX
            + relativePose.Ymm * frame.AxisYX;
        double worldY = frame.CenterYmm
            + relativePose.Xmm * frame.AxisXY
            + relativePose.Ymm * frame.AxisYY;

        return new WorldBoxPose(
            Xmm: Round(worldX),
            Ymm: Round(worldY),
            OrientationDeg: NormalizeAngleDeg(relativePose.OrientationDeg + frame.YawDeg));
    }

    private static void ValidateCorner(PalletCorner3 corner, string paramName)
    {
        if (!double.IsFinite(corner.Xmm) || !double.IsFinite(corner.Ymm) || !double.IsFinite(corner.Zmm))
            throw new ArgumentException("托盘角点坐标必须为有限数值。", paramName);
    }

    private static double Distance2D(PalletCorner3 a, PalletCorner3 b)
    {
        double dx = a.Xmm - b.Xmm;
        double dy = a.Ymm - b.Ymm;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int NormalizeAngleDeg(double angleDeg)
    {
        double normalized = angleDeg % 360.0;
        if (normalized < 0)
            normalized += 360.0;

        int rounded = (int)Math.Round(normalized, MidpointRounding.AwayFromZero) % 360;
        return rounded < 0 ? rounded + 360 : rounded;
    }

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
