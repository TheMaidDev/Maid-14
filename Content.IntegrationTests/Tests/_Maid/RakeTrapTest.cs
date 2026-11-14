#nullable enable
using Content.Goobstation.Maths.FixedPoint;
using Content.IntegrationTests.Tests.Interaction;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._Maid.RakeTrap;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

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
    ///     Using the rake in hand drops it, armed, on the tile in front of the player.
    /// </summary>
    [Test]
    public async Task DeployRakeTest()
    {
        var rake = await PlaceInHands("Rake");
        await FaceEast();
        await UseInHand();

        Assert.That(HandSys.TryGetActiveItem((ToServer(Player), Hands), out _), Is.False,
            "Rake was not dropped from the hand");
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // The rake should be on the tile in front of the player (east).
        var delta = Xform(rake).Coordinates.Position - Xform(Player).Coordinates.Position;
        Assert.That(Math.Abs(delta.X - 1f), Is.LessThan(0.3f), $"Rake was placed at unexpected X offset: {delta.X}");
    }

    /// <summary>
    ///     The installer steps over their own armed rake freely — no trigger, rake stays armed.
    /// </summary>
    [Test]
    public async Task DeployerStepsOnOwnRakeTest()
    {
        var rake = await PlaceInHands("Rake");
        await FaceEast();
        await UseInHand();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // Walk over the rake tile: the installer must not set their own trap off.
        await Move(DirectionFlag.East, 1.5f);
        await RunSeconds(1f);

        AssertComp<KnockedDownComponent>(false, Player);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Installer's own rake should stay armed");
    }

    /// <summary>
    ///     A mob stepping onto an armed rake is knocked down, damaged, muffled and dazed,
    ///     and the rake disarms. The installer is not affected.
    /// </summary>
    [Test]
    public async Task SteppingOnRakeTest()
    {
        var rake = await PlaceInHands("Rake");
        await FaceEast();
        await UseInHand();
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.True, "Deployed rake is not armed");

        // A victim (not the installer) steps onto the rake tile.
        var victim = await SpawnEntity("MobHuman", Xform(rake).Coordinates);
        await RunSeconds(1.5f);

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
        var rake = await PlaceInHands("Rake");
        await FaceEast();
        await UseInHand();
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

        // Examine is hidden even for an unarmed rake.
        var ev = new ExamineAttemptEvent(ToServer(Player));
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(rakeUid, ev));
        Assert.That(ev.Cancelled, Is.True, "Rake should not be examinable");

        await Move(DirectionFlag.East, 1.5f);
        await RunSeconds(1f);

        AssertComp<KnockedDownComponent>(false, Player);
        Assert.That(Comp<RakeTrapComponent>(rake).Armed, Is.False);
    }
}
