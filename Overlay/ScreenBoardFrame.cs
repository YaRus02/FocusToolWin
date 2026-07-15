using System.Windows.Media.Imaging;

namespace FocusTool.Win.Overlay;

internal sealed class ScreenBoardFrame
{
    public ScreenBoardFrame(
        ScreenRect bounds,
        BitmapSource image,
        IEnumerable<int>? bakedRegionMaskIds = null)
    {
        Bounds = bounds;
        Image = image;
        BakedRegionMaskIds = bakedRegionMaskIds is null
            ? new HashSet<int>()
            : new HashSet<int>(bakedRegionMaskIds);
    }

    public ScreenRect Bounds { get; }
    public BitmapSource Image { get; }
    public IReadOnlySet<int> BakedRegionMaskIds { get; }
}

internal sealed class ScreenBoardPrivacySnapshot
{
    public ScreenBoardPrivacySnapshot(BitmapSource? layer, IEnumerable<int> maskIds)
    {
        Layer = layer;
        MaskIds = new HashSet<int>(maskIds);
    }

    public BitmapSource? Layer { get; }
    public IReadOnlySet<int> MaskIds { get; }
    public bool HasMasks => MaskIds.Count > 0;
    public bool IsComplete => !HasMasks || Layer is not null;
}
