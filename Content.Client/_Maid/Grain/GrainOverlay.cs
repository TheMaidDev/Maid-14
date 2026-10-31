using Content.Shared._Maid.CVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Grain;

/// <summary>
///     Film grain over the game view. Ported from wwdpublic.
/// </summary>
public sealed class GrainOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "MaidGrain";

    /// <summary>
    ///     Shader strength at 100% on the settings slider; the wwdpublic default.
    /// </summary>
    private const float BaseStrength = 50f;

    /// <summary>
    ///     The time passed to the shader wraps around this period to keep its math precise.
    /// </summary>
    private const double TimePeriod = 100;

    private readonly IConfigurationManager _cfg;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public GrainOverlay(IPrototypeManager protoMan, IConfigurationManager cfg, IGameTiming timing)
    {
        _cfg = cfg;
        _timing = timing;
        _shader = protoMan.Index(Shader).InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", BaseStrength * _cfg.GetCVar(MaidCVars.GrainStrength));
        _shader.SetParameter("time", (float) (_timing.RealTime.TotalSeconds % TimePeriod));
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
