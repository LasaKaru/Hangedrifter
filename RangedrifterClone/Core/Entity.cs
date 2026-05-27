namespace RangedrifterClone.Core;

public readonly struct Entity : IEquatable<Entity>
{
    public static readonly Entity None = new(-1);
    public int Id { get; }
    public bool IsValid => Id >= 0;
    public Entity(int id) => Id = id;
    public bool Equals(Entity other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Entity e && Equals(e);
    public override int GetHashCode() => Id;
    public override string ToString() => $"Entity({Id})";
    public static bool operator ==(Entity a, Entity b) => a.Id == b.Id;
    public static bool operator !=(Entity a, Entity b) => a.Id != b.Id;
}
