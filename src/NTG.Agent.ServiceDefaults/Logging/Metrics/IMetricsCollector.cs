namespace NTG.Agent.ServiceDefaults.Logging.Metrics;

public interface IMetricsCollector
{
    void IncrementCounter(string name, double value = 1, params (string Key, string Value)[] tags);
    void RecordValue(string name, double value, params (string Key, string Value)[] tags);
    void RecordDuration(string name, TimeSpan duration, params (string Key, string Value)[] tags);
    IDisposable StartTimer(string name, params (string Key, string Value)[] tags);
    void RecordBusinessMetric(string eventName, object data, params (string Key, string Value)[] tags);
}
