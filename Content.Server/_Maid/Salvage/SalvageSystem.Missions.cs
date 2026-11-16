using System.Linq;
using Content.Server._Maid.Salvage;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Maid.CVars;
using Content.Shared._Maid.Salvage;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Salvage;

/// <summary>
/// Expedition difficulties, objectives, failures and rewards, returned from the pre-rework expeditions.
/// </summary>
public sealed partial class SalvageSystem
{
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private static readonly ProtoId<CargoAccountPrototype> RewardAccount = "Cargo";

    private float _failedCooldown;

    private void InitializeMissions()
    {
        SubscribeLocalEvent<SalvageExpeditionTargetComponent, ExaminedEvent>(OnTargetExamined);

        Subs.CVar(_configurationManager, MaidCVars.SalvageExpeditionFailedCooldown, value => _failedCooldown = value, true);
    }

    private void OnTargetExamined(EntityUid uid, SalvageExpeditionTargetComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("salvage-expedition-structure-examine"));
    }

    private void GenerateMissionsWithObjectives(SalvageExpeditionDataComponent component)
    {
        component.Missions.Clear();

        var difficulties = _prototypeManager.EnumeratePrototypes<SalvageDifficultyPrototype>().ToList();

        if (difficulties.Count == 0)
            return;

        difficulties.Sort(CompareDifficulties);
        _random.Shuffle(difficulties);

        var picked = difficulties.Take(MissionLimit).ToList();

        while (picked.Count < MissionLimit)
        {
            picked.Add(_random.Pick(difficulties));
        }

        picked.Sort(CompareDifficulties);

        var types = Enum.GetValues<SalvageMissionType>();
        var available = new List<SalvageMissionType>(types.Length);

        foreach (var difficulty in picked)
        {
            var seed = _random.Next();
            var mission = GetMission(difficulty, seed);

            available.Clear();

            foreach (var type in types)
            {
                if (TryGetObjectivePrototype(type, mission.Faction, out _))
                    available.Add(type);
            }

            component.Missions[component.NextIndex] = new SalvageMissionParams
            {
                Index = component.NextIndex,
                Seed = seed,
                Difficulty = difficulty.ID,
                MissionType = available.Count > 0 ? _random.Pick(available) : SalvageMissionType.Destruction,
            };

            component.NextIndex++;
        }
    }

    private static int CompareDifficulties(SalvageDifficultyPrototype x, SalvageDifficultyPrototype y)
    {
        var budget = x.ModifierBudget.CompareTo(y.ModifierBudget);
        return budget != 0 ? budget : string.CompareOrdinal(x.ID, y.ID);
    }

    private void FinishMission(Entity<SalvageExpeditionDataComponent> data, EntityUid uid)
    {
        var now = _timing.CurTime;

        if (!TryComp<SalvageExpeditionComponent>(uid, out var expedition) ||
            !HasComp<SalvageExpeditionObjectiveComponent>(uid))
        {
            data.Comp.NextOffer = now + TimeSpan.FromSeconds(_cooldown);
            Announce(uid, Loc.GetString("salvage-expedition-completed"));
            return;
        }

        if (expedition.Completed)
        {
            Log.Debug($"Completed mission {expedition.MissionParams.MissionType} with seed {expedition.MissionParams.Seed}");
            data.Comp.NextOffer = now + TimeSpan.FromSeconds(_cooldown);
            Announce(uid, Loc.GetString("salvage-expedition-mission-completed"));
            GiveRewards(expedition);
        }
        else
        {
            Log.Debug($"Failed mission {expedition.MissionParams.MissionType} with seed {expedition.MissionParams.Seed}");
            data.Comp.NextOffer = now + TimeSpan.FromSeconds(_failedCooldown);
            Announce(uid, Loc.GetString("salvage-expedition-mission-failed"));
        }
    }

    private void GiveRewards(SalvageExpeditionComponent expedition)
    {
        var station = expedition.Station;

        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby || TerminatingOrDeleted(station))
            return;

        if (!TryComp<StationCargoOrderDatabaseComponent>(station, out var cargoDb) ||
            !TryComp<StationDataComponent>(station, out var stationData) ||
            !_prototypeManager.TryIndex<SalvageDifficultyPrototype>(expedition.MissionParams.Difficulty, out var difficulty))
        {
            return;
        }

        var mission = GetMission(difficulty, expedition.MissionParams.Seed);
        var sender = Loc.GetString("cargo-gift-default-sender");
        var description = Loc.GetString("salvage-expedition-reward-description");
        var dest = Loc.GetString("cargo-gift-default-dest");

        foreach (var reward in mission.Rewards)
        {
            if (!_prototypeManager.HasIndex<EntityPrototype>(reward))
                continue;

            _cargo.AddAndApproveOrder(
                station,
                reward,
                GetRewardName(reward),
                0,
                1,
                sender,
                description,
                dest,
                cargoDb,
                RewardAccount,
                (station, stationData));
        }
    }

    private void UpdateMissions()
    {
        var query = EntityQueryEnumerator<SalvageExpeditionObjectiveComponent, SalvageExpeditionComponent>();

        while (query.MoveNext(out var uid, out var objective, out var expedition))
        {
            if (expedition.Completed)
                continue;

            var removed = false;

            for (var i = objective.Targets.Count - 1; i >= 0; i--)
            {
                var target = objective.Targets[i];

                if (!TerminatingOrDeleted(target) && !_mobState.IsDead(target))
                    continue;

                objective.Targets.RemoveAt(i);
                removed = true;
            }

            if (objective.Targets.Count == 0)
            {
                expedition.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-objective-completed"));
                continue;
            }

            if (!removed)
                continue;

            var message = objective.MissionType == SalvageMissionType.Elimination
                ? "salvage-expedition-megafauna-remaining"
                : "salvage-expedition-structure-remaining";

            Announce(uid, Loc.GetString(message, ("count", objective.Targets.Count)));
        }
    }
}
