// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._White.Explosion;

/// <summary>
/// Marks an entity as the origin of a screen-distorting shockwave ring.
/// Rendered client side by ExplosionShockWaveOverlay for as long as the entity exists,
/// so pair this with a TimedDespawn.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExplosionShockWaveComponent : Component
{
    /// <summary>
    /// The rate at which the wave fades, lower values means it's active for longer.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float FalloffPower = 40f;

    /// <summary>
    /// How sharp the wave distortion is. Higher values make the wave more pronounced.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Sharpness = 10f;

    /// <summary>
    /// Width of the wave.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Width = 0.8f;
}
