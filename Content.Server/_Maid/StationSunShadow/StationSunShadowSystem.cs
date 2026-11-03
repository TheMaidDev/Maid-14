using Content.Server.Station.Systems;
using Content.Shared.Light.Components;

namespace Content.Server._Maid.StationSunShadow;

public sealed class StationSunShadowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationSunShadowComponent, StationGridAddedEvent>(OnGridAdded);
    }

    private void OnGridAdded(EntityUid uid, StationSunShadowComponent comp, StationGridAddedEvent args)
    {
        var shadow = EnsureComp<SunShadowComponent>(args.GridId);
        shadow.Direction = comp.Direction;
        shadow.Alpha = comp.Alpha;
        Dirty(args.GridId, shadow);
    }
}
