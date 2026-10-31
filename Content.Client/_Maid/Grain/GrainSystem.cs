using Content.Shared._Maid.CVars;
using Robust.Client.Graphics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Grain;

/// <summary>
///     Toggles <see cref="GrainOverlay"/> according to <see cref="MaidCVars.GrainEnabled"/>.
///     Never shown in display compatibility mode, like in wwdpublic.
/// </summary>
public sealed class GrainSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MaidCVars.GrainEnabled, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<GrainOverlay>();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (enabled && !_cfg.GetCVar(CVars.DisplayCompat))
            _overlay.AddOverlay(new GrainOverlay(_proto, _cfg, _timing));
        else
            _overlay.RemoveOverlay<GrainOverlay>();
    }
}
