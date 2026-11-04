using Robust.Shared.GameStates;

namespace Content.Shared._Maid.StationSunShadow;

/// <summary>
/// Marks a grid whose sun shadow is cast by the whole grid silhouette (<c>StationSunShadowOverlay</c>)
/// instead of the short per-wall extrusion of the stock sun shadow overlay.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationSunMarkerComponent : Component
{
    /// <summary>
    /// Tiles that only have light-transparent things (grilles, solar panels) on them, so the shadow caster knows
    /// about them even where the entities themselves are outside of the client's PVS range.
    /// Chunk (tile / 8) to a bitmask of the 8x8 tiles in it, bit = x + 8 * y.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> Transparent = new();
}
