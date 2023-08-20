namespace Elasticstretch.Logging.Filebeat;

using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

sealed class LogSerializer
{
    static readonly byte[] Delimiter = Encoding.UTF8.GetBytes(Environment.NewLine);

    readonly BufferBlock<IJsonLoggable> block = new();

    int status;

    public bool TryStart()
    {
        return Interlocked.CompareExchange(ref status, 1, 0) == 0;
    }

    public bool TryAppend(IJsonLoggable entry)
    {
        return Volatile.Read(ref status) > 0 && block.Post(entry);
    }

    public async Task<bool> TryFlushAsync(Utf8JsonWriter writer, CancellationToken cancellationToken)
    {
        if (await block.OutputAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            while (block.TryReceive(out var item))
            {
                item.Log(writer);
                writer.WriteRawValue(Delimiter, skipInputValidation: true);
            }

            return true;
        }

        return false;
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref status, 2) == 1)
        {
            block.Complete();
        }
    }
}
