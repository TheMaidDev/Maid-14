using System.Collections.Immutable;
using System.Linq;
using System.Net;
using Content.Server.Database;
using Content.Shared.Database;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Maid;

[TestFixture]
public sealed class HwidAccountsTest
{
    [Test]
    public async Task FindsOtherAccountsWithSameModernHwid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        var shared = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var other = ImmutableArray.Create<byte>(9, 9, 9, 9);
        var sharedHwid = new ImmutableTypedHwid(shared, HwidType.Modern);
        var otherHwid = new ImmutableTypedHwid(other, HwidType.Modern);
        var addr = IPAddress.Loopback;

        var a = new NetUserId(Guid.NewGuid());
        var b = new NetUserId(Guid.NewGuid());
        var c = new NetUserId(Guid.NewGuid());

        await db.UpdatePlayerRecordAsync(a, "AltA", addr, sharedHwid);
        await db.UpdatePlayerRecordAsync(b, "AltB", addr, sharedHwid);
        await db.UpdatePlayerRecordAsync(c, "Other", addr, otherHwid);
        await db.AddConnectionLogAsync(a, "AltA", addr, sharedHwid, 0.5f, null, 0);
        await db.AddConnectionLogAsync(b, "AltB", addr, sharedHwid, 0.5f, null, 0);
        await db.AddConnectionLogAsync(c, "Other", addr, otherHwid, 0.5f, null, 0);

        var forB = await db.GetAccountsByModernHwidsAsync(b, [shared]);
        Assert.That(forB.Select(m => m.UserId), Is.EquivalentTo(new[] { a }));
        Assert.That(forB[0].UserName, Is.EqualTo("AltA"));

        // Any of several reported HWIDs may match.
        var forC = await db.GetAccountsByModernHwidsAsync(c, [other, shared]);
        Assert.That(forC.Select(m => m.UserId), Is.EquivalentTo(new[] { a, b }));

        var none = await db.GetAccountsByModernHwidsAsync(c, [other]);
        Assert.That(none, Is.Empty);

        await pair.CleanReturnAsync();
    }
}
