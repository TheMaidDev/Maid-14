using Content.Server.Maps;
using Robust.Shared.Map.Events;

namespace Content.Server._Maid.Maps;

public sealed class MigratedNoRotSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<string> _migratedTargets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeRead, after: [typeof(MapMigrationSystem)]);
        SubscribeLocalEvent<TransformComponent, MapInitEvent>(OnXformMapInit);
    }

    private void OnBeforeRead(BeforeEntityReadEvent ev)
    {
        _migratedTargets.Clear();
        foreach (var target in ev.RenamedPrototypes.Values)
        {
            _migratedTargets.Add(target);
        }
    }

    private void OnXformMapInit(Entity<TransformComponent> ent, ref MapInitEvent args)
    {
        if (_migratedTargets.Count == 0 || !ent.Comp.NoLocalRotation || ent.Comp.LocalRotation == Angle.Zero)
            return;

        if (MetaData(ent).EntityPrototype is { } proto && _migratedTargets.Contains(proto.ID))
            _transform.SetLocalRotation(ent.Owner, Angle.Zero, ent.Comp);
    }
}
