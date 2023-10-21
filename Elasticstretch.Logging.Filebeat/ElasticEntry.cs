namespace Elasticstretch.Logging.Filebeat;

using System.Buffers;
using System.Text.Json;

sealed class ElasticEntry : IElasticFieldWriter, IJsonLoggable
{
    readonly SortedList<JsonEncodedText, List<IJsonLoggable>> list = new(JsonTextComparer.Instance);

    public int FieldCount => list.Count;

    public Utf8JsonWriter Begin(JsonEncodedText name)
    {
        var field = new ElasticField();
        GetFields(name).Add(field);

        return new(field.Writer);
    }

    public void Merge(ElasticEntry other)
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

    public IReadOnlyList<IJsonLoggable> GetFields(int index, out JsonEncodedText name)
    {
        if (index >= list.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        name = list.Keys[index];
        return list[name];
    }

    public void Log(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        for (var i = 0; i < FieldCount; i++)
        {
            var fields = GetFields(i, out var name);

            writer.WritePropertyName(name);

            if (fields.Count == 1)
            {
                fields[0].Log(writer);
            }
            else
            {
                writer.WriteStartArray();

                for (var j = 0; j < fields.Count; j++)
                {
                    fields[j].Log(writer);
                }

                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    List<IJsonLoggable> GetFields(JsonEncodedText name)
    {
        if (!list.TryGetValue(name, out var group))
        {
            list.Add(name, group = new());
        }

        return group;
    }

    sealed class JsonTextComparer : IComparer<JsonEncodedText>
    {
        private JsonTextComparer()
        {
        }

        public static JsonTextComparer Instance { get; } = new();

        public int Compare(JsonEncodedText x, JsonEncodedText y)
        {
            return string.CompareOrdinal(x.ToString(), y.ToString());
        }
    }

    sealed class ElasticField : IJsonLoggable
    {
        readonly ArrayBufferWriter<byte> buffer = new();

        public IBufferWriter<byte> Writer => buffer;

        public void Log(Utf8JsonWriter writer)
        {
            writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
        }
    }
}
