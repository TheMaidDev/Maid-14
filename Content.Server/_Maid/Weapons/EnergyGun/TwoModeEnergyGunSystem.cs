using Content.Server.Power.Components;
using Content.Shared._Maid.Weapons.EnergyGun;

namespace Content.Server._Maid.Weapons.EnergyGun;

public sealed class TwoModeEnergyGunSystem : SharedTwoModeEnergyGunSystem
{
    protected override void TakeExtraCharge(EntityUid uid, float charge)
    {
        var ev = new ChangeChargeEvent(-charge);
        RaiseLocalEvent(uid, ref ev);
    }
}
