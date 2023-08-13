namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

static class ElasticSchema
{
    public const string Version = "8.9.0";

    public static class Fields
    {
        public static readonly JsonEncodedText Timestamp = JsonEncodedText.Encode("@timestamp"),
            Message = JsonEncodedText.Encode("message"),
            LogLevel = JsonEncodedText.Encode("log.level"),
            LogLogger = JsonEncodedText.Encode("log.logger"),
            Error = JsonEncodedText.Encode("error"),
            Type = JsonEncodedText.Encode("type"),
            StackTrace = JsonEncodedText.Encode("stack_trace"),
            Event = JsonEncodedText.Encode("event"),
            Code = JsonEncodedText.Encode("code"),
            Action = JsonEncodedText.Encode("action"),
            EcsVersion = JsonEncodedText.Encode("ecs.version");
    }
}