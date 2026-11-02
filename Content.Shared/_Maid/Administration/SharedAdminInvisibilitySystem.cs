using Content.Shared.Actions;
using Content.Shared.Examine;

namespace Content.Shared._Maid.Administration;

public abstract class SharedAdminInvisibilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdminInvisibilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AdminInvisibilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AdminInvisibilityComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<AdminInvisibilityComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
    }

    protected virtual void OnShutdown(Entity<AdminInvisibilityComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);
    }

    private void OnExamined(Entity<AdminInvisibilityComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Invisible)
            args.PushMarkup(Loc.GetString("admin-invisibility-examine"));
    }
}
