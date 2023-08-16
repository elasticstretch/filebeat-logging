namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

interface IElasticEntry : IElasticFieldWriter
{
    int FieldCount { get; }

    void Merge(IElasticEntry entry);

    IReadOnlyList<IElasticField> GetFields(int index, out JsonEncodedText name);
}
