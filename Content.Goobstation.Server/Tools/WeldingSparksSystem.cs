using Content.Goobstation.Common.Tools;
using Content.Goobstation.Shared.Tools;
using Content.Server.Tools;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Timing; // MAID welding sparks cleanup

namespace Content.Goobstation.Server.Tools;

public sealed class WeldingSparksSystem : EntitySystem
{
    [Dependency] private readonly ToolSystem _toolSystem = default!;
    // MAID BEGIN welding sparks cleanup
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCleanup;
    private readonly List<DoAfterId> _toRemove = new();
    // MAID END welding sparks cleanup

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeldingSparksComponent, UseToolEvent>(OnUseTool);
        SubscribeLocalEvent<WeldingSparksComponent, SharedToolSystem.ToolDoAfterEvent>(OnAfterUseTool);
        SubscribeLocalEvent<WeldingSparksComponent, BeforeRangedInteractEvent>(OnBeforeInteract);
        SubscribeLocalEvent<WeldingSparksComponent, ComponentShutdown>(OnShutdown); // MAID welding sparks cleanup
    }

    // MAID BEGIN welding sparks cleanup
    // DoAfter end events can be missed (tool deleted mid-weld, user deleted mid-weld), so periodically
    // drop any sparks whose DoAfter is no longer running.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCleanup)
            return;

        _nextCleanup = _timing.CurTime + CleanupInterval;

        var query = EntityQueryEnumerator<WeldingSparksComponent>();
        while (query.MoveNext(out var comp))
        {
            if (comp.SpawnedEffects.Count == 0)
                continue;

            foreach (var id in comp.SpawnedEffects.Keys)
            {
                if (!_doAfter.IsRunning(id))
                    _toRemove.Add(id);
            }

            foreach (var id in _toRemove)
            {
                if (comp.SpawnedEffects.Remove(id, out var effect))
                    QueueDel(effect);
            }

            _toRemove.Clear();
        }
    }

    private void OnShutdown(Entity<WeldingSparksComponent> ent, ref ComponentShutdown args)
    {
        foreach (var effect in ent.Comp.SpawnedEffects.Values)
            QueueDel(effect);

        ent.Comp.SpawnedEffects.Clear();
    }
    // MAID END welding sparks cleanup

    private void OnUseTool(Entity<WeldingSparksComponent> ent, ref UseToolEvent args)
    {
        if (TryComp<ToolComponent>(ent, out var toolComp))
        {
            _toolSystem.PlayToolSound(ent, toolComp, null, AudioParams.Default.AddVolume(-2f));
        }

        // Get the actual `DoAfterID` using its index, for use as a dictionary key.
        var doAfterId = new DoAfterId(args.User, args.DoAfterIdx);

        var spawnLoc = GetSpawnLoc(ent, args.Target);
        if (spawnLoc is not { } loc)
            return;
            
        SpawnEffect(ent, ref args, doAfterId, loc);
    }

    private void SpawnEffect(Entity<WeldingSparksComponent> ent, ref UseToolEvent args, DoAfterId id, EntityCoordinates spawnLoc)
    {
        var effect = Spawn(ent.Comp.EffectProto, spawnLoc);
        ent.Comp.SpawnedEffects.Add(id, effect);

        if (args.Target is { } target)
        {
            RaiseNetworkEvent(new SpawnedWeldingSparksEvent(GetNetEntity(target), GetNetEntity(effect), args.DoAfterLength));
        }
    }

    private EntityCoordinates? GetSpawnLoc(Entity<WeldingSparksComponent> ent, EntityUid? target)
    {
        // If there's a `target` (other than the parent tool), go with that.
        if (target is not null && target != ent.Owner)
            return Transform(target.Value).Coordinates;

        // Otherwise, try to spawn it on the tile where the player clicked.
        if (ent.Comp.LastClickLocation is { } clickLoc && clickLoc.IsValid(EntityManager))
            return clickLoc.SnapToGrid(EntityManager);

        Log.Error("Attempted to spawn weld sparks without a valid spawn location");
        return null;
    }

    // After the tool's DoAfter finishes or is cancelled, remove the effect associated with it.
    private void OnAfterUseTool(Entity<WeldingSparksComponent> ent, ref SharedToolSystem.ToolDoAfterEvent args)
    {
        if (!ent.Comp.SpawnedEffects.TryGetValue(args.DoAfter.Id, out var effect))
            return;

        QueueDel(effect);
        ent.Comp.SpawnedEffects.Remove(args.DoAfter.Id);
    }

    // This is a pretty hacky way of putting the spark effect in the right spot when welding a floor tile, since that doesn't pass a `target` arg.
    private void OnBeforeInteract(Entity<WeldingSparksComponent> ent, ref BeforeRangedInteractEvent args)
    {
        if (args.CanReach) // `clickLoc.IsValid()` is checked later in `GetSpawnLoc()`.
            ent.Comp.LastClickLocation = args.ClickLocation;
    }
}
