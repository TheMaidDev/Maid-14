using Robust.Shared.GameStates;

namespace Content.Shared._Maid.Shuttles;

/// <summary>
/// The grid can't FTL to CentComm after the evacuation. On a game rule, marks every grid the rule loads.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlockCentcommFTLComponent : Component;
