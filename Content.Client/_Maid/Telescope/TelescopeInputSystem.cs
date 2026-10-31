using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._Maid.CVars;
using Content.Shared._Maid.Telescope;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._Maid.Telescope;

/// <summary>
///     Moves the camera towards the cursor while the look up key is held (or toggled) with a telescope in hand.
///     The offset is smoothed locally every frame and reported to the server.
/// </summary>
public sealed class TelescopeInputSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly TelescopeSystem _telescope = default!;

    /// <summary>
    ///     How often the offset is sent to the server.
    /// </summary>
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.05);

    private bool _holdLookUp;
    private bool _toggled;
    private bool _wasKeyDown;
    private EntityUid? _lastTelescope;
    private ScalingViewport? _viewport;
    private Vector2 _lastSent;
    private TimeSpan _nextSend;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, MaidCVars.HoldLookUp, val =>
        {
            _holdLookUp = val;
            _toggled = false;
        }, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Key state is read here instead of a command bind: bound handlers are re-run on every prediction reconcile,
        // which flips the toggle an unpredictable number of times.
        var keyDown = _inputSystem.CmdStates.GetState(ContentKeyFunctions.LookUp) == BoundKeyState.Down;
        var pressed = keyDown && !_wasKeyDown;
        _wasKeyDown = keyDown;

        var player = _player.LocalEntity;
        if (_telescope.GetTelescope(player) is not { } telescope || player == null)
        {
            _toggled = false;
            _lastTelescope = null;
            return;
        }

        if (_lastTelescope != telescope.Owner)
        {
            _lastTelescope = telescope.Owner;
            _toggled = false;
        }

        if (pressed && !_holdLookUp)
            _toggled = !_toggled;

        var active = _holdLookUp ? keyDown : _toggled;

        var target = active ? GetTargetOffset(telescope.Comp) ?? telescope.Comp.Offset : Vector2.Zero;

        // Frame rate independent version of the old "lerp by LerpAmount every frame at 60 FPS".
        var t = 1f - MathF.Pow(1f - Math.Clamp(telescope.Comp.LerpAmount, 0.001f, 1f), frameTime * 60f);
        var offset = Vector2.Lerp(telescope.Comp.Offset, target, t);
        if ((offset - target).LengthSquared() < 0.0001f)
            offset = target;

        _telescope.SetOffset(telescope, player.Value, offset);

        if (offset == _lastSent || _timing.RealTime < _nextSend && offset != Vector2.Zero)
            return;

        _lastSent = offset;
        _nextSend = _timing.RealTime + SendInterval;
        RaiseNetworkEvent(new TelescopeOffsetMessage(offset));
    }

    /// <summary>
    ///     Offset the camera should move to, based on the cursor position relative to the viewport center.
    /// </summary>
    private Vector2? GetTargetOffset(TelescopeComponent telescope)
    {
        var mousePos = _input.MouseScreenPosition;
        if (!mousePos.IsValid)
            return null;

        if (_ui.MouseGetControl(mousePos) is ScalingViewport viewport)
            _viewport = viewport;

        if (_viewport == null || _viewport.Disposed)
            return null;

        var size = _viewport.PixelSize;
        var center = _viewport.GlobalPixelPosition + (Vector2) size / 2f;

        var diff = mousePos.Position - center;
        var len = diff.Length();

        var maxLength = Math.Min(size.X, size.Y) * 0.4f;
        var minLength = maxLength * 0.2f;

        if (len > maxLength)
        {
            diff *= maxLength / len;
            len = maxLength;
        }

        // Dead zone around the center so the camera doesn't wobble.
        if (len <= minLength)
            return Vector2.Zero;

        diff -= diff * minLength / len;

        var divisor = maxLength * telescope.Divisor;
        var offset = new Vector2(diff.X / divisor, -diff.Y / divisor);
        return new Angle(-_eye.CurrentEye.Rotation.Theta).RotateVec(offset);
    }
}
