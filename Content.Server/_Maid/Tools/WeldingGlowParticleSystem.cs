using Content.Shared._Maid.Tools;
using Content.Shared.DoAfter;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Tools;

/// <summary>
/// Times the welding glow particles: ends them when the user's weld is over and deletes them after the fade out.
/// </summary>
public sealed class WeldingGlowParticleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private EntityQuery<DoAfterComponent> _doAfterQuery;

    public override void Initialize()
    {
        base.Initialize();

        _doAfterQuery = GetEntityQuery<DoAfterComponent>();

        SubscribeLocalEvent<WeldingGlowParticleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WeldingGlowParticleComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.StartTime = _timing.CurTime;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<WeldingGlowParticleComponent>();
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

    private bool IsWelding(WeldingGlowParticleComponent glow)
    {
        return _doAfterQuery.TryGetComponent(glow.User, out var doAfters)
               && _doAfter.TryGetDoAfter(doAfters, glow.DoAfterIdx, out var doAfter)
               && doAfter is { Completed: false, Cancelled: false };
    }
}
