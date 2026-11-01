using System.Linq;
using Content.Server.Dragon;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Shared._Maid.EntitiesTracker;
using Content.Shared.Dragon;
using Content.Shared.Sprite;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using System.Numerics;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;

namespace Content.Server._Maid.PassiveEntitySpawner;

public sealed class MaidPassiveEntitySpawnerSystem : EntitySystem
{
    [Dependency] private readonly EntitiesTrackerSystem _entitiesTracker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;
    [Dependency] private readonly NPCSystem _npc = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MaidPassiveEntitySpawnerComponent, TrackedEntitiesCountChangedEvent>(OnTrackedCountChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MaidPassiveEntitySpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (TryComp<DragonRiftComponent>(uid, out var rift) && rift.State == DragonRiftState.Finished)
                continue;

            UpdateMultiplier(comp);
            var count = _entitiesTracker.GetCount(uid);
            var currentCooldown = GetCurrentCooldown(comp, count);

            var curTime = _timing.CurTime;
            if (comp.NextSpawnAt == null)
            {
                comp.LastSpawnedAt = curTime;
                comp.NextSpawnAt = curTime + currentCooldown;
            }

            if (curTime >= comp.NextSpawnAt)
            {
                SpawnEntity(uid, comp, xform);
            }
        }
    }

    private void OnTrackedCountChanged(Entity<MaidPassiveEntitySpawnerComponent> ent, ref TrackedEntitiesCountChangedEvent args)
    {
        if (TryComp<DragonRiftComponent>(ent.Owner, out var rift) && rift.State == DragonRiftState.Finished)
            return;

        UpdateMultiplier(ent.Comp);
        var newCooldown = GetCurrentCooldown(ent.Comp, args.NewCount);

        var lastSpawn = ent.Comp.LastSpawnedAt ?? _timing.CurTime;
        var newNextSpawn = lastSpawn + newCooldown;
        ent.Comp.NextSpawnAt = newNextSpawn;

        if (_timing.CurTime >= newNextSpawn)
        {
            SpawnEntity(ent.Owner, ent.Comp, Transform(ent.Owner));
        }
    }

    private void SpawnEntity(EntityUid uid, MaidPassiveEntitySpawnerComponent comp, TransformComponent xform)
    {
        var curTime = _timing.CurTime;
        comp.LastSpawnedAt = curTime;

        var proto = _random.Prob(comp.StrongSpawnChance) && comp.SpawnPrototypeStrong != null
            ? comp.SpawnPrototypeStrong
            : comp.SpawnPrototype;

        if (proto == null && TryComp<DragonRiftComponent>(uid, out var riftProto))
            proto = _random.Prob(riftProto.StrongSpawnChance) ? riftProto.SpawnPrototypeStrong : riftProto.SpawnPrototype;

        if (proto == null)
            return;

        var ent = Spawn(proto, xform.Coordinates);
        _entitiesTracker.TrackEntity(uid, ent);

        if (TryComp<DragonRiftComponent>(uid, out var rift) && rift.Dragon != null)
        {
            if (TryComp<RandomSpriteComponent>(rift.Dragon, out var randomSprite))
            {
                var spawnedSprite = EnsureComp<RandomSpriteComponent>(ent);
                _serManager.CopyTo(randomSprite, ref spawnedSprite, notNullableOverride: true);
                Dirty(ent, spawnedSprite);
            }

            _npc.SetBlackboard(ent, NPCBlackboard.FollowTarget, new EntityCoordinates(rift.Dragon.Value, Vector2.Zero));
        }

        var nextCooldown = GetCurrentCooldown(comp, _entitiesTracker.GetCount(uid));
        comp.NextSpawnAt = curTime + nextCooldown;
    }

    private void UpdateMultiplier(MaidPassiveEntitySpawnerComponent comp)
    {
        var playerCount = _playerManager.Sessions
            .Count(session => session.Status == SessionStatus.InGame);

        if (comp.CachedPlayerCount == playerCount)
            return;

        comp.CachedPlayerCount = playerCount;
        var targetCount = Math.Max(1f, comp.TotalEntityAmountPerPlayer * playerCount);
        comp.SpawnMultiplier = CalculateMultiplier(targetCount, (float) comp.TotalSecondsAmount.TotalSeconds, (float) comp.BaseSpawnCooldown.TotalSeconds);
    }

    private TimeSpan GetCurrentCooldown(MaidPassiveEntitySpawnerComponent comp, int currentCount)
    {
        var baseSeconds = (float) comp.BaseSpawnCooldown.TotalSeconds;
        var cdSeconds = baseSeconds + MathF.Pow(comp.SpawnMultiplier, currentCount) - 1f;
        var maxSeconds = (float) comp.MaxSpawnCooldown.TotalSeconds;
        var clamped = Math.Clamp(cdSeconds, baseSeconds, maxSeconds);
        return TimeSpan.FromSeconds(clamped);
    }

    public static float CalculateMultiplier(float targetCount, float totalTime, float baseCd)
    {
        var n = targetCount;
        if (n <= 1)
            return 1f;

        var s = totalTime - n * (baseCd - 1f);
        if (s <= n)
            return 1f;

        var low = 1f;
        var high = 10f;

        while ((MathF.Pow(high, n) - 1f) / (high - 1f) < s && high < 1000f)
        {
            high *= 2f;
        }

        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) * 0.5f;
            var val = (MathF.Pow(mid, n) - 1f) / (mid - 1f);
            if (val < s)
                low = mid;
            else
                high = mid;
        }

        return (low + high) * 0.5f;
    }
}
