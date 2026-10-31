namespace Content.Client._Maid.Graphics;

/// <summary>
///     Stable ids for sprite post-shader entries. Engine multi-shader support needs each system to own its id,
///     the obsolete <c>SpriteComponent.PostShader</c> setter clears every entry.
/// </summary>
public static class MaidPostShaderIds
{
    public const string Stealth = "Stealth";
    public const string InteractionOutline = "InteractionOutline";
    public const string TargetOutline = "TargetOutline";
    public const string DragDropTarget = "DragDropTarget";
    public const string FloorOcclusion = "FloorOcclusion";
    public const string Hologram = "Hologram";
    public const string Holopad = "Holopad";

    /// <summary>
    ///     Everything stealth must run after. Stealth samples SCREEN_TEXTURE with SCREEN_UV, so it has to be the final pass.
    /// </summary>
    public static readonly string[] BeforeStealth =
    {
        InteractionOutline, TargetOutline, DragDropTarget, FloorOcclusion, Hologram, Holopad,
    };
}
