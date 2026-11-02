using System.Numerics;
using Content.Goobstation.Common.Tools;
using Content.Shared._Maid.Tools;
using Robust.Shared.Map;

namespace Content.Server._Maid.Tools;

/// <summary>
/// Attaches the glow particle to a user when their welder starts welding.
/// <see cref="WeldingGlowParticleSystem"/> takes care of ending it.
/// </summary>
public sealed class WeldingGlowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeldingGlowComponent, UseToolEvent>(OnUseTool);
    }

    private void OnUseTool(Entity<WeldingGlowComponent> ent, ref UseToolEvent args)
    {
        var particle = Spawn(ent.Comp.Prototype, new EntityCoordinates(args.User, Vector2.Zero));
        if (!TryComp<WeldingGlowParticleComponent>(particle, out var comp))
        {
            Log.Error($"Welding glow prototype {ent.Comp.Prototype} of {ToPrettyString(ent)} has no {nameof(WeldingGlowParticleComponent)}");
            QueueDel(particle);
            return;
        }

        comp.User = args.User;
        comp.DoAfterIdx = args.DoAfterIdx;
    }
}
