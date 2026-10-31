using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Shared._Maid.Storage;

/// <summary>
///     Blocks toggling an item stored in a storage grid if its toggled size or shape no longer fits there,
///     so it stays as it is instead of overflowing the grid or falling out.
/// </summary>
public sealed class ToggleSizeStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemToggleSizeComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<ItemToggleSizeComponent, ItemToggleDeactivateAttemptEvent>(OnDeactivateAttempt);
    }

    private void OnActivateAttempt(Entity<ItemToggleSizeComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.ActivatedSize == null && ent.Comp.ActivatedShape == null)
            return;

        if (_storage.StoredItemFitsResized(ent, ent.Comp.ActivatedSize, ent.Comp.ActivatedShape))
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString("comp-storage-insufficient-capacity");
    }

    private void OnDeactivateAttempt(Entity<ItemToggleSizeComponent> ent, ref ItemToggleDeactivateAttemptEvent args)
    {
        if (args.Cancelled || ent.Comp.DeactivatedSize == null && ent.Comp.DeactivatedShape == null)
            return;

        if (_storage.StoredItemFitsResized(ent, ent.Comp.DeactivatedSize, ent.Comp.DeactivatedShape))
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString("comp-storage-insufficient-capacity");
    }
}
