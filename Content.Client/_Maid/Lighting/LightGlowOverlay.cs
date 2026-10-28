using System.Numerics;
using Content.Shared._Maid.Lighting;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Maid.Lighting;

/// <summary>
///     Draws a blurred glow mask over every enabled light with <see cref="LightGlowComponent"/>.
/// </summary>
public sealed class LightGlowOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "MaidLightGlow";

    private readonly IEntityManager _entMan;
    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    public override bool RequestScreenTexture => true;

    public LightGlowOverlay(IEntityManager entMan, IPrototypeManager protoMan)
    {
        _entMan = entMan;
        _sprite = entMan.System<SpriteSystem>();
        _transform = entMan.System<SharedTransformSystem>();
        _shader = protoMan.Index(Shader).InstanceUnique();
        ZIndex = (int) DrawDepth.Overdoors;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        var bounds = args.WorldAABB.Enlarged(5f);

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_shader);

        var query = _entMan.EntityQueryEnumerator<LightGlowComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out _, out var glow, out var light, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            if (!(glow.Enabled ?? light.Enabled))
                continue;

            var (worldPos, _, worldMatrix) = _transform.GetWorldPositionRotationMatrix(xform);
            if (!bounds.Contains(worldPos))
                continue;

            var mask = _sprite.Frame0(glow.Sprite);
            // Integer division is intentional: keeps the mask aligned to whole tiles like the old build.
            var offset = glow.Offset - new Vector2(mask.Width / 2 / EyeManager.PixelsPerMeter, mask.Height / 2 / EyeManager.PixelsPerMeter);

            handle.SetTransform(worldMatrix);
            handle.DrawTexture(mask, offset, glow.Color ?? light.Color);
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
