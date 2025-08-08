using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NTG.Agent.ServiceDefaults.Logging.Metrics;

public class MetricsCollector(ILogger<MetricsCollector> logger, string meterName = "NTG.Agent") : IMetricsCollector, IDisposable
{
    private readonly Meter _meter = new(meterName);
    private readonly ILogger<MetricsCollector> _logger = logger;
    private readonly Dictionary<string, Counter<double>> _counters = [];
    // Histograms are used to record distributions of values, such as response times or sizes.
    // They allow you to analyze the distribution of recorded values over time.
    private readonly Dictionary<string, Histogram<double>> _histograms = [];

    public void IncrementCounter(string name, double value = 1, params (string Key, string Value)[] tags)
    {
        var counter = GetOrCreateCounter(name);
        var tagList = new TagList(tags.Select(t => new KeyValuePair<string, object?>(t.Key, t.Value)).ToArray());

        counter.Add(value, tagList);

        _logger.LogInformation("Counter incremented: {CounterName} by {Value} with tags {@Tags}",
            name, value, tags);
    }

    public void RecordValue(string name, double value, params (string Key, string Value)[] tags)
    {
        var histogram = GetOrCreateHistogram(name);
        var tagList = new TagList(tags.Select(t => new KeyValuePair<string, object?>(t.Key, t.Value)).ToArray());

        histogram.Record(value, tagList);

        _logger.LogInformation("Value recorded: {MetricName} = {Value} with tags {@Tags}",
            name, value, tags);
    }

    public void RecordDuration(string name, TimeSpan duration, params (string Key, string Value)[] tags)
    {
        RecordValue($"{name}.duration_ms", duration.TotalMilliseconds, tags);

        _logger.LogInformation("Performance metric: {Operation} completed in {Duration}ms with tags {@Tags}",
            name, duration.TotalMilliseconds, tags);
    }

    public IDisposable StartTimer(string name, params (string Key, string Value)[] tags)
    {
        return new MetricsTimer(this, name, tags);
    }

    public void RecordBusinessMetric(string eventName, object data, params (string Key, string Value)[] tags)
    {
        IncrementCounter($"business.{eventName}", 1, tags);

        _logger.LogInformation("Business event: {EventName} with data {@Data} and tags {@Tags}",
            eventName, data, tags);
    }

    private Counter<double> GetOrCreateCounter(string name)
    {
        if (!_counters.TryGetValue(name, out var counter))
        {
            counter = _meter.CreateCounter<double>(name);
            _counters[name] = counter;
        }
        return counter;
    }

    private Histogram<double> GetOrCreateHistogram(string name)
    {
        if (!_histograms.TryGetValue(name, out var histogram))
        {
            histogram = _meter.CreateHistogram<double>(name);
            _histograms[name] = histogram;
        }
        return histogram;
    }

    public void Dispose()
    {
        _meter?.Dispose();
        GC.SuppressFinalize(this);
    }

    private class MetricsTimer(MetricsCollector collector, string name, (string Key, string Value)[] tags) : IDisposable
    {
        private readonly MetricsCollector _collector = collector;
        private readonly string _name = name;
        private readonly (string Key, string Value)[] _tags = tags;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public void Dispose()
        {
            _stopwatch.Stop();
            _collector.RecordDuration(_name, _stopwatch.Elapsed, _tags);
        }
    }
}
