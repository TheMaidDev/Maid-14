using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Content.Server._Maid.Salvage;
using Content.Shared._Maid.Salvage;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Collections;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Salvage;

/// <summary>
/// Spawns the mission objective, returned from the pre-rework expeditions.
/// </summary>
public sealed partial class SpawnSalvageMissionJob
{
    /// <summary>
    /// Spawns the faction structures or megafauna the mission is about.
    /// </summary>
    private async Task SetupObjective(
        SalvageMission mission,
        SalvageDifficultyPrototype difficulty,
        Entity<MapGridComponent> grid,
        Dungeon dungeon,
        System.Random random)
    {
        var type = _missionParams.MissionType;

        if (!_entManager.System<SharedSalvageSystem>().TryGetObjectivePrototype(type, mission.Faction, out var proto))
        {
            _sawmill.Warning($"Faction {mission.Faction} has nothing for mission type {type}, spawning the mission without an objective");
            return;
        }

        var count = type == SalvageMissionType.Destruction ? Math.Max(1, difficulty.StructureCount) : 1;
        var targets = new List<EntityUid>(count);

        for (var i = 0; i < count; i++)
        {
            await SuspendIfOutOfTime();

            if (!TrySpawnInRandomRoom(grid, proto, dungeon, random, out var uid))
                continue;

            _entManager.EnsureComponent<SalvageExpeditionTargetComponent>(uid.Value);
            targets.Add(uid.Value);
        }

        if (targets.Count == 0)
        {
            _sawmill.Warning($"Failed to spawn any {proto} for the mission with seed {_missionParams.Seed}");
            return;
        }

        var objective = _entManager.AddComponent<SalvageExpeditionObjectiveComponent>(grid);
        objective.MissionType = type;
        objective.Targets.AddRange(targets);
    }

    private bool TrySpawnInRandomRoom(
        Entity<MapGridComponent> grid,
        EntProtoId proto,
        Dungeon dungeon,
        System.Random random,
        [NotNullWhen(true)] out EntityUid? uid)
    {
        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        while (availableRooms.Count > 0)
        {
            availableTiles.Clear();
            var room = availableRooms.RemoveSwap(random.Next(availableRooms.Count));
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree(grid, tile, (int) CollisionGroup.MachineLayer,
                        (int) CollisionGroup.MachineLayer))
                {
                    continue;
                }

                uid = _entManager.SpawnAtPosition(proto, _map.GridTileToLocal(grid, grid, tile));
                return true;
            }
        }

        uid = null;
        return false;
    }
}
