// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._White;
using Content.Shared._White.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._White.Explosion;

public sealed class ExplosionShockWaveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShockWaveShader = "ShockWave";

    public const int MaxCount = 10;

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly ShaderInstance _shader;

    private readonly Vector2[] _positions = new Vector2[MaxCount];
    private readonly float[] _falloffPower = new float[MaxCount];
    private readonly float[] _sharpness = new float[MaxCount];
    private readonly float[] _width = new float[MaxCount];
    private int _count;

    private SharedTransformSystem? _xformSystem;
    private bool _enabled;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public ExplosionShockWaveOverlay()
    {
        IoCManager.InjectDependencies(this);

        _shader = _prototypeManager.Index(ShockWaveShader).InstanceUnique();
        _cfg.OnValueChanged(WhiteCVars.ShowExplosionShockWave, val => _enabled = val, true);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_enabled)
            return false;

        if (args.Viewport.Eye == null || _xformSystem is null && !_entMan.TrySystem(out _xformSystem))
            return false;

        var query = _entMan.EntityQueryEnumerator<ExplosionShockWaveComponent, TransformComponent>();

        _count = 0;

        while (query.MoveNext(out var uid, out var distortion, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var mapPos = _xformSystem.GetWorldPosition(uid);

            var tempCoords = args.Viewport.WorldToLocal(mapPos);

            tempCoords.Y = 1 - tempCoords.Y / args.Viewport.Size.Y;
            tempCoords.X /= args.Viewport.Size.X;

            _positions[_count] = tempCoords;
            _falloffPower[_count] = distortion.FalloffPower;
            _sharpness[_count] = distortion.Sharpness;
            _width[_count] = distortion.Width;
            _count++;

            if (_count == MaxCount)
                break;
        }

        return _count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        _shader.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("count", _count);
        _shader.SetParameter("position", _positions);
        _shader.SetParameter("falloffPower", _falloffPower);
        _shader.SetParameter("sharpness", _sharpness);
        _shader.SetParameter("width", _width);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldBounds, Color.White);
        worldHandle.UseShader(null);
    }
}
