using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Telescope;

/// <summary>
///     Lets the holder (or the entity itself) shift the camera towards the cursor while the look up key is active.
///     Ported from the old WD build telescope.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(TelescopeSystem))]
public sealed partial class TelescopeComponent : Component
{
    /// <summary>
    ///     The smaller the divisor, the further the camera goes. Max offset is roughly <c>1 / Divisor</c> tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Divisor = 0.1f;

    /// <summary>
    ///     How fast the camera follows the cursor, as a lerp fraction per 1/60 of a second.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LerpAmount = 0.1f;

    /// <summary>
    ///     Current camera offset. Not networked: the client smooths it locally and reports it to the server for PVS.
    /// </summary>
    [ViewVariables]
    public Vector2 Offset;

    [ViewVariables]
    public EntityUid? LastHolder;
}
