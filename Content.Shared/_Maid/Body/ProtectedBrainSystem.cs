using Content.Shared.Nutrition;

namespace Content.Shared._Maid.Body;

public sealed class ProtectedBrainSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProtectedBrainComponent, IngestibleEvent>(OnIngestible);
    }

    private void OnIngestible(Entity<ProtectedBrainComponent> ent, ref IngestibleEvent args)
    {
        args.Cancelled = true;
    }
}
