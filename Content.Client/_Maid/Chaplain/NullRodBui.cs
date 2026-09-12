using Content.Client._Maid.UserInterface.Radial;
using Content.Shared._Maid.Chaplain;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Maid.Chaplain;

[UsedImplicitly]
public sealed class NullRodBui : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private SpriteSystem _spriteSystem = default!;

    private bool _selected;
    private RadialContainer? _weaponSelector;

    public NullRodBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (!_entityManager.TryGetComponent(Owner, out HolyNullRodComponent? nullRod))
        {
            Close();
            return;
        }

        _spriteSystem = _entityManager.EntitySysManager.GetEntitySystem<SpriteSystem>();

        _weaponSelector = new RadialContainer();

        _weaponSelector.Closed += () =>
        {
            if (_selected)
                return;

            SendMessage(new WeaponSelectedEvent(string.Empty));
            Close();
        };

        foreach (var weapon in nullRod.Weapons)
        {
            if (!_prototypeManager.TryIndex<EntityPrototype>(weapon, out var weaponPrototype))
                continue;

            var button = _weaponSelector.AddButton(
                weaponPrototype.Name,
                _spriteSystem.GetPrototypeIcon(weaponPrototype).Default
            );

            button.Controller.OnPressed += _ =>
            {
                _selected = true;
                SendMessage(new WeaponSelectedEvent(weapon));
                _weaponSelector.Close();
                Close();
            };
        }

        _weaponSelector.OpenAttachedLocalPlayer();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _weaponSelector?.Close();
    }
}
