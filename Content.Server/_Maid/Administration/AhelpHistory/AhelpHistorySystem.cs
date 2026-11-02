using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._Maid.Administration.AhelpHistory;
using Content.Shared._Maid.CVars;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Maid.Administration.AhelpHistory;

public sealed class AhelpHistorySystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const int MaxSenderNameLength = 256;

    private const int MaxMessageLength = 4096;

    private const int DefaultLimit = 200;

    private const int MaxLimit = 500;

    private const double MinRequestIntervalSeconds = 0.5;

    private bool _enabled;
    private readonly Dictionary<NetUserId, float> _lastRequests = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("ahelp.history");

        Subs.CVar(_cfg, MaidCVars.AhelpHistory, v => _enabled = v, true);
        SubscribeNetworkEvent<AhelpHistoryRequest>(OnHistoryRequest);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _lastRequests.Remove(e.Session.UserId);
    }

    public void OpenFor(ICommonSession player, NetUserId target, string targetName)
    {
        RaiseNetworkEvent(new OpenAhelpHistory
        {
            Target = target,
            TargetName = targetName,
        },
        player.Channel);
    }

    public void Record(
        NetUserId player,
        NetUserId sender,
        string senderName,
        string text,
        bool senderIsAdmin,
        bool adminOnly,
        bool playSound,
        bool fromWebhook)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text))
            return;

        if (senderName.Length > MaxSenderNameLength)
            senderName = senderName[..MaxSenderNameLength];

        if (text.Length > MaxMessageLength)
            text = text[..MaxMessageLength];

        var roundId = _gameTicker.RoundId;

        var message = new AhelpMessage
        {
            RoundId = roundId > 0 ? roundId : null,
            PlayerUserId = player.UserId,
            SenderUserId = sender.UserId,
            SenderName = senderName,
            SentAt = DateTime.UtcNow,
            Message = text,
            SenderIsAdmin = senderIsAdmin,
            AdminOnly = adminOnly,
            PlaySound = playSound,
            FromWebhook = fromWebhook,
        };

        Save(message);
    }

    private async void Save(AhelpMessage message)
    {
        try
        {
            await _db.AddAhelpMessageAsync(message);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to save ahelp message for {message.PlayerUserId}: {e}");
        }
    }

    private async void OnHistoryRequest(AhelpHistoryRequest request, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } session || session.Status == SessionStatus.Disconnected)
            return;

        if (!_admins.HasAdminFlag(session, AdminFlags.Adminhelp))
        {
            _sawmill.Warning($"Session {session.UserId} requested ahelp history without permissions.");
            return;
        }

        var now = (float) _timing.RealTime.TotalSeconds;
        if (_lastRequests.TryGetValue(session.UserId, out var last) && now - last < MinRequestIntervalSeconds)
        {
            RespondError(session, request.Target, "rate-limited");
            return;
        }

        _lastRequests[session.UserId] = now;

        AhelpHistoryResponse response;
        try
        {
            response = await BuildResponse(request);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to load ahelp history for {request.Target}: {e}");
            RespondError(session, request.Target, "error");
            return;
        }

        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(response, session.Channel);
    }

    private async Task<AhelpHistoryResponse> BuildResponse(AhelpHistoryRequest request)
    {
        var located = await _locator.LookupIdAsync(request.Target);
        var targetName = located?.Username ?? request.Target.UserId.ToString();

        var limit = Math.Clamp(request.Limit < 1 ? DefaultLimit : request.Limit, 1, MaxLimit);
        var skip = Math.Max(0, request.Skip);

        var filter = request.AnchorId is { }
            ? new AhelpHistoryFilter(request.Target, null, null, null, null)
            : new AhelpHistoryFilter(
                request.Target,
                request.RoundId,
                request.From,
                request.To,
                string.IsNullOrWhiteSpace(request.Text) ? null : request.Text);

        int anchorSkip = 0;
        if (request.AnchorId is { } anchorId)
        {
            var anchorPos = await _db.GetAhelpHistorySkipAsync(request.Target, anchorId);

            anchorSkip = Math.Max(0, anchorPos - (limit - 1));
            skip = anchorSkip;
        }

        var messages = await _db.GetAhelpHistoryAsync(filter, limit + 1, skip);
        var hasMore = messages.Count > limit;
        if (hasMore)
            messages = messages.Take(limit).ToList();

        var roundIds = messages
            .Where(m => m.RoundId is { } id)
            .Select(m => m.RoundId!.Value)
            .Distinct()
            .ToArray();
        var rounds = roundIds.Length == 0
            ? new Dictionary<int, AhelpRoundInfo>()
            : (await _db.GetAhelpRoundInfosAsync(roundIds))
                .ToDictionary(r => r.Id);

        var entries = messages
            .Select(m =>
            {
                var entry = ToEntry(m);
                if (m.RoundId is { } roundId && rounds.TryGetValue(roundId, out var round))
                {
                    entry.RoundServerName = round.ServerName;
                    entry.RoundStartedAtKnown = round.StartedAt is { };
                    entry.RoundStartedAt = round.StartedAt ?? default;
                }

                return entry;
            })
            .ToList();

        return new AhelpHistoryResponse
        {
            Target = request.Target,
            TargetName = targetName,
            Entries = entries,
            HasMore = hasMore && entries.Count > 0,
            AnchorSkip = anchorSkip,
        };
    }

    private void RespondError(ICommonSession session, NetUserId target, string error)
    {
        RaiseNetworkEvent(new AhelpHistoryResponse
        {
            Target = target,
            Error = error,
        },
        session.Channel);
    }

    private AhelpHistoryEntry ToEntry(AhelpMessage message)
    {
        return new AhelpHistoryEntry
        {
            Id = message.Id,
            SentAt = message.SentAt,
            SenderName = message.SenderName,
            SenderIsAdmin = message.SenderIsAdmin,
            FromWebhook = message.FromWebhook,
            AdminOnly = message.AdminOnly,
            Message = message.Message,
            RoundId = message.RoundId,
        };
    }
}
