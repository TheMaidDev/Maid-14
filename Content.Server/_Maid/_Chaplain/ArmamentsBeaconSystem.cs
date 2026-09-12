using Content.Shared._Maid.Chaplain;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._Maid.Chaplain;

public sealed class ArmamentsBeaconSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArmamentsBeaconComponent, ArmorSelectedEvent>(OnArmorSelected);
    }

    private void OnArmorSelected(Entity<ArmamentsBeaconComponent> ent, ref ArmorSelectedEvent args)
    {
        var player = args.Actor;
        var index = args.SelectedIndex;

        if (index < 0 || index >= ent.Comp.Armor.Count)
        {
            _sawmill.Warning($"Неверный индекс: {index}, доступно: {ent.Comp.Armor.Count}");
            return;
        }

        _inventorySystem.TryUnequip(player, "outerClothing", true);
        _inventorySystem.SpawnItemInSlot(player, "outerClothing", ent.Comp.Armor[index], silent: true);

        if (index < ent.Comp.Helmets.Count && ent.Comp.Helmets[index] != null)
        {
            var helmet = ent.Comp.Helmets[index]!.Value;
            if (!_prototypeManager.HasIndex<EntityPrototype>(helmet))
            {
                _sawmill.Error($"ПРОТОТИП НЕ НАЙДЕН: {helmet}! Шлем не будет надет.");
                Del(ent);
                return;
            }

            _inventorySystem.TryUnequip(player, "head", true);

            var result = _inventorySystem.SpawnItemInSlot(player, "head", helmet, silent: true);
            if (!result)
            {
                var helmetEntity = Spawn(helmet, Transform(player).Coordinates);
                var equipResult = _inventorySystem.TryEquip(player, helmetEntity, "head", true, true);

                if (equipResult)
                    _sawmill.Debug($"Шлем {helmet} успешно надет через TryEquip");
                else
                    _sawmill.Error($"Не удалось надеть шлем {helmet} ни через SpawnItemInSlot, ни через TryEquip");
            }
        }
        Del(ent);
    }
}
