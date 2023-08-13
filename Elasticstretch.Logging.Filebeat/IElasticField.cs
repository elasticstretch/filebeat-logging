namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

interface IElasticField
{
    void CopyTo(Utf8JsonWriter writer);
}
