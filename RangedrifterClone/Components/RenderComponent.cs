namespace RangedrifterClone.Components;

public class RenderComponent
{
    public char Glyph { get; set; }
    public SadRogue.Primitives.Color Foreground { get; set; } = SadRogue.Primitives.Color.White;
    public SadRogue.Primitives.Color Background { get; set; } = SadRogue.Primitives.Color.Transparent;
    public int RenderLayer { get; set; } = 0;
    public bool IsVisible { get; set; } = true;
}
