namespace Elasticstretch.Logging.Filebeat;

using System.Buffers;
using System.Text.Json;

sealed class ElasticEntry : IElasticEntry
{
    readonly SortedList<JsonEncodedText, List<IElasticField>> list = new();

    public int FieldCount => list.Count;

    public Utf8JsonWriter Begin(JsonEncodedText name)
    {
        var field = new ElasticField();
        GetFields(name).Add(field);

        return new(field.Writer);
    }

    public void Merge(IElasticEntry other)
    {
        for (var i = 0; i < other.FieldCount; i++)
        {
            var source = other.GetFields(i, out var name);
            var target = GetFields(name);

            for (var j = 0; j < source.Count; j++)
            {
                target.Add(source[j]);
            }
        }
    }

    public IReadOnlyList<IElasticField> GetFields(int index, out JsonEncodedText name)
    {
        if (index >= list.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        name = list.Keys[index];
        return list[name];
    }

    List<IElasticField> GetFields(JsonEncodedText name)
    {
        if (!list.TryGetValue(name, out var group))
        {
            list.Add(name, group = new());
        }

        return group;
    }

    sealed class ElasticField : IElasticField
    {
        readonly ArrayBufferWriter<byte> buffer = new();

        public IBufferWriter<byte> Writer => buffer;

        public void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
        }
    }
}
