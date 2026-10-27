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

namespace Content.Goobstation.Server.Tools;

public sealed class WeldingSparksSystem : EntitySystem
{
    [Dependency] private readonly ToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeldingSparksComponent, UseToolEvent>(OnUseTool);
        SubscribeLocalEvent<WeldingSparksComponent, SharedToolSystem.ToolDoAfterEvent>(OnAfterUseTool);
        SubscribeLocalEvent<WeldingSparksComponent, BeforeRangedInteractEvent>(OnBeforeInteract);
        // MAID BEGIN welding sparks cleanup
        SubscribeLocalEvent<WeldingSparksComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WeldingSparksUserComponent, ComponentShutdown>(OnUserShutdown);
        // MAID END welding sparks cleanup
    }

    // MAID BEGIN welding sparks cleanup
    // DoAfter end events are raised on the tool, so they are lost if the tool or the user is deleted mid-weld.
    // Sparks are tracked on both the tool and the user, and deleted when either goes away.
    private void OnShutdown(Entity<WeldingSparksComponent> ent, ref ComponentShutdown args)
    {
        foreach (var (id, effect) in ent.Comp.SpawnedEffects)
        {
            QueueDel(effect);
            UntrackUserEffect(id.Uid, effect);
        }

        ent.Comp.SpawnedEffects.Clear();
    }

    private void OnUserShutdown(Entity<WeldingSparksUserComponent> ent, ref ComponentShutdown args)
    {
        foreach (var (effect, (tool, id)) in ent.Comp.Effects)
        {
            QueueDel(effect);

            if (TryComp<WeldingSparksComponent>(tool, out var sparks))
                sparks.SpawnedEffects.Remove(id);
        }

        ent.Comp.Effects.Clear();
    }

    private void UntrackUserEffect(EntityUid user, EntityUid effect)
    {
        if (!TryComp<WeldingSparksUserComponent>(user, out var userComp)
            || !userComp.Effects.Remove(effect)
            || userComp.Effects.Count > 0
            || TerminatingOrDeleted(user))
            return;

        RemComp<WeldingSparksUserComponent>(user);
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
        EnsureComp<WeldingSparksUserComponent>(id.Uid).Effects[effect] = (ent.Owner, id); // MAID welding sparks cleanup

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
        UntrackUserEffect(args.DoAfter.Id.Uid, effect); // MAID welding sparks cleanup
    }

    // This is a pretty hacky way of putting the spark effect in the right spot when welding a floor tile, since that doesn't pass a `target` arg.
    private void OnBeforeInteract(Entity<WeldingSparksComponent> ent, ref BeforeRangedInteractEvent args)
    {
        if (args.CanReach) // `clickLoc.IsValid()` is checked later in `GetSpawnLoc()`.
            ent.Comp.LastClickLocation = args.ClickLocation;
    }
}
