using Content.Shared._Maid.Tools;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Tools;

public sealed class WeldingGlowSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeldingGlowComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WeldingGlowComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.StartTime = _timing.CurTime;
        Dirty(ent);
    }

    /// <summary>
    /// Ends the glow of a welding effect and deletes it after the fade out.
    /// Effects without a glow are deleted right away.
    /// </summary>
    public void Release(EntityUid uid)
    {
        if (!TryComp<WeldingGlowComponent>(uid, out var glow))
        {
            QueueDel(uid);
            return;
        }

        if (glow.EndTime != null)
            return;

        glow.EndTime = _timing.CurTime;
        Dirty(uid, glow);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<WeldingGlowComponent>();
        while (query.MoveNext(out var uid, out var glow))
        {
            if (glow.EndTime is { } end && curTime >= end + TimeSpan.FromSeconds(glow.FadeTime))
                QueueDel(uid);
        }
    }
}
