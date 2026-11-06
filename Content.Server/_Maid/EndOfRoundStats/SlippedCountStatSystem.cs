using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._Maid.CVars;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Slippery;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Maid.EndOfRoundStats;

/// <summary>
/// Counts every slip during the round and shows the total and the clumsiest one in the round end summary.
/// Ported from the old WhiteDream build.
/// </summary>
public sealed class SlippedCountStatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;


    private readonly Dictionary<EntityUid, SlipperData> _slips = new();

    private readonly record struct SlipperData(string Name, string? Username, int Count);

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast: the directed SlipperyComponent subscription is taken by the slipping integration test.
        SubscribeLocalEvent<SlipEvent>(OnSlip);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnSlip(ref SlipEvent args)
    {
        var slipped = args.Slipped;
        _slips.TryGetValue(slipped, out var data);

        _slips[slipped] = new SlipperData(
            Name(slipped),
            GetUsername(slipped) ?? data.Username,
            data.Count + 1);
    }

    private string? GetUsername(EntityUid uid)
    {
        if (TryComp<ActorComponent>(uid, out var actor))
            return actor.PlayerSession.Name;

        if (_mind.TryGetMind(uid, out _, out var mind) &&
            _player.TryGetSessionById(mind.UserId, out var session))
        {
            return session.Name;
        }

        return null;
    }

    private void OnRoundEnd(RoundEndTextAppendEvent ev)
    {
        var total = _slips.Values.Sum(data => data.Count);

        string line;
        if (total < _config.GetCVar(MaidCVars.SlippedCountThreshold))
        {
            if (total != 0 || !_config.GetCVar(MaidCVars.SlippedCountDisplayNone))
                return;

            line = Loc.GetString("eorstats-slippedcount-none");
        }
        else
        {
            line = Loc.GetString("eorstats-slippedcount-totalslips", ("timesSlipped", total));

            if (_config.GetCVar(MaidCVars.SlippedCountTopSlipper) && _slips.Count > 0)
            {
                var top = _slips.Values.MaxBy(data => data.Count);
                line += "\n" + (top.Username != null
                    ? Loc.GetString("eorstats-slippedcount-topslipper-hasusername",
                        ("username", top.Username),
                        ("name", top.Name),
                        ("slipcount", top.Count))
                    : Loc.GetString("eorstats-slippedcount-topslipper-hasnousername",
                        ("name", top.Name),
                        ("slipcount", top.Count)));
            }
        }

        ev.AddLine("\n[color=springGreen]" + line + "[/color]");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _slips.Clear();
    }
}
