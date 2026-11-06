using Robust.Shared.Player;

namespace Content.Server._Maid.Discord;

public enum DiscordFeed
{
    AdminChat,
    NewPlayer,
}

/// <summary>
///     Sends selected in-game events (admin chat, first-time players) to separate Discord channels
///     through webhooks. Each feed has its own webhook URL CVar; an empty URL disables the feed.
/// </summary>
public interface IDiscordFeedManager
{
    void Initialize();

    /// <summary>Queues a message for the feed. Never blocks; safe to call from the game thread.</summary>
    void Send(DiscordFeed feed, string content);

    /// <summary>
    ///     Sends an admin chat message to the admin chat feed as "name (rank) (round N): message".
    ///     The rank is the admin's title, or "Admin" if the admin has none.
    /// </summary>
    void SendAdminChat(ICommonSession player, string message);

    /// <summary>Neutralises pings, markdown links and mentions in player-provided text.</summary>
    string Sanitize(string text);
}
