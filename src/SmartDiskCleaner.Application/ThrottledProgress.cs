namespace SmartDiskCleaner.Application;

using SmartDiskCleaner.Domain;

public sealed class ThrottledProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _interval;
    private readonly Func<T, bool>? _alwaysReportPredicate;
    private readonly object _lock = new();
    private DateTimeOffset _lastReportTime = DateTimeOffset.MinValue;
    private T? _lastValue;

    public ThrottledProgress(Action<T> handler, TimeSpan interval, Func<T, bool>? alwaysReportPredicate = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _interval = interval;
        _alwaysReportPredicate = alwaysReportPredicate;
    }

    public void Report(T value)
    {
        var now = DateTimeOffset.UtcNow;
        var shouldReportImmediately = _alwaysReportPredicate?.Invoke(value) ?? false;

        lock (_lock)
        {
            _lastValue = value;
            if (shouldReportImmediately || (now - _lastReportTime) >= _interval)
            {
                _lastReportTime = now;
                _handler(value);
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_lastValue != null)
            {
                _lastReportTime = DateTimeOffset.UtcNow;
                _handler(_lastValue);
            }
        }
    }
}
