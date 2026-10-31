using Content.Shared._Maid.CVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._Maid.Lighting;

/// <summary>
///     Toggles <see cref="LightGlowOverlay"/> according to <see cref="MaidCVars.EnableLightsGlowing"/>.
/// </summary>
public sealed class LightGlowSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MaidCVars.EnableLightsGlowing, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<LightGlowOverlay>();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (enabled)
            _overlay.AddOverlay(new LightGlowOverlay(EntityManager, _proto));
        else
            _overlay.RemoveOverlay<LightGlowOverlay>();
    }
}
