using Robust.Shared.Network;

namespace Content.Server.Database;

/// <summary>
///     Another account that connected with a modern HWID (as reported by the auth server) matching the checked one.
/// </summary>
public sealed record HwidMatchedAccount(NetUserId UserId, string UserName, DateTimeOffset LastSeen);
