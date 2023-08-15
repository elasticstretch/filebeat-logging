namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

interface IElasticField
{
    void WriteTo(Utf8JsonWriter writer);
}
