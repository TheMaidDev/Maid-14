using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Client._Maid.Audio.PathSound;
using Content.Client.Doors;
using Content.IntegrationTests.Pair;
using Content.Shared._Maid.CVars;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;

// The client and the server door systems have the same name
using ClientDoorSystem = Content.Client.Doors.DoorSystem;
using ServerDoorSystem = Content.Server.Doors.Systems.DoorSystem;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Configuration;
using Robust.UnitTesting;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._Maid;

/// <summary>
/// Hard contract of the sound-path model: for every scenario the folded filter occlusion is asserted
/// against analytically expected values, so "audible" and "silent" are exact, testable statements.
/// </summary>
[TestFixture]
[TestOf(typeof(PathSoundSystem))]
public sealed class PathSoundTest
{
    /// <summary>Broadband attenuation of the engine lowpass per occlusion unit: 20/ln(10) / 10 = 0.8686 dB.</summary>
    private const float OcclDbPerUnit = 0.8686f;

    /// <summary>Muffle percent of the shipped Airlock prototype.</summary>
    private const float AirlockMuffle = 90f;

    /// <summary>Muffle percent of the shipped GlassAirlock prototype.</summary>
    private const float GlassAirlockMuffle = 80f;

    /// <summary>Occlusion units of the shipped ReinforcedWindow prototype.</summary>
    private static float WindowOccl => -MathF.Log(1f - 20f / 100f);

    /// <summary>Occlusion units of one closed airlock on the route.</summary>
    private static float AirlockOccl(float muffle) => -MathF.Log(1f - muffle / 100f);

    /// <summary>Filter values below this are audibly clean; above the upper bounds the sound is dead.</summary>
    private const float AudibleMax = 40f;

    private sealed record Scene(
        TestPair Pair,
        RobustIntegrationTest.ServerIntegrationInstance Server,
        RobustIntegrationTest.ClientIntegrationInstance Client,
        PathSoundSystem System,
        EntityUid Player,
        MapCoordinates Listener,
        EntityUid Grid,
        Dictionary<int, EntityUid> Doors,
        float Fold,
        float PerTile,
        float TileSize);

    /// <summary>Round-joins a client, teleports the player to the test-map corridor start and builds a sealed one-tile-wide corridor east.</summary>
    private static async Task<Scene> SetupScene(int corridorLength)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var cSys = client.System<PathSoundSystem>();

        var map = await pair.CreateTestMap();

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var sEntMan = server.EntMan;
            var sPlayers = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
            player = sPlayers.Sessions.Single().AttachedEntity!.Value;
            sEntMan.GetComponent<TransformComponent>(player).Coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
        });

        // The client's local player physics is client-authoritative: a server-side teleport of the
        // local mob does not move the client's predicted copy, so teleport on the client too.
        var cPlayerEarly = pair.ToClientUid(player);
        await client.WaitPost(() =>
        {
            client.EntMan.GetComponent<TransformComponent>(cPlayerEarly).Coordinates = new EntityCoordinates(map.CGridUid, 0.5f, 0.5f);
        });

        // Seal the route: plating floor with flanking wall rows, so any detour through space costs wall crossings.
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var plating = new Tile(tiles["Plating"].TileId);
            var grid = map.Grid;

            for (var x = -1; x <= corridorLength + 2; x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);

                // Flanking walls seal the corridor against the free space detour: without them the
                // model routes around every blocker through vacuum, which carries no sound.
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, 1.5f));
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, -0.5f));
            }
        });

        await pair.RunTicksSync(30);

        MapCoordinates listener = default;
        var cPlayer = pair.ToClientUid(player);
        await client.WaitAssertion(() =>
        {
            var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
            var transform = client.System<SharedTransformSystem>();
            listener = xform.Coordinates.ToMap(client.EntMan, transform);
        });

        // Sync barrier: the client player must actually be on the corridor start tile and the field
        // must be built around it — a lagging predicted player position would shift the whole field.
        for (var i = 0; i < 120; i++)
        {
            var ok = false;
            await client.WaitAssertion(() =>
            {
                var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
                var local = xform.Coordinates.Position;
                ok = cSys.FieldReady
                    && cSys.FieldSeed == new Vector2i(0, 0)
                    && MathF.Abs(local.X - 0.5f) < 0.1f
                    && MathF.Abs(local.Y - 0.5f) < 0.1f;
            });

            if (ok)
                break;

            await pair.RunTicksSync(5);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(cSys.FieldSeed, Is.EqualTo(new Vector2i(0, 0)),
                $"client field was built around a stale player position: {cSys.FieldSeed}");
            Assert.That(cSys.FieldReady, Is.True, "client field was never built");
        });

        return new Scene(pair, server, client, cSys, player, listener, map.Grid, new Dictionary<int, EntityUid>(),
            1f + cSys.OcclusionLossDb / OcclDbPerUnit, cSys.LossDbPerTile / OcclDbPerUnit, map.Grid.Comp.TileSize);
    }

    /// <summary>A sound born on an airlock tile inside a bank must stay audible: its own tile is exempt and the bank neighbors are not on the direct corridor route. The PredEntrySumW regression leaked a neighbor's muffle here and pushed the filter past 30.</summary>
    [Test]
    public async Task BankOwnSoundStaysAudible()
    {
        var scene = await SetupBankScene();
        await using var pair = scene.Pair;

        await AssertFilterBelow(scene, 3, 1, 12f, "bank own sound at (3,1)");
        await AssertFilterBelow(scene, 5, 1, 15f, "bank own sound at (5,1)");
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Round-43 user scene: the sounding glass airlock at (-3,0) is one closed glass door west of the
    /// listener, and the middle column's open door (-1,1) two tiles south forms a near-clean bypass.
    /// The door's own sound must take the bypass outright (round-38 rule: gate 1 hop, full share), and
    /// a foreign source keeps the round-39 contract: a bypass crossing an open door tile is not "pure",
    /// so the +2-hop side step must not lift the closed door and stays in the muffled blend band.
    /// </summary>
    [Test]
    public async Task GlassColumnBankOpenBypass()
    {
        var (scene, doors) = await SetupColumnBankScene();
        await using var pair = scene.Pair;

        await SetColumnDoorState(scene, doors, (-1, 1), DoorState.Open);

        // The field must catch up with the opened bypass tile; poll a while, then report world vs field.
        var cBypass = pair.ToClientUid(doors[(-1, 1)]);
        for (var i = 0; i < 40; i++)
        {
            var fresh = false;
            await scene.Client.WaitAssertion(() =>
            {
                fresh = scene.System.TryGetTile(new Vector2i(-1, 1), out var s, out _, out _) && s < 0.5f;
            });

            if (fresh)
                break;

            await pair.RunTicksSync(10);
        }

        var sourcePos = scene.Listener.Position + new Vector2(-3f * scene.TileSize, 0f);
        await scene.Client.WaitAssertion(() =>
        {
            scene.System.TryGetTile(new Vector2i(-1, 1), out var sBypass, out var hBypass, out var pBypass);
            scene.System.TryGetTile(new Vector2i(-1, 0), out var sClosed, out var hClosed, out var pClosed);
            scene.System.TryGetTile(new Vector2i(-3, 0), out var sSrc, out var hSrc, out var pSrc);
            var cBypassComp = scene.Client.EntMan.GetComponent<DoorComponent>(cBypass);
            var cBypassPhys = scene.Client.EntMan.GetComponent<PhysicsComponent>(cBypass);
            var probe = $"bypass(-1,1) SumW {sBypass:0.000} hops {hBypass} | closed(-1,0) SumW {sClosed:0.000} hops {hClosed} | src(-3,0) SumW {sSrc:0.000} hops {hSrc} past {pSrc} | dw {scene.System.DetourWeight:0.#} | world: state {cBypassComp.State} collide {cBypassPhys.CanCollide}";

            // The door's own sound: the detour through the open bypass door is strictly better and the
            // door gate accepts +2 extra hops, so every blending root reports a clean route.
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out var occl, out var len, out var past, out var filter, doorSource: true),
                Is.True, $"door own sound: field read failed; {probe}");
            Assert.That(filter, Is.LessThan(12f),
                $"door own sound must take the open-door bypass: occl {occl:0.000} len {len:0.0} past {past:0.0} filter {filter:0.00}; {probe}");

            // A foreign source on the same tile: the two roots hugging the east column (seeds (1,0) and
            // (1,1)) sit on closed glass doors and are excluded, so the west roots blend 50/50 — the
            // northern one keeps the closed (-1,0) door on its short route (the +2-hop bypass through
            // the open door tile is not "pure" and misses the 4-hop gate, round-39 contract), the
            // southern one already routes through the open bypass door in its primary. The source tile
            // is itself the last blocker, so the past-decay never applies and both pasts are zero.
            var glass = AirlockOccl(GlassAirlockMuffle);
            var occlForeign = -MathF.Log((MathF.Exp(-glass) + MathF.Exp(-0.005f)) / 2f);
            var expectedForeign = occlForeign * scene.Fold + 3.5f * scene.PerTile;

            var breaks = new List<PathSoundSystem.RootBreak>();
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out var occlF, out var lenF, out var pastF, out var filterF, breakdown: breaks),
                Is.True, "foreign sound: field read failed");
            var rootDump = string.Join(" | ", breaks.Select(b =>
                $"r{b.Root} w{b.RootWeight:0.00} o1 {b.Occl1:0.000} h1 {b.Hops1} p1 {b.Past1} o2 {b.Occl2:0.000} h2 {b.Hops2} share {b.Share:0.0} f1 {b.F1:0.0} f2 {b.F2:0.0}{(b.Skipped ? " SKIP" : "")}"));
            Assert.That(filterF, Is.EqualTo(expectedForeign).Within(2f),
                $"foreign sound blend: occl {occlF:0.000} len {lenF:0.0} past {pastF:0.0} filter {filterF:0.00} vs expected {expectedForeign:0.00}; {probe}; roots: {rootDump}");
        });

        // Without the bypass every crossing tile is a closed glass door: the door's own sound falls
        // back to the primary route and stays in the muffled band, never clean.
        await SetColumnDoorState(scene, doors, (-1, 1), DoorState.Closed);
        await scene.Client.WaitAssertion(() =>
        {
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out var occlC, out var lenC, out var pastC, out var filterC, doorSource: true),
                Is.True, "shut bypass: field read failed");
            Assert.That(filterC, Is.GreaterThan(20f),
                $"shut bypass must keep the door own sound muffled: occl {occlC:0.000} len {lenC:0.0} past {pastC:0.0} filter {filterC:0.00}");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Bank scene: the north wall row carries a glass-airlock bank at x=2..5 instead of walls; the listener starts mid-corridor south of the bank.</summary>
    private static async Task<Scene> SetupBankScene()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var cSys = client.System<PathSoundSystem>();

        var map = await pair.CreateTestMap();

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var sEntMan = server.EntMan;
            var sPlayers = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
            player = sPlayers.Sessions.Single().AttachedEntity!.Value;
            sEntMan.GetComponent<TransformComponent>(player).Coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
        });

        var cPlayerEarly = pair.ToClientUid(player);
        await client.WaitPost(() =>
        {
            client.EntMan.GetComponent<TransformComponent>(cPlayerEarly).Coordinates = new EntityCoordinates(map.CGridUid, 0.5f, 0.5f);
        });

        var doors = new Dictionary<int, EntityUid>();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var plating = new Tile(tiles["Plating"].TileId);
            var grid = map.Grid;

            for (var x = -1; x <= 16; x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);

                // South wall row is solid; the north row carries a gap at x=2..5 filled by the bank.
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, -0.5f));
                if (x < 2 || x > 5)
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, 1.5f));
            }

            for (var x = 2; x <= 5; x++)
                doors[x] = server.EntMan.SpawnEntity("AirlockGlass", new EntityCoordinates(grid, x + 0.5f, 1.5f));
        });

        await pair.RunTicksSync(30);

        MapCoordinates listener = default;
        var cPlayer = pair.ToClientUid(player);
        await client.WaitAssertion(() =>
        {
            var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
            var transform = client.System<SharedTransformSystem>();
            listener = xform.Coordinates.ToMap(client.EntMan, transform);
        });

        for (var i = 0; i < 120; i++)
        {
            var ok = false;
            await client.WaitAssertion(() =>
            {
                var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
                var local = xform.Coordinates.Position;
                ok = cSys.FieldReady
                    && cSys.FieldSeed == new Vector2i(0, 0)
                    && MathF.Abs(local.X - 0.5f) < 0.1f
                    && MathF.Abs(local.Y - 0.5f) < 0.1f;
            });

            if (ok)
                break;

            await pair.RunTicksSync(5);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(cSys.FieldSeed, Is.EqualTo(new Vector2i(0, 0)),
                $"client field was built around a stale player position: {cSys.FieldSeed}");
            Assert.That(cSys.FieldReady, Is.True, "client field was never built");
        });

        return new Scene(pair, server, client, cSys, player, listener, map.Grid, doors,
            1f + cSys.OcclusionLossDb / OcclDbPerUnit, cSys.LossDbPerTile / OcclDbPerUnit, map.Grid.Comp.TileSize);
    }

    /// <summary>
    /// User-reported scene (round-43 scheme, shifted so the listener tile is (0,0)): a sealed room split
    /// by three floor-to-ceiling glass-airlock columns, the sounding airlock at (-3,0) two tiles west of
    /// the listener behind one closed glass door (-1,0), and the middle column's door (-1,1) open as a
    /// bypass. The columns are sealed against both wall rows, so trimming them to six tiles keeps the
    /// route topology (and every model value) of the full 22-tile report scene. Scheme mapping:
    /// listener (14,1) -> (0,0), source (11,1) -> (-3,0), bypass (13,2) -> (-1,1).
    /// </summary>
    private static async Task<(Scene Scene, Dictionary<(int, int), EntityUid> Doors)> SetupColumnBankScene()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var cSys = client.System<PathSoundSystem>();

        var map = await pair.CreateTestMap();

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var sEntMan = server.EntMan;
            var sPlayers = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
            player = sPlayers.Sessions.Single().AttachedEntity!.Value;
            sEntMan.GetComponent<TransformComponent>(player).Coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
        });

        var cPlayerEarly = pair.ToClientUid(player);
        await client.WaitPost(() =>
        {
            client.EntMan.GetComponent<TransformComponent>(cPlayerEarly).Coordinates = new EntityCoordinates(map.CGridUid, 0.5f, 0.5f);
        });

        var doors = new Dictionary<(int, int), EntityUid>();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var plating = new Tile(tiles["Plating"].TileId);
            var grid = map.Grid;

            // Room interior: x -8..16, y 0..5.
            for (var x = -8; x <= 16; x++)
            {
                for (var y = 0; y <= 5; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);
            }

            // Sealing wall rows: top y=-1, bottom y=6, sides x=-9 and x=17.
            for (var x = -9; x <= 17; x++)
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, -0.5f));
            for (var x = -8; x <= 17; x++)
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, 6.5f));
            for (var y = 0; y <= 6; y++)
            {
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, -8.5f, y + 0.5f));
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 17.5f, y + 0.5f));
            }

            // Three glass-airlock columns at x=-3, -1, +1, floor to ceiling (y 0..5).
            foreach (var x in new[] { -3, -1, 1 })
            {
                for (var y = 0; y <= 5; y++)
                    doors[(x, y)] = server.EntMan.SpawnEntity("AirlockGlass", new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
            }
        });

        await pair.RunTicksSync(30);

        MapCoordinates listener = default;
        var cPlayer = pair.ToClientUid(player);
        await client.WaitAssertion(() =>
        {
            var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
            var transform = client.System<SharedTransformSystem>();
            listener = xform.Coordinates.ToMap(client.EntMan, transform);
        });

        for (var i = 0; i < 120; i++)
        {
            var ok = false;
            await client.WaitAssertion(() =>
            {
                var xform = client.EntMan.GetComponent<TransformComponent>(cPlayer);
                var local = xform.Coordinates.Position;
                ok = cSys.FieldReady
                    && cSys.FieldSeed == new Vector2i(0, 0)
                    && MathF.Abs(local.X - 0.5f) < 0.1f
                    && MathF.Abs(local.Y - 0.5f) < 0.1f;
            });

            if (ok)
                break;

            await pair.RunTicksSync(5);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(cSys.FieldSeed, Is.EqualTo(new Vector2i(0, 0)),
                $"client field was built around a stale player position: {cSys.FieldSeed}");
            Assert.That(cSys.FieldReady, Is.True, "client field was never built");
        });

        return (new Scene(pair, server, client, cSys, player, listener, map.Grid, new Dictionary<int, EntityUid>(),
            1f + cSys.OcclusionLossDb / OcclDbPerUnit, cSys.LossDbPerTile / OcclDbPerUnit, map.Grid.Comp.TileSize), doors);
    }

    /// <summary>Drives a column-bank door into the state on both sides, flipping collision like the real actuation flow does mid-animation: the server value is networked and clobbers a client-only flip.</summary>
    private static async Task SetColumnDoorState(
        Scene scene, Dictionary<(int, int), EntityUid> doors, (int, int) tile, DoorState state)
    {
        var door = doors[tile];
        var cDoor = scene.Pair.ToClientUid(door);
        await scene.Server.WaitPost(() =>
        {
            scene.Server.System<ServerDoorSystem>().SetState(door, state);
            scene.Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>()
                .SetCanCollide(door, state != DoorState.Open);
            if (state == DoorState.Open)
            {
                var doorComp = scene.Server.EntMan.GetComponent<DoorComponent>(door);
                doorComp.NextStateChange = null;
                scene.Server.EntMan.Dirty(door, doorComp);
            }
        });
        await scene.Client.WaitPost(() =>
        {
            scene.Client.System<ClientDoorSystem>().SetState(cDoor, state);
            scene.Client.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>()
                .SetCanCollide(cDoor, state != DoorState.Open);
        });
        await scene.Pair.RunTicksSync(30);

        // Airlocks auto-close a couple seconds after opening; the tests need the state to stick, and
        // the TTL rebuild guarantees the field has caught up with the collision flip either way.
        await scene.Pair.RunSeconds(2.5f);
    }

    /// <summary>Asserts the folded filter of a source at (dx, dy) tiles from the listener stays under the cap, dumping the value on failure.</summary>
    private static async Task AssertFilterBelow(Scene scene, int dx, int dy, float maxFilter, string message)
    {
        var sourcePos = scene.Listener.Position + new Vector2(dx * scene.TileSize, dy * scene.TileSize);
        await scene.Client.WaitAssertion(() =>
        {
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out _, out _, out _, out var filter),
                Is.True, $"{message}: field read failed");

            Assert.That(filter, Is.LessThan(maxFilter),
                $"{message}: filter {filter:0.00} must stay under {maxFilter:0.0}");
        });
    }

    /// <summary>Spawns a blocker on the corridor tile and stores it for door-state driving.</summary>
    private static async Task Place(Scene scene, int x, string proto)
    {
        EntityUid uid = default;
        await scene.Server.WaitPost(() =>
        {
            uid = scene.Server.EntMan.SpawnEntity(proto, new EntityCoordinates(scene.Grid, x + 0.5f, 0.5f));
        });

        await scene.Pair.RunTicksSync(20);

        if (scene.Server.EntMan.HasComponent<DoorComponent>(uid))
            scene.Doors[x] = uid;
    }

    /// <summary>Drives a placed door into the given state on both sides and waits until the client door component matches; a direct SetState never flips client collision.</summary>
    private static async Task SetDoorState(Scene scene, int x, DoorState state)
    {
        var door = scene.Doors[x];
        var cDoor = scene.Pair.ToClientUid(door);
        await scene.Server.WaitPost(() => scene.Server.System<ServerDoorSystem>().SetState(door, state));
        await scene.Client.WaitPost(() => scene.Client.System<ClientDoorSystem>().SetState(cDoor, state));
        await scene.Pair.RunTicksSync(30);

        for (var i = 0; i < 60; i++)
        {
            var ok = false;
            await scene.Client.WaitAssertion(() =>
            {
                ok = scene.Client.EntMan.GetComponent<DoorComponent>(cDoor).State == state;
            });

            if (ok)
                break;

            await scene.Pair.RunTicksSync(5);
        }

        await scene.Client.WaitAssertion(() =>
        {
            var doorComp = scene.Client.EntMan.GetComponent<DoorComponent>(cDoor);
            Assert.That(doorComp.State, Is.EqualTo(state), "client door state never caught up");
        });

        // Airlocks auto-close a couple seconds after opening; the tests need the state to stick.
        if (state == DoorState.Open)
        {
            await scene.Server.WaitPost(() =>
            {
                var doorComp = scene.Server.EntMan.GetComponent<DoorComponent>(door);
                doorComp.NextStateChange = null;
                scene.Server.EntMan.Dirty(door, doorComp);
            });
            await scene.Pair.RunTicksSync(10);
        }

        // Force the field TTL rebuild so a missed door event cannot stall the assertion.
        await scene.Pair.RunSeconds(2.5f);
    }

    /// <summary>Asserts the folded filter occlusion of a source on the corridor at the given tile east of the player.</summary>
    private static async Task AssertFilter(Scene scene, int sourceTileX, float expected, float tolerance, string message)
    {
        // The source is relative to the listener: the test-map grid may sit at any world offset.
        var sourcePos = scene.Listener.Position + new Vector2(sourceTileX * scene.TileSize, 0f);
        await scene.Client.WaitAssertion(() =>
        {
            scene.System.TryGetTile(new Vector2i(5, 0), out var tile5, out _, out _);
            scene.System.TryGetTile(new Vector2i(10, 0), out var tile10, out _, out _);

            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out var occl, out var len, out var past, out var filter),
                Is.True, "field read failed");

            var cGrid = scene.Pair.ToClientUid(scene.Grid);
            var cPlayer = scene.Pair.ToClientUid(scene.Player);
            var gridWorld = scene.Client.EntMan.GetComponent<TransformComponent>(cGrid).WorldPosition;
            var playerWorld = scene.Client.EntMan.GetComponent<TransformComponent>(cPlayer).WorldPosition;

            Assert.That(filter, Is.EqualTo(expected).Within(tolerance),
                $"{message}: occl {occl:0.000} len {len:0.0} past {past:0.0} seed {scene.System.FieldSeed} listener {scene.Listener.Position} tile5 {tile5} tile10 {tile10} gridWorld {gridWorld} playerWorld {playerWorld}");
        });
    }


    /// <summary>Analytic filter value: route blockers minus the past-decay, folded, plus the per-tile distance fade. The model clamps the occl at -ln(1e-9) like TryGetAudio does.</summary>
    private float Expected(Scene scene, float blockersOccl, int sourceX, int lastBlockerX)
    {
        var past = lastBlockerX >= 0 ? sourceX - lastBlockerX : 0;
        var occlR = MathF.Max(0f, blockersOccl - scene.System.OcclusionDecayPerTile * past);
        occlR = MathF.Min(occlR, -MathF.Log(1e-9f));
        return occlR * scene.Fold + (sourceX + past) * scene.PerTile;
    }

    [Test]
    public async Task CleanCorridorDistanceFade()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        // No blockers at all: the filter is pure distance fade, 10 tiles of it.
        await AssertFilter(scene, 10, 10f * scene.PerTile, 1.5f, "clean corridor fade");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SingleClosedAirlock()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "Airlock");
        await AssertFilter(scene, 10, Expected(scene, AirlockOccl(AirlockMuffle), 10, 5), 1.5f, "one airlock");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FiveAirlocksRowSilence()
    {
        var scene = await SetupScene(14);
        await using var pair = scene.Pair;

        for (var x = 5; x <= 9; x++)
            await Place(scene, x, "Airlock");

        // Five closed airlocks on the route: far past the audible bound.
        await AssertFilter(scene, 12, Expected(scene, 5f * AirlockOccl(AirlockMuffle), 12, 9), 6f, "row of five");
        await scene.Client.WaitAssertion(() =>
        {
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, scene.Listener.Position + new Vector2(12f * scene.TileSize, 0f), out _, out _, out _, out var filter),
                Is.True);
            Assert.That(filter, Is.GreaterThan(150f), "five airlocks must silence the route");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SourceOwnTileExemption()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "Airlock");
        // The sound born on the airlock tile is not muffled by it (round-21 rule).
        await AssertFilter(scene, 5, 5f * scene.PerTile, 1.5f, "own tile exempt");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OpeningAirlockRaisesAudibility()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "Airlock");
        await AssertFilter(scene, 10, Expected(scene, AirlockOccl(AirlockMuffle), 10, 5), 1.5f, "closed airlock");

        // Opening the airlock: the server's state becomes Open, but the engine does not flip the door's
        // client-side collision for a networked state change (no SetCollidable happens there), so on the
        // client the door stays solid and the model keeps the closed-route value — a consistency contract.
        await SetDoorState(scene, 5, DoorState.Open);
        var cDoor = scene.Pair.ToClientUid(scene.Doors[5]);
        await scene.Client.WaitAssertion(() =>
        {
            var doorComp = scene.Client.EntMan.GetComponent<DoorComponent>(cDoor);
            var phys = scene.Client.EntMan.GetComponent<PhysicsComponent>(cDoor);
            Assert.That(doorComp.State, Is.EqualTo(DoorState.Open));
            Assert.That(phys.CanCollide, Is.True, "the networked door state must not flip client collision");
        });
        await AssertFilter(scene, 10, Expected(scene, AirlockOccl(AirlockMuffle), 10, 5), 1.5f, "opened but solid on client");

        // Closing it again restores the muffled value.
        await SetDoorState(scene, 5, DoorState.Closed);
        await AssertFilter(scene, 10, Expected(scene, AirlockOccl(AirlockMuffle), 10, 5), 1.5f, "reclosed airlock");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VestibuleStack()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "Airlock");
        await Place(scene, 8, "AirlockGlass");

        await AssertFilter(scene, 10, Expected(scene, AirlockOccl(AirlockMuffle) + AirlockOccl(GlassAirlockMuffle), 10, 8), 2.5f, "airlock + glass airlock");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WindowMuffle()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "ReinforcedWindow");
        // Five tiles past the window the past-decay has fully eaten the mild muffle: pure distance fade remains.
        await AssertFilter(scene, 10, Expected(scene, WindowOccl, 10, 5), 1.5f, "window");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallFullSilence()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        // A sealed room around the source: every route to it crosses a wall, no space detour exists.
        await scene.Server.WaitPost(() =>
        {
            for (var x = 9; x <= 11; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x == 10 && y == 0)
                        continue;

                    scene.Server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(scene.Grid, x + 0.5f, y + 0.5f));
                }
            }
        });
        await scene.Pair.RunTicksSync(30);

        // The source sits inside the sealed room: the route crosses its wall (120 occl units raw).
        await AssertFilter(scene, 10, Expected(scene, 15f * 8f, 10, 9), 40f, "sealed room");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DetourAroundBlockerRaisesAudibility()
    {
        var scene = await SetupScene(12);
        await using var pair = scene.Pair;

        await Place(scene, 5, "Airlock");

        // Gates in the north wall row far from the airlock plus a real floor strip behind them open a
        // clean detour the primary pass rejects (13+ hops beats nothing against the 8.3-cost airlock
        // crossing) while the detour pass takes it (the airlock costs 14.8 there). Vacuum stays shut:
        // the detour pass excludes empty tiles, and the vacuum approach to the strip is longer than the
        // airlock for the primary.
        await scene.Server.WaitPost(() =>
        {
            var maps = scene.Server.System<SharedMapSystem>();
            var grid = scene.Server.EntMan.GetComponent<MapGridComponent>(scene.Grid);
            var tiles = scene.Server.ResolveDependency<ITileDefinitionManager>();
            var plating = new Tile(tiles["Plating"].TileId);

            foreach (var x in new[] { 2, 3, 4, 5, 6, 7, 8 })
                maps.SetTile(scene.Grid, grid, new Vector2i(x, 2), plating);

            foreach (var gate in new[] { new Vector2i(2, 1), new Vector2i(8, 1) })
            {
                var toDelete = new List<EntityUid>();
                var anchored = maps.GetAnchoredEntitiesEnumerator(scene.Grid, grid, gate);
                while (anchored.MoveNext(out var ent))
                    toDelete.Add(ent.Value);

                foreach (var ent in toDelete)
                    scene.Server.EntMan.DeleteEntity(ent);
            }
        });
        await scene.Pair.RunTicksSync(30);

        // The shortest route crosses the airlock one tile back; the clean detour must lift the filter past it.
        var primaryExpected = Expected(scene, AirlockOccl(AirlockMuffle), 6, 5);
        await scene.Client.WaitAssertion(() =>
        {
            var sourcePos = scene.Listener.Position + new Vector2(6f * scene.TileSize, 0f);
            Assert.That(
                scene.System.TryGetModelOcclusion(scene.Listener, sourcePos, out _, out _, out _, out var filter),
                Is.True, "field read failed");
            Assert.That(filter, Is.LessThan(primaryExpected - 5f),
                $"the detour must lift audibility: filter {filter:0.00} vs primary {primaryExpected:0.00}");
        });

        // Disabling detours restores the exact primary-route contract.
        await scene.Client.WaitPost(() =>
            scene.Client.ResolveDependency<IConfigurationManager>().SetCVar(MaidCVars.SoundPathDetourWeight.Name, 0f));
        await scene.Pair.RunTicksSync(60);
        await scene.Client.WaitAssertion(() =>
        {
            Assert.That(scene.System.DetourWeight, Is.EqualTo(0f),
                "the detour cvar flip must reach the client system");
        });
        await AssertFilter(scene, 6, primaryExpected, 1.5f, "detours disabled");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeyondFieldRadius()
    {
        var scene = await SetupScene(32);
        await using var pair = scene.Pair;

        // Source past the field edge: the clamp branch reads the edge tile and folds the straight remainder.
        await AssertFilter(scene, 30, 30f * scene.PerTile, 3f, "beyond field radius");

        await pair.CleanReturnAsync();
    }
}
