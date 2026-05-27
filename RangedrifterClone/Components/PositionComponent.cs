namespace RangedrifterClone.Components;

public class PositionComponent
{
    public int X { get; set; }
    public int Y { get; set; }
    public SadRogue.Primitives.Point Point => new(X, Y);
}
