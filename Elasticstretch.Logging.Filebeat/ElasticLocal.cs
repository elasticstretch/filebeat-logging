namespace Elasticstretch.Logging.Filebeat;

sealed class ElasticLocal : IElasticLocal
{
    readonly AsyncLocal<List<IElasticEntry>> local = new();

    public IElasticEntry this[int index]
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

    public IDisposable Add(IElasticEntry entry)
    {
        var list = local.Value ??= new();
        list.Add(entry);

        return new EntryRemover(local, list.Count - 1, entry);
    }

    sealed class EntryRemover : IDisposable
    {
        private readonly AsyncLocal<List<IElasticEntry>> local;
        private readonly int index;

        private IElasticEntry? entry;

        public EntryRemover(AsyncLocal<List<IElasticEntry>> local, int index, IElasticEntry entry)
        {
            this.local = local;
            this.index = index;
            this.entry = entry;
        }

        public void Dispose()
        {
            if (entry != null)
            {
                if (local.Value == null || index >= local.Value.Count || local.Value[index] != entry)
                {
                    throw new InvalidOperationException("Local entry not found.");
                }

                local.Value.RemoveAt(index);
                entry = null;
            }
        }
    }
}
