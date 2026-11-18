using Content.Server.Antag.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared._Maid.Shuttles;
using Robust.Server.Physics;

namespace Content.Server._Maid.Shuttles;

public sealed class BlockCentcommFTLSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AntagSelectionComponent, RuleLoadedGridsEvent>(OnAntagRuleLoadedGrids);
        SubscribeLocalEvent<BlockCentcommFTLComponent, RuleLoadedGridsEvent>(OnBlockedRuleLoadedGrids);
        SubscribeLocalEvent<BlockCentcommFTLComponent, GridSplitEvent>(OnGridSplit);
    }

    private void OnAntagRuleLoadedGrids(Entity<AntagSelectionComponent> ent, ref RuleLoadedGridsEvent args)
    {
        BlockGrids(args.Grids);
    }

    private void OnBlockedRuleLoadedGrids(Entity<BlockCentcommFTLComponent> ent, ref RuleLoadedGridsEvent args)
    {
        BlockGrids(args.Grids);
    }

    private void OnGridSplit(Entity<BlockCentcommFTLComponent> ent, ref GridSplitEvent args)
    {
        BlockGrids(args.NewGrids);
    }

    private void BlockGrids(IReadOnlyList<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            EnsureComp<BlockCentcommFTLComponent>(grid);
        }
    }
}
