using Robust.Shared.GameStates;

namespace Content.Shared._Maid.StationSunShadow;

/// <summary>
/// Marks a grid whose sun shadow is cast by the whole grid silhouette (<c>StationSunShadowOverlay</c>)
/// instead of the short per-wall extrusion of the stock sun shadow overlay.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StationSunMarkerComponent : Component;
