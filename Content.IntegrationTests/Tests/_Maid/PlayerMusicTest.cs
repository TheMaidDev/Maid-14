using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Content.Client._Maid.PlayerMusic;
using Content.Server._Maid.PlayerMusic;
using Content.Shared._Maid.PlayerMusic;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

// The client and the server systems have the same name
using PlayerMusicClientSystem = Content.Client._Maid.PlayerMusic.PlayerMusicSystem;
using PlayerMusicServerSystem = Content.Server._Maid.PlayerMusic.PlayerMusicSystem;

namespace Content.IntegrationTests.Tests._Maid;

/// <summary>
/// Full client-server simulation of the player music flow:
/// a connected player records a song from the game files onto a cassette through the device window,
/// plays it, and nearby clients download, decode and play the same song.
/// </summary>
[TestFixture]
public sealed class PlayerMusicTest
{
    /// <summary>
    /// A short stereo song from the game audio; stereo covers the PlayGlobal fade path too.
    /// </summary>
    private const string SongPath = "/Audio/Ambience/ambimo2.ogg";

    private static object? GetField(object system, string name)
    {
        return system.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(system);
    }

    [Test]
    public async Task PlayerUploadsSongAndPlaysIt()
    {
        // DummyTicker = false: the client joins the round and controls a real mob
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;

        var sEntMan = server.EntMan;
        var sPlayers = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var sUi = server.System<SharedUserInterfaceSystem>();
        var sSlots = server.System<ItemSlotsSystem>();
        var sRes = server.ResolveDependency<IResourceManager>();
        var sSys = server.System<PlayerMusicServerSystem>();
        var cSys = client.System<PlayerMusicClientSystem>();
        var cUi = client.System<SharedUserInterfaceSystem>();

        byte[] songBytes = [];
        await server.WaitPost(() =>
        {
            using var file = sRes.ContentFileRead(new ResPath(SongPath));
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            songBytes = memory.ToArray();
        });

        // Sanity: the song from the game files must pass the same structural check as user uploads
        Assert.That(PlayerMusicOgg.TryAnalyze(songBytes, out _), Is.True, $"{SongPath} must be a valid ogg file");

        EntityUid boombox = default;
        EntityUid cassette = default;
        await server.WaitPost(() =>
        {
            var session = sPlayers.Sessions.Single();
            var coords = sEntMan.GetComponent<TransformComponent>(session.AttachedEntity!.Value).Coordinates;

            cassette = sEntMan.SpawnEntity("MaidBlankCassette", coords);
            boombox = sEntMan.SpawnEntity("MaidBoombox", coords);
            Assert.That(
                sSlots.TryInsert(boombox, PlayerMusicBoomboxComponent.CassetteSlotId, cassette, null),
                Is.True, "cassette insertion failed");
            sUi.OpenUi(boombox, PlayerMusicUiKey.Key, session);
        });

        await pair.RunTicksSync(15);

        var cBoombox = pair.ToClientUid(boombox);
        Assert.That(client.EntMan.EntityExists(cBoombox), Is.True, "client must see the boombox");

        // The player records the song through the open device window
        await client.WaitPost(() => cSys.RequestUpload(cBoombox, "ambience song", songBytes));
        await pair.RunTicksSync(30);

        var songId = string.Empty;
        await server.WaitAssertion(() =>
        {
            var cassetteComp = sEntMan.GetComponent<PlayerMusicCassetteComponent>(cassette);
            Assert.That(cassetteComp.Tracks, Has.Count.EqualTo(1), "the song was not recorded onto the cassette");
            songId = cassetteComp.Tracks[0].Id;

            var songs = (IDictionary) GetField(sSys, "_songs")!;
            Assert.That(songs.Contains(songId), Is.True, "the song data was not stored on the server");
        });

        // The player starts playback through the device window
        await client.WaitAssertion(() =>
        {
            Assert.That(
                cUi.TryGetOpenUi<PlayerMusicBoundUserInterface>(cBoombox, PlayerMusicUiKey.Key, out var bui),
                Is.True, "the device window must be open on the client");
            bui!.SendMessage(new PlayerMusicPlayMessage(0));
        });

        // Nearby client downloads the song in chunks and decodes it; both sides have to tick
        var cache = (ICollection) GetField(cSys, "_cache")!;
        var playing = (ICollection) GetField(cSys, "_playing")!;

        for (var i = 0; i < 30 && cache.Count == 0; i++)
            await pair.RunTicksSync(10);
        Assert.That(cache.Count, Is.EqualTo(1), "the client did not download and decode the song");

        for (var i = 0; i < 30 && playing.Count == 0; i++)
            await pair.RunTicksSync(10);
        Assert.That(playing.Count, Is.EqualTo(1), "the client did not start playing the song");

        await client.WaitAssertion(() =>
        {
            foreach (var value in ((IDictionary) GetField(cSys, "_playing")!).Values)
            {
                var audioUid = (EntityUid) value.GetType().GetProperty("AudioEntity")!.GetValue(value)!;
                Assert.That(client.EntMan.EntityExists(audioUid), Is.True, "the audio entity must exist");
                Assert.That(client.EntMan.HasComponent<AudioComponent>(audioUid), Is.True,
                    "the audio entity must play the song");
            }
        });

        await server.WaitAssertion(() =>
        {
            var boomboxComp = sEntMan.GetComponent<PlayerMusicBoomboxComponent>(boombox);
            Assert.That(boomboxComp.StartedAt, Is.Not.Null, "the server must consider the song playing");
            Assert.That(boomboxComp.SongId, Is.EqualTo(songId));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UploadWithoutUiOrOverLimitsIsDenied()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;

        var sEntMan = server.EntMan;
        var sPlayers = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var sUi = server.System<SharedUserInterfaceSystem>();
        var sSlots = server.System<ItemSlotsSystem>();
        var sRes = server.ResolveDependency<IResourceManager>();
        var sSys = server.System<PlayerMusicServerSystem>();
        var cSys = client.System<PlayerMusicClientSystem>();

        byte[] songBytes = [];
        await server.WaitPost(() =>
        {
            using var file = sRes.ContentFileRead(new ResPath(SongPath));
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            songBytes = memory.ToArray();
        });

        EntityUid boombox = default;
        EntityUid cassette = default;
        await server.WaitPost(() =>
        {
            var session = sPlayers.Sessions.Single();
            var coords = sEntMan.GetComponent<TransformComponent>(session.AttachedEntity!.Value).Coordinates;

            cassette = sEntMan.SpawnEntity("MaidBlankCassette", coords);
            boombox = sEntMan.SpawnEntity("MaidBoombox", coords);
            sSlots.TryInsert(boombox, PlayerMusicBoomboxComponent.CassetteSlotId, cassette, null);
        });

        await pair.RunTicksSync(15);
        var cBoombox = pair.ToClientUid(boombox);

        // No window open: a raw-event recording must not land on the cassette
        await client.WaitPost(() => cSys.RequestUpload(cBoombox, "no ui", songBytes));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(sEntMan.GetComponent<PlayerMusicCassetteComponent>(cassette).Tracks, Has.Count.EqualTo(0),
                "recording without the device window must be denied");
        });

        // The window is open now, but the file is bigger than max_song_bytes
        await server.WaitPost(() => sUi.OpenUi(boombox, PlayerMusicUiKey.Key, sPlayers.Sessions.Single()));
        await pair.RunTicksSync(10);

        // Wait out the upload start throttle so this attempt really reaches the size check
        await pair.RunTicksSync(310);

        var maxBytes = server.CfgMan.GetCVar(PlayerMusicCVars.MaxSongBytes);
        var oversized = new byte[maxBytes + 1024];
        await client.WaitPost(() => cSys.RequestUpload(cBoombox, "too big", oversized));
        await pair.RunTicksSync(30);

        // A disabled feature denies uploads too
        await server.WaitPost(() => server.CfgMan.SetCVar(PlayerMusicCVars.Enabled, false));
        await pair.RunTicksSync(310);
        await client.WaitPost(() => cSys.RequestUpload(cBoombox, "disabled", songBytes));
        await pair.RunTicksSync(30);
        await server.WaitPost(() => server.CfgMan.SetCVar(PlayerMusicCVars.Enabled, true));

        await server.WaitAssertion(() =>
        {
            Assert.That(sEntMan.GetComponent<PlayerMusicCassetteComponent>(cassette).Tracks, Has.Count.EqualTo(0),
                "oversized and disabled uploads must be denied");

            var songs = (ICollection) GetField(sSys, "_songs")!;
            Assert.That(songs.Count, Is.EqualTo(0), "no song data must be stored");
            Assert.That(GetField(sSys, "_pendingBytes"), Is.EqualTo(0L), "no half-finished upload must keep memory");
            Assert.That(((ICollection) GetField(sSys, "_pending")!).Count, Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }
}
