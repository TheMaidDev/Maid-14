using Content.Shared._Maid.CVars;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Configuration;

namespace Content.Client._Maid.OfferItem;

/// <summary>
///     Toggles <see cref="OfferItemIndicatorsOverlay"/> according to <see cref="MaidCVars.OfferModeIndicatorsPointShow"/>.
/// </summary>
public sealed class OfferItemIndicatorsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MaidCVars.OfferModeIndicatorsPointShow, OnShowIndicatorsChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<OfferItemIndicatorsOverlay>();
    }

    private void OnShowIndicatorsChanged(bool show)
    {
        if (show)
            _overlay.AddOverlay(new OfferItemIndicatorsOverlay(_input, EntityManager, _eye, _player));
        else
            _overlay.RemoveOverlay<OfferItemIndicatorsOverlay>();
    }
}
