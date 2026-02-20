using Robust.Shared.GameStates;

namespace Content.Server._Maid.Animal;

[RegisterComponent, NetworkedComponent]
public sealed partial class AnimalDraggableComponent : Component
{
    [DataField]
    public float MaxDragWeight = 50f;

    [DataField]
    public EntityUid? DraggingEntity;

    [DataField]
    public float DragSpeedMultiplier = 0.7f;

    [DataField]
    public bool CanDragMobs = true;

    [DataField]
    public bool RequiresEffort = true;

    [DataField]
    public float StaminaDrainPerSecond = 5f;
}