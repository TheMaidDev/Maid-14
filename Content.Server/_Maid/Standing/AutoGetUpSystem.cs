using Content.Shared._Maid.CVars;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Maid.Standing;

/// <summary>
///     Disables auto standing after a knockdown for players who turned off <see cref="MaidCVars.AutoGetUp"/>.
/// </summary>
public sealed class AutoGetUpSystem : EntitySystem
{
    [Dependency] private readonly INetConfigurationManager _netConfig = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, KnockDownAttemptEvent>(OnKnockDownAttempt);
    }

    private void OnKnockDownAttempt(Entity<ActorComponent> ent, ref KnockDownAttemptEvent args)
    {
        if (!args.AutoStand)
            return;

        if (!_netConfig.GetClientCVar(ent.Comp.PlayerSession.Channel, MaidCVars.AutoGetUp))
            args.AutoStand = false;
    }
}
