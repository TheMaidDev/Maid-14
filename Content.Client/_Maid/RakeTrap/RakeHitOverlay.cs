using Content.Shared._Maid.RakeTrap;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Maid.RakeTrap;

/// <summary>
///     Full-screen "rake in the face" effect: a short hot flash followed by a decaying
///     dizzy wobble with chromatic aberration (shader MaidRakeHit).
/// </summary>
public sealed class RakeHitOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "MaidRakeHit";

    /// <summary>How long the impact flash lasts (seconds).</summary>
    private const float FlashTime = 0.15f;

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    private float _impact;
    private float _daze;

    public RakeHitOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(Shader).InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        _impact = 0f;
        _daze = 0f;

        var player = _playerManager.LocalEntity;
        if (player == null || !_entityManager.TryGetComponent<RakeDazedComponent>(player, out var dazed))
            return;

        if (dazed.HitAt is not { } hitAt)
            return;

        var elapsed = (float)(_timing.CurTime - hitAt).TotalSeconds;
        var progress = Math.Clamp(elapsed / (float)dazed.Duration.TotalSeconds, 0f, 1f);

        // Impact flash: hot for the first moments, then gone.
        var flashProgress = elapsed / FlashTime;
        _impact = flashProgress >= 1f ? 0f : 1f - MathF.Pow(flashProgress, 3f);

        // Dizzy wobble: linear decay over the whole daze.
        _daze = 1f - progress;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity == null)
            return false;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return _impact > 0.001f || _daze > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        // Reduced motion: no wobble, just a brief static flash tint.
        if (_cfg.GetCVar(CCVars.ReducedMotion))
        {
            if (_impact > 0.001f)
                handle.DrawRect(args.WorldBounds, Color.FromHex("#c8402c").WithAlpha(_impact * 0.7f));
            return;
        }

        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("impact", _impact);
        _shader.SetParameter("daze", _daze);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
