using System.Numerics;
using Content.Shared._Maid.OfferItem;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._Maid.OfferItem;

/// <summary>
///     Draws the give item icon next to the cursor while the local player is choosing who to offer an item to.
/// </summary>
public sealed class OfferItemIndicatorsOverlay : Overlay
{
    private readonly IInputManager _inputManager;
    private readonly IEyeManager _eye;
    private readonly IPlayerManager _player;
    private readonly OfferItemSystem _offer;

    private readonly Texture _sight;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    // The gift sits in a corner of a 64x64 frame, so it needs full scale and opacity to be noticeable.
    private readonly Color _mainColor = Color.White.WithAlpha(0.8f);
    private readonly Color _strokeColor = Color.Black.WithAlpha(0.6f);
    private const float Scale = 1f;

    public OfferItemIndicatorsOverlay(IInputManager input, IEntityManager entMan, IEyeManager eye, IPlayerManager player)
    {
        _inputManager = input;
        _eye = eye;
        _player = player;
        _offer = entMan.System<OfferItemSystem>();

        _sight = entMan.System<SpriteSystem>().Frame0(
            new SpriteSpecifier.Rsi(new ResPath("/Textures/_Maid/Interface/Misc/give_item.rsi"), "give_item"));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_offer.IsInOfferMode(_player.LocalEntity))
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mouseScreenPosition = _inputManager.MouseScreenPosition;
        var mousePosMap = _eye.PixelToMap(mouseScreenPosition);
        if (mousePosMap.MapId != args.MapId)
            return;

        var uiScale = (args.ViewportControl as Control)?.UIScale ?? 1f;
        var limitedScale = MathF.Min(uiScale, 1.25f);

        var sightSize = _sight.Size * limitedScale * Scale;
        var expandedSize = sightSize + new Vector2(7f, 7f);
        var centerPos = mouseScreenPosition.Position;

        args.ScreenHandle.DrawTextureRect(_sight,
            UIBox2.FromDimensions(centerPos - sightSize * 0.5f, sightSize), _strokeColor);
        args.ScreenHandle.DrawTextureRect(_sight,
            UIBox2.FromDimensions(centerPos - expandedSize * 0.5f, expandedSize), _mainColor);
    }
}
