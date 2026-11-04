namespace Content.Shared._Maid.Stacks;

/// <summary>
///     Makes a stack keep its own entity prototype when it is split, and only merge with stacks of the same prototype.
///     Rated machine parts share a stack type with the basic ones (so machine frames accept them),
///     without this a bluespace matter bin turns into a basic one when it is split or merged.
/// </summary>
[RegisterComponent]
public sealed partial class StrictStackPrototypeComponent : Component;
