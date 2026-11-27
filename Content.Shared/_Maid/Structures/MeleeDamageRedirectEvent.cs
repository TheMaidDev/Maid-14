using Robust.Shared.GameObjects;

namespace Content.Shared._Maid.Structures;

/// <summary>
/// Райзится на цели мили-атаки до нанесения урона.
/// Позволяет перенаправить удар на другую сущность
/// например, с решётки на защищающее её окно.
/// </summary>
[ByRefEvent]
public record struct MeleeDamageRedirectEvent
{
    public EntityUid? NewTarget;
}
