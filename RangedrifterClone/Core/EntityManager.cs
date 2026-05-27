namespace RangedrifterClone.Core;

public class EntityManager
{
    private int _nextId = 0;
    private readonly Dictionary<Type, Dictionary<int, object>> _components = new();
    private readonly HashSet<int> _activeEntities = new();
    private readonly Queue<int> _recycledIds = new();

    public Entity CreateEntity()
    {
        int id = _recycledIds.Count > 0 ? _recycledIds.Dequeue() : _nextId++;
        _activeEntities.Add(id);
        return new Entity(id);
    }

    public void DestroyEntity(Entity entity)
    {
        if (!_activeEntities.Remove(entity.Id)) return;
        foreach (var dict in _components.Values)
            dict.Remove(entity.Id);
        _recycledIds.Enqueue(entity.Id);
    }

    public void AddComponent<T>(Entity entity, T component) where T : class
    {
        var type = typeof(T);
        if (!_components.TryGetValue(type, out var dict))
        {
            dict = new Dictionary<int, object>();
            _components[type] = dict;
        }
        dict[entity.Id] = component;
    }

    public T? GetComponent<T>(Entity entity) where T : class
    {
        if (_components.TryGetValue(typeof(T), out var dict) &&
            dict.TryGetValue(entity.Id, out var comp))
            return (T)comp;
        return null;
    }

    public bool HasComponent<T>(Entity entity) where T : class
    {
        return _components.TryGetValue(typeof(T), out var dict) && dict.ContainsKey(entity.Id);
    }

    public bool TryGetComponent<T>(Entity entity, out T? component) where T : class
    {
        component = GetComponent<T>(entity);
        return component != null;
    }

    public IEnumerable<Entity> GetEntitiesWith<T>() where T : class
    {
        if (!_components.TryGetValue(typeof(T), out var dict))
            yield break;
        foreach (var id in dict.Keys.ToList())
            if (_activeEntities.Contains(id))
                yield return new Entity(id);
    }

    public IEnumerable<Entity> GetEntitiesWith<T1, T2>() where T1 : class where T2 : class
    {
        foreach (var e in GetEntitiesWith<T1>())
            if (HasComponent<T2>(e)) yield return e;
    }

    public IEnumerable<Entity> GetEntitiesWith<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class
    {
        foreach (var e in GetEntitiesWith<T1, T2>())
            if (HasComponent<T3>(e)) yield return e;
    }

    public IEnumerable<Entity> AllEntities => _activeEntities.Select(id => new Entity(id));
}
