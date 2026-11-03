using Robust.Client.Graphics;

namespace Content.Client._Maid.Lighting;

/// <summary>
/// Registers <see cref="StationSunShadowOverlay"/>.
/// </summary>
public sealed class StationSunShadowSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new StationSunShadowOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<StationSunShadowOverlay>();
    }
}
