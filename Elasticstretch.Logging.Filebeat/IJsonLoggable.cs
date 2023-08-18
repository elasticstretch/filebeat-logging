namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

interface IJsonLoggable
{
    void Log(Utf8JsonWriter writer);
}
