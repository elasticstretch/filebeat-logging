namespace Elasticstretch.Logging.Filebeat;

internal interface IElasticLocal
{
    int Count { get; }

    IElasticEntry this[int index] { get; }

    IDisposable Add(IElasticEntry entry);
}
