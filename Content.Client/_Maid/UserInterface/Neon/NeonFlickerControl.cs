// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Client.UserInterface.Controls;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Maid.UserInterface.Neon;

/// <summary>
/// Soft neon-sign flicker: slow brightness pulse with rare short dropouts.
/// </summary>
public sealed class NeonFlickerControl : BoxContainer
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private float _phase;
    private float _flickerRemaining;

    /// <summary>
    /// How strong the pulse and dropouts are. 1 is the default lobby intensity.
    /// </summary>
    public float Intensity { get; set; } = 1f;

    public NeonFlickerControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        MouseFilter = MouseFilterMode.Pass;
        _phase = _random.NextFloat() * MathF.Tau;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        _phase += args.DeltaSeconds * 1.7f;
        var pulse = 0.5f + 0.5f * MathF.Sin(_phase);

        if (_flickerRemaining > 0f)
        {
            _flickerRemaining -= args.DeltaSeconds;
            pulse *= 0.28f + _random.NextFloat() * 0.45f;
        }
        else if (_random.Prob(Math.Clamp(args.DeltaSeconds * 0.28f, 0f, 0.2f)))
        {
            _flickerRemaining = 0.035f + _random.NextFloat() * 0.07f;
        }

        var amount = Math.Clamp(0.86f + 0.14f * pulse * Intensity, 0.35f, 1f);
        // Slight cyan lift so gold/white text reads as neon instead of a flat dim.
        Modulate = new Color(amount * 0.96f, amount, Math.Clamp(amount * 1.06f, 0f, 1f), 1f);
    }
}
