using Content.Client._Maid.UserInterface.Radial;
using Content.Shared._Maid.Chaplain;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Maid.Chaplain;

[UsedImplicitly]
public sealed class ArmamentsBeaconBui : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private SpriteSystem _spriteSystem = default!;

    private bool _selected;
    private RadialContainer? _armorSelector;

    public ArmamentsBeaconBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (!_entityManager.TryGetComponent(Owner, out ArmamentsBeaconComponent? beacon))
        {
            Close();
            return;
        }

        _spriteSystem = _entityManager.EntitySysManager.GetEntitySystem<SpriteSystem>();

        _armorSelector = new RadialContainer();

        _armorSelector.Closed += () =>
        {
            if (_selected)
                return;

            SendMessage(new ArmorSelectedEvent(-1));
            Close();
        };

        for (var i = 0; i < beacon.Armor.Count; i++)
        {
            if (!_prototypeManager.TryIndex<EntityPrototype>(beacon.Armor[i], out var armorPrototype))
                continue;

            var button = _armorSelector.AddButton(
                armorPrototype.Name,
                _spriteSystem.GetPrototypeIcon(armorPrototype).Default
            );

            if (button?.Controller == null)
                continue;

            var index = i;
            button.Controller.OnPressed += _ =>
            {
                _selected = true;
                SendMessage(new ArmorSelectedEvent(index));
                _armorSelector?.Close();
                Close();
            };
        }

        _armorSelector?.OpenAttachedLocalPlayer();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _armorSelector?.Close();
    }
}
