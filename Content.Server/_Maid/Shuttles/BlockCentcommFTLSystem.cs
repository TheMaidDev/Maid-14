using Content.Server.Antag.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared._Maid.Shuttles;

namespace Content.Server._Maid.Shuttles;

public sealed class BlockCentcommFTLSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AntagSelectionComponent, RuleLoadedGridsEvent>(OnAntagRuleLoadedGrids);
        SubscribeLocalEvent<BlockCentcommFTLComponent, RuleLoadedGridsEvent>(OnBlockedRuleLoadedGrids);
    }

    private void OnAntagRuleLoadedGrids(Entity<AntagSelectionComponent> ent, ref RuleLoadedGridsEvent args)
    {
        BlockGrids(args.Grids);
    }

    private void OnBlockedRuleLoadedGrids(Entity<BlockCentcommFTLComponent> ent, ref RuleLoadedGridsEvent args)
    {
        BlockGrids(args.Grids);
    }

    private void BlockGrids(IReadOnlyList<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            EnsureComp<BlockCentcommFTLComponent>(grid);
        }
    }
}
