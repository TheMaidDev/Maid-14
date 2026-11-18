#nullable enable
using System.Linq;
using System.Numerics;
using Content.Goobstation.Maths.FixedPoint;
using Content.IntegrationTests.Tests.Interaction;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._Maid.RakeTrap;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._Maid;

/// <summary>
///     Tests for the rake trap: deploying in front of the user, triggering on step,
///     and disarming an armed rake by clicking it.
/// </summary>
public sealed class RakeTrapTest : MovementTest
{
    /// <summary>
    ///     Face east so the tile in front of the player is the tile east of them
    ///     (players spawn facing north, where the linear test grid has no tiles).
    /// </summary>
    private async Task FaceEast()
    {
        await Server.WaitPost(() => Xform(Player).LocalRotation = Angle.FromDegrees(90));
        await RunTicks(1);
    }

    /// <summary>
    ///     Puts the rake in hand and deploys it armed on the tile in front of the player
    ///     through its right-click deploy verb.
    /// </summary>
    private async Task<NetEntity> DeployRake()
    {
        var rake = await PlaceInHands("Rake");
        await FaceEast();
        await ExecuteDeployVerb(rake);
        return rake;
    }

    /// <summary>
    ///     Finds the deploy verb on the held rake and executes it server-side, like the
    ///     right-click verb menu would.
    /// </summary>
    private async Task ExecuteDeployVerb(NetEntity rake)
    {
        var rakeUid = ToServer(rake);
        await Server.WaitPost(() =>
        {
            var verbSystem = SEntMan.System<SharedVerbSystem>();
            var verbs = verbSystem.GetLocalVerbs(rakeUid, ToServer(Player), typeof(InteractionVerb));
            var deploy = verbs.FirstOrDefault(v => v.Text == Loc.GetString("rake-deploy-verb"));
            Assert.That(deploy, Is.Not.Null, "The held rake has no deploy verb");
            verbSystem.ExecuteVerb(deploy!, ToServer(Player), rakeUid);
        });
        await RunTicks(5);
    }

    /// <summary>
    ///     Spawns a mob on the tile east of the rake and sends it sliding west onto the rake,
    ///     at the given speed (the trap needs a moving victim). Optionally knocks it down first,
    ///     turning it into a sliding crawling body.
    /// </summary>
    private async Task<EntityUid> SlideOntoRake(NetEntity rake, string proto, float speed, bool down = false)
    {
        var victim = await SpawnEntity(proto, Xform(rake).Coordinates.Offset(new Vector2(0.65f, 0f)));
        await Server.WaitPost(() =>
        {
            // Mobs are kinematic-controller bodies driven by the movement system: with no input
            // their velocity is zeroed every tick. Strip the movement components and switch to a
            // plain dynamic body so the dummy slides at the given speed.
            SEntMan.RemoveComponent<InputMoverComponent>(victim);
            SEntMan.RemoveComponent<MobMoverComponent>(victim);

            if (down)
                SEntMan.System<SharedStunSystem>().TryKnockdown(victim, TimeSpan.FromSeconds(10));

            var physics = SEntMan.System<SharedPhysicsSystem>();
            physics.SetBodyType(victim, BodyType.Dynamic);
            physics.SetLinearVelocity(victim, new Vector2(-speed, 0));
        });
        await RunSeconds(1.5f);
        return victim;
    }

    /// <summary>
    ///     Using the wielded rake in hand drops it, armed, on the tile in front of the player,
    ///     facing away from them.
    /// </summary>
    [Test]
    public async Task DeployRakeTest()
    {
        var rake = await DeployRake();

        Assert.That(HandSys.TryGetActiveItem((ToServer(Player), Hands), out _), Is.False,
            "Rake was not dropped from the hand");
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // The rake copies the player's facing: the directional sprite points the head away.
        Assert.That(Xform(rake).LocalRotation.Theta, Is.EqualTo(Xform(Player).LocalRotation.Theta).Within(0.01),
            "Deployed rake does not face away from the installer");

        // The deployed trap is hidden: no examine for anyone.
        var examine = new ExamineAttemptEvent(ToServer(Player));
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(ToServer(rake), examine));
        Assert.That(examine.Cancelled, Is.True, "Deployed trap should not be examinable");

        // The rake should be on the tile in front of the player (east).
        var delta = Xform(rake).Coordinates.Position - Xform(Player).Coordinates.Position;
        Assert.That(Math.Abs(delta.X - 1f), Is.LessThan(0.3f), $"Rake was placed at unexpected X offset: {delta.X}");
    }

    /// <summary>
    ///     The installer steps over their own armed rake during the grace window — no trigger,
    ///     rake stays armed.
    /// </summary>
    [Test]
    public async Task DeployerStepsOnOwnRakeTest()
    {
        var rake = await DeployRake();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // Walk over the rake tile: the installer must not set their own trap off.
        await Move(DirectionFlag.East, 1.5f);
        await RunSeconds(1f);

        AssertComp<KnockedDownComponent>(false, Player);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Installer's own rake should stay armed");
    }

    /// <summary>
    ///     After the grace window expires the installer sets off their own rake like anyone else.
    /// </summary>
    [Test]
    public async Task InstallerStepsOnOwnRakeAfterGraceTest()
    {
        var rake = await DeployRake();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // Shrink the grace window so the test doesn't have to wait it out.
        await Server.WaitPost(() => Comp<RakeTrapComponent>(rake).InstallerGrace = TimeSpan.FromSeconds(0.5));
        await RunSeconds(1f);

        // Walk over the rake tile: the installer is no longer immune.
        await Move(DirectionFlag.East, 1.5f);
        await RunSeconds(1f);

        AssertComp<KnockedDownComponent>(true, Player);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False, "Rake should disarm after hitting its installer");
    }

    /// <summary>
    ///     An IPC stepping onto the rake sets it off like a human does (HumanoidAppearance check).
    /// </summary>
    [Test]
    public async Task IpcSteppingOnRakeTest()
    {
        var rake = await DeployRake();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        var ipc = await SlideOntoRake(rake, "MobIPC", 3f);

        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False, "IPC did not trigger the rake");
        Assert.That(SEntMan.HasComponent<KnockedDownComponent>(ipc), Is.True, "IPC was not knocked down by the rake");
    }

    /// <summary>
    ///     Mobs that are not humanoids (mice, pets, borgs) step over the rake harmlessly.
    /// </summary>
    [Test]
    public async Task NonHumanMobDoesNotTriggerTest()
    {
        var rake = await DeployRake();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // A mouse stepping onto the rake tile must not set it off.
        var mouse = await SpawnEntity("MobMouse", Xform(rake).Coordinates);
        await RunSeconds(1.5f);

        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Rake should stay armed when a mouse steps on it");
        Assert.That(SEntMan.HasComponent<KnockedDownComponent>(mouse), Is.False, "Mouse should not be knocked down");
    }

    /// <summary>
    ///     A knocked-down body sliding over the rake does not set it off — lying or crawling
    ///     through the trap is safe.
    /// </summary>
    [Test]
    public async Task DownedMobDoesNotTriggerTest()
    {
        var rake = await DeployRake();

        var body = await SlideOntoRake(rake, "MobHuman", 3f, down: true);

        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True,
            "Rake should stay armed when a lying body slides over it");
        Assert.That(SEntMan.HasComponent<RakeMuffledComponent>(body), Is.False,
            "Sliding body should not be hit by the rake");
    }

    /// <summary>
    ///     A mob moving below the trigger speed does not set the rake off.
    /// </summary>
    [Test]
    public async Task SlowMobDoesNotTriggerTest()
    {
        var rake = await DeployRake();

        var slowpoke = await SlideOntoRake(rake, "MobHuman", 1.4f);

        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True,
            "Rake should stay armed when a slow mob steps on it");
        Assert.That(SEntMan.HasComponent<KnockedDownComponent>(slowpoke), Is.False,
            "Slow mob should not be knocked down by the rake");
    }

    /// <summary>
    ///     A mob stepping onto an armed rake is knocked down, damaged, muffled and dazed,
    ///     and the rake disarms. The installer is not affected.
    /// </summary>
    [Test]
    public async Task SteppingOnRakeTest()
    {
        var rake = await DeployRake();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // A victim (not the installer) steps onto the rake tile at walking pace.
        var victim = await SlideOntoRake(rake, "MobHuman", 3f);

        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False, "Rake did not disarm after triggering");
        Assert.That(SEntMan.HasComponent<KnockedDownComponent>(victim), Is.True,
            "Victim was not knocked down by the rake");

        var damage = SEntMan.GetComponent<DamageableComponent>(victim).TotalDamage;
        Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "Rake did not deal any damage");

        Assert.That(SEntMan.HasComponent<RakeMuffledComponent>(victim), Is.True,
            "Victim does not have the muffled audio component");
        Assert.That(SEntMan.HasComponent<RakeDazedComponent>(victim), Is.True,
            "Victim does not have the screen daze component");

        // The installer walked nowhere, but must not be affected regardless.
        AssertComp<KnockedDownComponent>(false, Player);
    }

    /// <summary>
    ///     Clicking an armed rake blocks the pickup and, after the disarm delay, makes it pickable again.
    /// </summary>
    [Test]
    public async Task DisarmArmedRakeTest()
    {
        var rake = await DeployRake();
        var rakeUid = ToServer(rake);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // A click (InteractHand) on the armed rake is intercepted before the pickup
        // attempt: handled, no pickup, disarm starts.
        var ev = new InteractHandEvent(ToServer(Player), rakeUid);
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(rakeUid, ev));
        await RunTicks(5);
        Assert.That(ev.Handled, Is.True, "Click on an armed rake should be handled");
        Assert.That(HandSys.IsHolding((ToServer(Player), Hands), rakeUid, out _), Is.False,
            "Armed rake should never end up in a hand");
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Armed rake should not disarm instantly");

        // The disarm doafter takes 2 seconds.
        await RunSeconds(2.5f);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False, "Rake was not disarmed by the click");

        // Once disarmed, a pickup attempt is not blocked anymore (backstop path).
        var ev2 = new GettingPickedUpAttemptEvent(ToServer(Player), rakeUid);
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(rakeUid, ev2));
        await RunTicks(5);
        Assert.That(ev2.Cancelled, Is.False, "Disarmed rake should be pickable");
    }

    /// <summary>
    ///     An unarmed rake lying on the floor is harmless when walked over.
    /// </summary>
    [Test]
    public async Task UnarmedRakeDoesNotTriggerTest()
    {
        var rake = await SpawnTarget("Rake");
        var rakeUid = ToServer(rake);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False, "Freshly spawned rake should be unarmed");

        // The unarmed item examines normally (stats and description).
        var ev = new ExamineAttemptEvent(ToServer(Player));
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(rakeUid, ev));
        Assert.That(ev.Cancelled, Is.False, "Unarmed rake should be examinable");

        await Move(DirectionFlag.East, 1.5f);
        await RunSeconds(1f);

        AssertComp<KnockedDownComponent>(false, Player);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False);
    }
}
