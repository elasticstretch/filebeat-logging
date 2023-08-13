namespace Elasticstretch.Logging.Filebeat;

using System.Buffers;
using System.Text.Json;

sealed class ElasticEntry : IElasticEntry
{
    readonly SortedList<JsonEncodedText, List<IElasticField>> fields = new();

    public int FieldCount => fields.Count;

    public Utf8JsonWriter Begin(JsonEncodedText name)
    {
        var field = new ElasticField();
        Add(name, field);
        return new(field.Writer);
    }

    public void Add(JsonEncodedText name, IElasticField field)
    {
        if (!fields.TryGetValue(name, out var group))
        {
            fields.Add(name, group = new());
        }

        group.Add(field);
    }

    public IReadOnlyList<IElasticField> GetFields(int index, out JsonEncodedText name)
    {
        name = fields.Keys[index];
        return fields[name];
    }

    sealed class ElasticField : IElasticField
    {
        readonly ArrayBufferWriter<byte> buffer = new();

        public IBufferWriter<byte> Writer => buffer;

        public void CopyTo(Utf8JsonWriter writer)
        {
            writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
        }
    }
}
