using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample;

public class InMemoryCrudStore<TEntity, TKey> : ICrudStore<TEntity, TKey>
    where TEntity : class
{
    private readonly List<TEntity> _items;
    private readonly Func<TEntity, TKey> _keySelector;
    private readonly Action<TEntity, TKey> _keySetter;
    private int _nextId;

    public InMemoryCrudStore(
        List<TEntity> seed,
        Func<TEntity, TKey> keySelector,
        Action<TEntity, TKey> keySetter,
        int nextId)
    {
        _items = seed;
        _keySelector = keySelector;
        _keySetter = keySetter;
        _nextId = nextId;
    }

    public ValueTask<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default)
    {
        var item = _items.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals(_keySelector(x), id));
        return ValueTask.FromResult(item);
    }

    public IQueryable<TEntity> Query() => _items.AsQueryable();

    public ValueTask CreateAsync(TEntity entity, CancellationToken ct = default)
    {
        if (typeof(TKey) == typeof(int))
        {
            _keySetter(entity, (TKey)(object)_nextId++);
        }
        _items.Add(entity);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(TEntity entity, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public ValueTask DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        _items.Remove(entity);
        return ValueTask.CompletedTask;
    }
}
