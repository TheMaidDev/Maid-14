using Content.Server.Bed.Cryostorage.Events;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Nuke;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Server.Nuke;

public sealed class NukeDiskCryostorageSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CryostorageBodyRemovedEvent>(OnCryostorageBodyRemoved);
    }

    // Maid edit start
    private void OnCryostorageBodyRemoved(ref CryostorageBodyRemovedEvent args)
    {
        if (args.Station == null)
            return;

        var disk = FindNukeDiskOwnedByPlayer(args.Body);
        if (disk == null)
            return;

        var captainLocker = FindCaptainLocker(args.Station.Value);
        if (captainLocker == null)
            return;

        if (_container.TryGetContainer(captainLocker.Value, "entity_storage", out var container))
        {
            _container.TryRemoveFromContainer(disk.Value);

            if (_container.Insert(disk.Value, container))
                return;
        }

        _container.TryRemoveFromContainer(disk.Value);
        _transform.SetCoordinates(disk.Value, Transform(captainLocker.Value).Coordinates);
    }

    private EntityUid? FindNukeDiskOwnedByPlayer(EntityUid player)
    {
        var query = EntityQueryEnumerator<NukeDiskComponent, TransformComponent>();

        while (query.MoveNext(out var diskUid, out _, out var xform))
        {
            var current = xform.ParentUid;

            while (current != EntityUid.Invalid)
            {
                if (current == player)
                    return diskUid;

                current = Transform(current).ParentUid;
            }
        }

        return null;
    }

    private EntityUid? FindCaptainLocker(EntityUid station)
    {
        var query = EntityQueryEnumerator<CaptainLockerComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            var owningStation = _station.GetOwningStation(uid);
            if (owningStation == station)
                return uid;
        }

        return null;
    }
    // Maid edit end
}