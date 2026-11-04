using Content.Shared._Maid.Weapons.EnergyGun;
using Content.Shared.Rounding;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;

namespace Content.Client._Maid.Weapons.EnergyGun;

public sealed class TwoModeEnergyGunSystem : SharedTwoModeEnergyGunSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TwoModeEnergyGunComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    /// <summary>
    /// The charge indicator uses the colour of the current mode and shows how many charges are left.
    /// </summary>
    private void OnAppearanceChange(Entity<TwoModeEnergyGunComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !_sprite.LayerMapTryGet((ent, args.Sprite), TwoModeEnergyGunVisualLayers.Mag, out var layer, false))
        {
            return;
        }

        if (!_appearance.TryGetData<int>(ent, TwoModeEnergyGunVisuals.Mode, out var modeIndex, args.Component))
            modeIndex = ent.Comp.CurrentMode;

        if (modeIndex < 0 || modeIndex >= ent.Comp.Modes.Count)
            return;

        var steps = ent.Comp.MagSteps;
        if (!_appearance.TryGetData<int>(ent, AmmoVisuals.AmmoMax, out var capacity, args.Component))
            capacity = steps;

        if (!_appearance.TryGetData<int>(ent, AmmoVisuals.AmmoCount, out var count, args.Component))
            count = capacity;

        var step = capacity > 0 ? ContentHelpers.RoundToLevels(count, capacity, steps) : 0;
        _sprite.LayerSetRsiState((ent, args.Sprite), layer, $"{ent.Comp.Modes[modeIndex].MagState}-{step}");
    }
}
