using Robust.Shared.Serialization;

namespace Content.Shared._Maid.Salvage;

/// <summary>
/// Objective of a salvage expedition, as it was before the upstream expeditions rework.
/// </summary>
[Serializable, NetSerializable]
public enum SalvageMissionType : byte
{
    /// <summary>
    /// Destroy the faction structures inside the dungeon.
    /// </summary>
    Destruction,

    /// <summary>
    /// Kill the faction megafauna inside the dungeon.
    /// </summary>
    Elimination,
}
