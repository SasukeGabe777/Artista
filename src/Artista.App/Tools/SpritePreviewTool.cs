using System.Windows.Input;

namespace Artista.App.Tools;

/// <summary>
/// Action tool that converts selected sprite-sheet regions (or parked
/// pasteboard pieces) into an ordered animation in a Sprite Canvas window.
/// </summary>
public sealed class SpritePreviewTool : ToolBase
{
    public override string Name => "Sprite Preview";
    public override string IconKey => "Icon.SpritePreview";
    public override string StatusHint =>
        "Park two or more frames on the pasteboard, or Ctrl-select separate frame regions, then click Sprite Preview.";
    public override Cursor Cursor => Cursors.Arrow;

    public override void OnActivated() => Context.OpenSpritePreview();
}
