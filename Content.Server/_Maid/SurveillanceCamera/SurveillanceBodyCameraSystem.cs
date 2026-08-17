using Content.Server.Popups;
using Content.Server.SurveillanceCamera;
using Content.Shared.Examine;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;

namespace Content.Server._Maid.SurveillanceCamera;

public sealed class SurveillanceBodyCameraSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SurveillanceCameraSystem _surveillanceCameras = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurveillanceBodyCameraComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<SurveillanceBodyCameraComponent, ExaminedEvent>(OnExamine);
    }

    private void OnToggled(EntityUid uid, SurveillanceBodyCameraComponent component, ref ItemToggledEvent args)
    {
        if (!TryComp<SurveillanceCameraComponent>(uid, out var surComp))
            return;

        _surveillanceCameras.SetActive(uid, args.Activated, surComp);

        var message = Loc.GetString(args.Activated ? "surveillance-body-camera-on" : "surveillance-body-camera-off",
            ("item", Identity.Entity(uid, EntityManager)));
        _popup.PopupEntity(message, uid, Filter.PvsExcept(uid, entityManager: EntityManager), true);
    }

    private void OnExamine(EntityUid uid, SurveillanceBodyCameraComponent component, ExaminedEvent args)
    {
        if (!TryComp<SurveillanceCameraComponent>(uid, out var surComp))
            return;

        if (!args.IsInDetailsRange)
            return;

        var message = Loc.GetString(surComp.Active ? "surveillance-body-camera-on" : "surveillance-body-camera-off",
            ("item", Identity.Entity(uid, EntityManager)));
        args.PushMarkup(message);
    }
}
