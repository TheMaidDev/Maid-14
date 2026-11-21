using Content.Shared.Shuttles.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class EmergencyShuttleSystem
{
    private static readonly EntityWhitelist CentcommFtlBlacklist = new()
    {
        Components = new[] { "BlockCentcommFTL" },
    };

    private void OpenCentcommFtl(MapId mapId)
    {
        var mapUid = _mapSystem.GetMapOrInvalid(mapId);

        if (!Exists(mapUid))
            return;

        var destination = EnsureComp<FTLDestinationComponent>(mapUid);
        destination.Blacklist = CentcommFtlBlacklist;
        Dirty(mapUid, destination);

        if (_shuttle.TryAddFTLDestination(mapId, true, false, false, out var ftlComp))
            _shuttle.SetFTLWhitelist((mapUid, ftlComp), null);
    }
}
