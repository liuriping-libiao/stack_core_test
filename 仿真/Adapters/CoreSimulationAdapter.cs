using StackPlanner.Core;

namespace CoreDllFunctionTester.Simulation.Adapters;

/// <summary>
/// 仿真模块与 StackPlanner.Core 之间的唯一适配点。
/// 仿真引擎与界面不引用 Core，只通过本适配器读取规划结果并回写箱子状态。
/// 替换或删除 Core 对接时，只需修改本文件。
/// </summary>
public sealed class CoreSimulationAdapter : ISimulationDataSource, ISimulationStatusSink
{
    private readonly StackPlanner.Core.StackPlanner _planner;
    private readonly Func<StackPlanResult>? _scenePlanProvider;
    private readonly Func<BoxPlacementRelocator.PalletFrame>? _palletFrameProvider;

    /// <summary>创建适配器，使用调用方持有的 Core 规划器实例（同一份内存状态）。</summary>
    public CoreSimulationAdapter(
        StackPlanner.Core.StackPlanner planner,
        Func<StackPlanResult>? scenePlanProvider = null,
        Func<BoxPlacementRelocator.PalletFrame>? palletFrameProvider = null)
    {
        _planner = planner;
        _scenePlanProvider = scenePlanProvider;
        _palletFrameProvider = palletFrameProvider;
    }

    /// <inheritdoc />
    public SimulationScene LoadScene()
    {
        var scenePlan = _scenePlanProvider?.Invoke() ?? _planner.CurrentPlan;
        var placementsByNumber = scenePlan.Placements
            .GroupBy(item => item.BoxNumber, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First(), StringComparer.Ordinal);
        var originalPlanByNumber = _planner.CurrentPlan.Placements
            .GroupBy(item => item.BoxNumber, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First(), StringComparer.Ordinal);

        var details = _planner.GetPlannerBoxDetailsV2();
        var boxes = details.Boxes
            .Where(x => placementsByNumber.ContainsKey(x.BoxNumber))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.BoxNumber, StringComparer.Ordinal)
            .Select(x =>
            {
                var placement = placementsByNumber[x.BoxNumber];
                var originalPlacement = originalPlanByNumber.TryGetValue(x.BoxNumber, out var found)
                    ? found
                    : placement;
                return new SimulationBoxItem
                {
                    BoxNumber = x.BoxNumber,
                    Order = placement.Order,
                    LengthMm = x.LengthMm,
                    WidthMm = x.WidthMm,
                    HeightMm = x.HeightMm,
                    WeightKg = x.WeightKg,
                    XMm = placement.Xmm,
                    YMm = placement.Ymm,
                    OriginalXMm = originalPlacement.Xmm,
                    OriginalYMm = originalPlacement.Ymm,
                    // Core 的 Zmm = PlaceFloorZ - 箱底高度 - 箱高，这里反算出箱底高度。
                    BottomZMm = _planner.PlaceFloorZ - placement.Zmm - x.HeightMm,
                    OrientationDeg = placement.OrientationDeg,
                    OriginalOrientationDeg = originalPlacement.OrientationDeg,
                    LayerIndex = placement.LayerIndex,
                    RowIndex = x.RowIndex,
                    ColumnIndex = x.ColumnIndex,
                    IsSucceeded = x.Status == BoxStatus.StackingSucceeded,
                };
            })
            .ToList();

        double palletXMin = _planner.PalletXMin;
        double palletXMax = _planner.PalletXMax;
        double palletYMin = _planner.PalletYMin;
        double palletYMax = _planner.PalletYMax;
        double palletTiltDeg = _planner.PalletTiltDeg;

        if (_palletFrameProvider is not null)
        {
            var frame = _palletFrameProvider();
            palletXMin = frame.CenterXmm - frame.LengthMm / 2.0;
            palletXMax = frame.CenterXmm + frame.LengthMm / 2.0;
            palletYMin = frame.CenterYmm - frame.WidthMm / 2.0;
            palletYMax = frame.CenterYmm + frame.WidthMm / 2.0;
            palletTiltDeg = frame.YawDeg;
        }

        return new SimulationScene
        {
            PalletXMinMm = palletXMin,
            PalletXMaxMm = palletXMax,
            PalletYMinMm = palletYMin,
            PalletYMaxMm = palletYMax,
            PalletTiltDeg = palletTiltDeg,
            StackMaxHeightMm = StackPlanner.Core.StackPlanner.StackMaxHeightMm,
            Boxes = boxes,
        };
    }

    /// <inheritdoc />
    public void SetState(SimulationBoxItem box, SimulationBoxState state)
    {
        BoxStatus coreStatus;
        BoxPlacement? placement = null;

        switch (state)
        {
            case SimulationBoxState.Pending:
                coreStatus = BoxStatus.OnShelf;
                break;
            case SimulationBoxState.Stacking:
                coreStatus = BoxStatus.Stacking;
                break;
            case SimulationBoxState.Succeeded:
                coreStatus = BoxStatus.StackingSucceeded;
                placement = ResolveRelativePlacement(box);
                break;
            case SimulationBoxState.Failed:
                coreStatus = BoxStatus.StackingFailed;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "不支持的仿真状态。");
        }

        if (!_planner.UpdateBoxStatus(box.BoxNumber, coreStatus, placement))
            throw new InvalidOperationException($"Core 中不存在箱号 {box.BoxNumber}。");
    }

    private BoxPlacement ResolveRelativePlacement(SimulationBoxItem box)
    {
        var current = _planner.CurrentPlan.Placements
            .Where(item => string.Equals(item.BoxNumber, box.BoxNumber, StringComparison.Ordinal))
            .OrderBy(item => item.Order)
            .FirstOrDefault();
        if (current is not null)
            return current;

        return new BoxPlacement
        {
            Order = box.Order,
            BoxNumber = box.BoxNumber,
            Xmm = box.XMm,
            Ymm = box.YMm,
            Zmm = _planner.PlaceFloorZ - box.BottomZMm - box.HeightMm,
            OrientationDeg = box.OrientationDeg,
            LayerIndex = box.LayerIndex,
        };
    }
}
