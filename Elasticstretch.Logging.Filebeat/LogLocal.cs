namespace Elasticstretch.Logging.Filebeat;

sealed class LogLocal<TState>
{
    readonly AsyncLocal<List<TState>> local = new();

    public TState this[int index]
    {
        get
        {
            if (local.Value == null || index >= local.Value.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return local.Value[index];
        }
    }

    public int Count => local.Value != null ? local.Value.Count : 0;

    public IDisposable Add(TState entry)
    {
        var list = local.Value ??= new();
        list.Add(entry);

        return new EntryRemover(local, list.Count - 1, entry);
    }

    sealed class EntryRemover : IDisposable
    {
        private readonly AsyncLocal<List<TState>> local;
        private readonly int index;

        private TState? entry;

        public EntryRemover(AsyncLocal<List<TState>> local, int index, TState entry)
        {
            this.local = local;
            this.index = index;
            this.entry = entry;
        }

        public void Dispose()
        {
            if (entry != null)
            {
                if (local.Value == null || local.Value.IndexOf(entry) != index)
                {
                    throw new InvalidOperationException("Local entry not found.");
                }

                local.Value.RemoveAt(index);
                entry = default;
            }
        }
    }
}
