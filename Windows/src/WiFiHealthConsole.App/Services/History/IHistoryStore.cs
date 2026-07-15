using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services.History;

public interface IHistoryStore
{
    string FilePath { get; }

    Task<IReadOnlyList<HistorySample>> LoadAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(HistorySample sample, CancellationToken cancellationToken = default);

    Task SaveAsync(
        IEnumerable<HistorySample> samples,
        CancellationToken cancellationToken = default);
}

public sealed class HistoryStoreException : Exception
{
    public HistoryStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
