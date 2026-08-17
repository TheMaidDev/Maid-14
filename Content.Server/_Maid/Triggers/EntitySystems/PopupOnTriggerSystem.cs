using Content.Server.Explosion.EntitySystems;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Content.Shared.Trigger;

namespace Content.Server._Maid.Triggers.EntitySystems;

public sealed class PopupOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Components.PopupOnTriggerComponent, TriggerEvent>(OnPopupTrigger);
    }

    private void OnPopupTrigger(EntityUid uid, Components.PopupOnTriggerComponent component, ref TriggerEvent args)
    {
        if (!TryComp(uid, out SubdermalImplantComponent? implant) || implant.ImplantedEntity is null)
            return;

        _popup.PopupEntity(Loc.GetString(component.Popup), implant.ImplantedEntity.Value, component.PopupType);
        args.Handled = true;
    }
}
