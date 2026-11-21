using StackPlanner.Core;

namespace CoreDllFunctionTester;

/// <summary>
/// 基于托盘四角点，将 Core 输出的“相对托盘中心”箱体位姿重定位到世界坐标。
/// 该算法文件独立于界面逻辑，便于后续迁移到 Robot 端复用。
/// </summary>
public static class BoxPlacementRelocator
{
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
        double AxisYY)
    {
        internal BoxRelocationMath.PalletFrame ToCore()
            => new(
                LengthMm,
                WidthMm,
                FloorZMm,
                CenterXmm,
                CenterYmm,
                YawDeg,
                AxisXX,
                AxisXY,
                AxisYX,
                AxisYY);

        internal static PalletFrame FromCore(BoxRelocationMath.PalletFrame frame)
            => new(
                frame.LengthMm,
                frame.WidthMm,
                frame.FloorZMm,
                frame.CenterXmm,
                frame.CenterYmm,
                frame.YawDeg,
                frame.AxisXX,
                frame.AxisXY,
                frame.AxisYX,
                frame.AxisYY);
    }

    /// <summary>
    /// 由托盘四角点构建重定位框架。
    /// 角点顺序约定：1->2 为托盘长度方向（+X），2->3 为宽度方向（+Y）。
    /// </summary>
    public static PalletFrame BuildFrame(IReadOnlyList<PalletCornerPoint> corners)
    {
        if (corners is null || corners.Count != 4)
            throw new ArgumentException("托盘角点必须提供 4 个。", nameof(corners));
        var coreCorners = corners.Select(corner => new BoxRelocationMath.PalletCorner3(
            corner.Xmm,
            corner.Ymm,
            corner.Zmm)).ToArray();
        var frame = BoxRelocationMath.BuildFrame(coreCorners);
        return PalletFrame.FromCore(frame);
    }

    /// <summary>
    /// 将 Core 的相对托盘中心坐标重定位到世界坐标。
    /// </summary>
    public static BoxPlacement RelocateToWorld(BoxPlacement relativePlacement, in PalletFrame frame)
    {
        var coreRelative = new BoxRelocationMath.RelativeBoxPose(
            relativePlacement.Xmm,
            relativePlacement.Ymm,
            relativePlacement.OrientationDeg);
        var worldPose = BoxRelocationMath.RelocateToWorld(coreRelative, frame.ToCore());

        return relativePlacement with
        {
            Xmm = worldPose.Xmm,
            Ymm = worldPose.Ymm,
            OrientationDeg = worldPose.OrientationDeg,
        };
    }
}
