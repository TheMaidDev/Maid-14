using Content.Server._Maid.Terminator.Roles;
using Content.Server.Antag;
using Content.Server.Body.Components;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Server.Objectives.Components;
using Content.Server.Roles;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Whitelist;
using Robust.Shared.Random;

namespace Content.Server._Maid.Terminator.GameTicking.Rules;

public sealed class TerminatorRuleSystem : GameRuleSystem<TerminatorRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerminatorRuleComponent, AntagSelectEntityEvent>(OnAntagSelectEntity);
        SubscribeLocalEvent<TerminatorRuleComponent, AfterAntagEntitySelectedEvent>(AfterAntagSelected);
    }

    private void OnAntagSelectEntity(Entity<TerminatorRuleComponent> ent, ref AntagSelectEntityEvent args)
    {
        var allAliveHumanoids = _mind.GetAliveHumans();
        allAliveHumanoids.RemoveWhere(human => _whitelist.IsBlacklistPass(ent.Comp.TargetBlacklist, human));

        if (allAliveHumanoids.Count == 0)
        {
            Log.Warning("Could not find any alive players to create a terminator for!");
            return;
        }

        // pick a random player
        var randomHumanoidMind = _random.Pick(allAliveHumanoids);

        ent.Comp.Target = randomHumanoidMind;
    }

    private void AfterAntagSelected(Entity<TerminatorRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        var targetComp = EnsureComp<TargetOverrideComponent>(args.EntityUid);
        targetComp.Target = ent.Comp.Target;

        RemComp<RespiratorComponent>(args.EntityUid);
    }
}
