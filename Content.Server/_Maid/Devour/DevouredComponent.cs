namespace Content.Server._Maid.Devour;

/// <summary>
/// Added while the entity lies in a devourer's stomach, so the devoured body takes no damage and is not digested.
/// </summary>
[RegisterComponent]
public sealed partial class DevouredComponent : Component;
