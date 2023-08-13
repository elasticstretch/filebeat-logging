namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

interface IElasticEntry : IElasticFieldWriter
{
    int FieldCount { get; }

    void Add(JsonEncodedText name, IElasticField field);

    IReadOnlyList<IElasticField> GetFields(int index, out JsonEncodedText name);
}
