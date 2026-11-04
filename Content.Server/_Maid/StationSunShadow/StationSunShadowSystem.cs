using Content.Server.Station.Systems;
using Content.Shared._Maid.StationSunShadow;
using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Maid.StationSunShadow;

/// <summary>
/// Sets a station grid up for the global star light: a constant <see cref="SunShadowComponent"/> and a marker.
/// All lighting is computed on the client, see Content.Client._Maid.Lighting.StationSunShadowOverlay.
/// </summary>
public sealed class StationSunShadowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationSunShadowComponent, StationGridAddedEvent>(OnGridAdded);
    }

    private void OnGridAdded(EntityUid uid, StationSunShadowComponent comp, StationGridAddedEvent args)
    {
        var grid = args.GridId;

        var shadow = EnsureComp<SunShadowComponent>(grid);
        shadow.Direction = comp.Direction;
        shadow.Alpha = comp.Alpha;
        Dirty(grid, shadow);

        EnsureComp<StationSunMarkerComponent>(grid);

        var mapUid = Transform(grid).MapUid;
        if (mapUid != null)
        {
            var light = EnsureComp<MapLightComponent>(mapUid.Value);
            light.AmbientLightColor = comp.SunColor;
            Dirty(mapUid.Value, light);
        }
    }
}
