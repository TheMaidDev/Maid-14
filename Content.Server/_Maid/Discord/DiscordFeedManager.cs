using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Discord;
using Content.Shared._Maid.CVars;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Maid.Discord;

public sealed class DiscordFeedManager : IDiscordFeedManager
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly DiscordWebhook _discord = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IBaseServer _server = default!;

    // Discord allows 2000 chars per message; keep a margin.
    private const int MaxLength = 1900;
    // A player record created within this window means the player is new.
    private static readonly TimeSpan NewPlayerWindow = TimeSpan.FromMinutes(2);
    // Pause between sends, keeps us below the ~30 requests/minute webhook limit.
    private static readonly TimeSpan SendDelay = TimeSpan.FromSeconds(2);

    private readonly Dictionary<DiscordFeed, Channel<string>> _queues = new();  // filled in Initialize only, read-only afterwards
    private readonly ConcurrentDictionary<DiscordFeed, WebhookIdentifier?> _identifiers = new();
    private readonly ConcurrentDictionary<NetUserId, byte> _announced = new();
    private ISawmill _sawmill = default!;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("discord.feed");

        Register(DiscordFeed.AdminChat, MaidCVars.DiscordAdminChatWebhook);
        Register(DiscordFeed.NewPlayer, MaidCVars.DiscordNewPlayerWebhook);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void Register(DiscordFeed feed, CVarDef<string> cvar)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
        {
            // Drop the oldest messages instead of growing without limit when Discord is slow.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _queues[feed] = channel;
        _identifiers[feed] = null;

        // Resolve the webhook id/token once per CVar change instead of on every message.
        _cfg.OnValueChanged(cvar, url => _ = ResolveIdentifier(feed, url), true);

        _ = Task.Run(() => Pump(feed, channel.Reader));
    }

    private async Task ResolveIdentifier(DiscordFeed feed, string url)
    {
        _identifiers[feed] = null;

        if (string.IsNullOrWhiteSpace(url))
            return;

        var data = await _discord.GetWebhook(url);
        if (data == null)
        {
            _sawmill.Warning($"Could not resolve the Discord webhook for feed {feed}, it will stay disabled.");
            return;
        }

        _identifiers[feed] = data.Value.ToIdentifier();
    }

    public void Send(DiscordFeed feed, string content)
    {
        // Feed disabled: don't even queue.
        if (_identifiers.GetValueOrDefault(feed) == null)
            return;

        if (content.Length > MaxLength)
            content = content[..MaxLength];

        _queues[feed].Writer.TryWrite(content);
    }

    private async Task Pump(DiscordFeed feed, ChannelReader<string> reader)
    {
        await foreach (var content in reader.ReadAllAsync())
        {
            try
            {
                if (_identifiers.GetValueOrDefault(feed) is not { } identifier)
                    continue;

                var payload = new WebhookPayload
                {
                    Content = content,
                    // Empty "parse" list: no @everyone, @here, user or role pings.
                    AllowedMentions = new WebhookMentions(),
                };

                var response = await _discord.CreateMessage(identifier, payload);

                if ((int) response.StatusCode == 429)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                    await Task.Delay(wait);
                    continue;
                }
            }
            catch (Exception e)
            {
                _sawmill.Error($"Error while sending Discord feed {feed} message:\n{e}");
            }

            await Task.Delay(SendDelay);
        }
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Connected)
            return;

        try
        {
            if (_identifiers.GetValueOrDefault(DiscordFeed.NewPlayer) == null)
                return;

            // The record is created/updated in ConnectionManager before the session becomes Connected.
            var record = await _db.GetPlayerRecordByUserId(e.Session.UserId);
            if (record == null || DateTimeOffset.UtcNow - record.FirstSeenTime > NewPlayerWindow)
                return;

            // Reconnecting within the window must not announce the player twice.
            if (!_announced.TryAdd(e.Session.UserId, 0))
                return;

            var message =
                $"🆕 **{Sanitize(e.Session.Name)}** впервые зашёл на сервер **{Sanitize(_server.ServerName)}**";

            var altNotice = await GetAltNotice(e.Session);
            if (altNotice != null)
                message += "\n" + altNotice;

            Send(DiscordFeed.NewPlayer, message);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error while processing new player notification:\n{ex}");
        }
    }

    /// <summary>
    ///     Builds the "possible second account" warning, or null if there is nothing to report.
    ///     The data comes from the auth server: it returns a set of modern HWIDs for the connection, and the
    ///     engine documents that a match between two connections means the auth server considers them the same user.
    /// </summary>
    private async Task<string?> GetAltNotice(ICommonSession session)
    {
        var maxMinutes = _cfg.GetCVar(MaidCVars.DiscordNewPlayerAltPlaytimeMinutes);
        if (maxMinutes <= 0)
            return null;

        // No modern HWIDs for unauthenticated/local connections, nothing to compare.
        var userData = session.Channel.UserData;
        if (userData.ModernHWIds.IsDefaultOrEmpty)
            return null;

        // Playtime of this account; for a really new account this is zero, the check keeps the rule explicit.
        var playTimes = await _db.GetPlayTimes(session.UserId.UserId);
        var overall = playTimes.Find(p => p.Tracker == PlayTimeTrackingShared.TrackerOverall)?.TimeSpent ?? TimeSpan.Zero;
        if (overall >= TimeSpan.FromMinutes(maxMinutes))
            return null;

        var matches = await _db.GetAccountsByModernHwidsAsync(session.UserId, userData.ModernHWIds);
        if (matches.Count == 0)
            return null;

        var names = matches
            .Take(5)
            .Select(m => string.IsNullOrEmpty(m.UserName) ? m.UserId.ToString() : Sanitize(m.UserName));
        var more = matches.Count > 5 ? $" и ещё {matches.Count - 5}" : string.Empty;

        return $"⚠️ Возможный второй аккаунт: HWID совпадает с {string.Join(", ", names)}{more}. " +
               $"Наиграно {(int) overall.TotalMinutes} мин (порог {maxMinutes}), trust auth-сервера {userData.Trust:0.00}.";
    }

    public string Sanitize(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("@", "@​")
            .Replace("<", "\\<")
            .Replace("`", "\\`")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("~", "\\~")
            .Replace("|", "\\|")
            .Replace("[", "\\[")
            .Replace("http", "http​");
    }
}
