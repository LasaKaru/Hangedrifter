namespace RangedrifterClone.Core;

public class MessageLog
{
    private readonly List<(string Text, SadRogue.Primitives.Color Color)> _messages = new();
    private const int MaxMessages = 100;

    public event Action<string, SadRogue.Primitives.Color>? MessageAdded;

    public IReadOnlyList<(string Text, SadRogue.Primitives.Color Color)> Messages => _messages;

    public void Add(string text, SadRogue.Primitives.Color? color = null)
    {
        var c = color ?? SadRogue.Primitives.Color.White;
        _messages.Add((text, c));
        if (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
        MessageAdded?.Invoke(text, c);
    }
}
