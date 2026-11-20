using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Client._Maid.Audio.PathSound;
using Content.Client.Doors;
using Content.IntegrationTests.Pair;
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
    public async Task BeyondFieldRadius()
    {
        var scene = await SetupScene(32);
        await using var pair = scene.Pair;

        // Source past the field edge: the clamp branch reads the edge tile and folds the straight remainder.
        await AssertFilter(scene, 30, 30f * scene.PerTile, 3f, "beyond field radius");

        await pair.CleanReturnAsync();
    }
}
