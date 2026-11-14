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
///     dizzy wobble with chromatic aberration (shader MaidRakeHit), then tunnel vision —
///     a dark screen with a clear circle in the middle that slowly opens up (shader CircleMask).
/// </summary>
public sealed class RakeHitOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "MaidRakeHit";
    private static readonly ProtoId<ShaderPrototype> TunnelShader = "CircleMask";

    /// <summary>How long the impact flash lasts (seconds).</summary>
    private const float FlashTime = 0.15f;

    /// <summary>Clear-vision circle radius while "seeing your nose" (screen pixels).</summary>
    private const float TunnelRadiusStart = 140f;

    /// <summary>Radius the circle grows to by the end of the tunnel phase (fully clear).</summary>
    private const float TunnelRadiusEnd = 4000f;

    /// <summary>How dark the screen is outside the tunnel circle (0..1).</summary>
    private const float TunnelDarkness = 0.95f;

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _tunnelShader;

    private float _impact;
    private float _daze;
    private float _tunnelRadius;

    public RakeHitOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(Shader).InstanceUnique();
        _tunnelShader = _prototypeManager.Index(TunnelShader).InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        _impact = 0f;
        _daze = 0f;
        _tunnelRadius = 0f;

        var player = _playerManager.LocalEntity;
        if (player == null || !_entityManager.TryGetComponent<RakeDazedComponent>(player, out var dazed))
            return;

        if (dazed.HitAt is not { } hitAt)
            return;

        var elapsed = (float)(_timing.CurTime - hitAt).TotalSeconds;

        // Phase 1: impact flash + dizzy wobble.
        var flashProgress = elapsed / FlashTime;
        _impact = flashProgress >= 1f ? 0f : 1f - MathF.Pow(flashProgress, 3f);

        var wobbleProgress = Math.Clamp(elapsed / (float)dazed.Duration.TotalSeconds, 0f, 1f);
        _daze = 1f - wobbleProgress;

        // Phase 2: tunnel vision — the clear circle grows from "your nose" to fully clear.
        var tunnelProgress = Math.Clamp(
            (elapsed - (float)dazed.Duration.TotalSeconds) / (float)dazed.TunnelTime.TotalSeconds,
            0f, 1f);
        var tunnelAmount = 1f - MathF.Pow(tunnelProgress, 3f);
        if (tunnelAmount > 0.001f)
            _tunnelRadius = MathHelper.Lerp(TunnelRadiusEnd, TunnelRadiusStart, tunnelAmount);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity == null)
            return false;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return _impact > 0.001f || _daze > 0.001f || _tunnelRadius > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        if (!_cfg.GetCVar(CCVars.ReducedMotion))
        {
            // Impact flash + dizzy wobble with chromatic aberration.
            if ((_impact > 0.001f || _daze > 0.001f) && ScreenTexture != null)
            {
                _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                _shader.SetParameter("impact", _impact);
                _shader.SetParameter("daze", _daze);
                handle.UseShader(_shader);
                handle.DrawRect(args.WorldBounds, Color.White);
                handle.UseShader(null);
            }
        }
        else if (_impact > 0.001f)
        {
            // Reduced motion: no wobble, just a brief static flash tint.
            handle.DrawRect(args.WorldBounds, Color.FromHex("#c8402c").WithAlpha(_impact * 0.7f));
        }

        // Tunnel vision: dark screen, clear circle in the middle (static — fine for reduced motion too).
        if (_tunnelRadius > 0f)
        {
            var zoom = 1f;
            if (_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eye))
                zoom = MathF.Max(eye.Zoom.X, 0.001f);

            _tunnelShader.SetParameter("Zoom", zoom);
            _tunnelShader.SetParameter("CircleRadius", _tunnelRadius);
            _tunnelShader.SetParameter("CircleMult", TunnelDarkness);
            handle.UseShader(_tunnelShader);
            handle.DrawRect(args.WorldBounds, Color.White);
            handle.UseShader(null);
        }
    }
}
