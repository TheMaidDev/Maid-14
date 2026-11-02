using System.Numerics;
using Content.Goobstation.Common.Tools;
using Content.Shared._Maid.Tools;
using Content.Shared.DoAfter;
using Content.Shared.Tools.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Tools;

/// <summary>
/// Attaches a flickering light to a user while they weld, and removes it after the fade out once the weld is over.
/// </summary>
public sealed class WeldingGlowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly EntProtoId GlowProto = "EffectWeldingGlow";

    private EntityQuery<DoAfterComponent> _doAfterQuery;

    public override void Initialize()
    {
        base.Initialize();

        _doAfterQuery = GetEntityQuery<DoAfterComponent>();

        SubscribeLocalEvent<WelderComponent, UseToolEvent>(OnUseTool);
        SubscribeLocalEvent<WeldingGlowComponent, MapInitEvent>(OnMapInit);
    }

    private void OnUseTool(Entity<WelderComponent> ent, ref UseToolEvent args)
    {
        var glow = Spawn(GlowProto, new EntityCoordinates(args.User, Vector2.Zero));
        var comp = Comp<WeldingGlowComponent>(glow);
        comp.User = args.User;
        comp.DoAfterIdx = args.DoAfterIdx;
    }

    private void OnMapInit(Entity<WeldingGlowComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.StartTime = _timing.CurTime;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<WeldingGlowComponent>();
        while (query.MoveNext(out var uid, out var glow))
        {
            if (glow.EndTime is { } end)
            {
                if (curTime >= end + TimeSpan.FromSeconds(glow.FadeTime))
                    QueueDel(uid);

                continue;
            }

            if (IsWelding(glow))
                continue;

            glow.EndTime = curTime;
            Dirty(uid, glow);
        }
    }

    private bool IsWelding(WeldingGlowComponent glow)
    {
        return _doAfterQuery.TryGetComponent(glow.User, out var doAfters)
               && doAfters.DoAfters.TryGetValue(glow.DoAfterIdx, out var doAfter)
               && !doAfter.Completed
               && !doAfter.Cancelled;
    }
}
