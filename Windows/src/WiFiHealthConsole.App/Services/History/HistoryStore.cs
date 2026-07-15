using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services.History;

public sealed class HistoryStore : IHistoryStore, IDisposable
{
    public const int DefaultMaximumEntries = 2_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _maximumEntries;
    private bool _disposed;

    public HistoryStore(string? filePath = null, int maximumEntries = DefaultMaximumEntries)
    {
        if (maximumEntries is < 1 or > DefaultMaximumEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEntries),
                $"历史记录上限必须在 1～{DefaultMaximumEntries} 条之间。");
        }

        FilePath = Path.GetFullPath(filePath ?? BuildDefaultPath());
        _maximumEntries = maximumEntries;
        var typeInfoResolver = new DefaultJsonTypeInfoResolver();
        typeInfoResolver.Modifiers.Add(RemoveWifiSnapshotAliases);
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = typeInfoResolver,
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string FilePath { get; }

    public async Task<IReadOnlyList<HistorySample>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadCoreAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(
        HistorySample sample,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var samples = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            samples.Add(sample);
            await WriteCoreAsync(Normalize(samples), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IEnumerable<HistorySample> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(samples);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteCoreAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<List<HistorySample>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            List<HistorySample>? samples;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Read the early-preview bare-array format as a one-way compatibility path.
                samples = document.RootElement.Deserialize<List<HistorySample>>(_jsonOptions);
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty("samples", out var samplesElement)
                    || document.RootElement.TryGetProperty("Samples", out samplesElement)))
            {
                samples = samplesElement.Deserialize<List<HistorySample>>(_jsonOptions);
            }
            else
            {
                throw new JsonException("history.json 缺少 samples 数组。");
            }

            return Normalize(samples ?? []).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new HistoryStoreException(
                $"历史文件格式损坏，原文件已保留且不会被覆盖：{FilePath}",
                error);
        }
        catch (IOException error)
        {
            throw new HistoryStoreException($"无法读取历史文件：{FilePath}", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new HistoryStoreException($"没有权限读取历史文件：{FilePath}", error);
        }
    }

    private async Task WriteCoreAsync(
        IReadOnlyList<HistorySample> samples,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new HistoryStoreException("历史文件路径没有父目录。");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            var payload = new HistoryDocument
            {
                SchemaVersion = 1,
                UpdatedAt = DateTimeOffset.Now,
                Samples = samples,
            };

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer
                    .SerializeAsync(stream, payload, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Cancellation before the atomic replace leaves the previous history untouched.
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException error)
        {
            throw new HistoryStoreException($"无法写入历史文件：{FilePath}", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new HistoryStoreException($"没有权限写入历史文件：{FilePath}", error);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A stale temp file is safer than deleting or replacing the last good history.
            }
        }
    }

    private IReadOnlyList<HistorySample> Normalize(IEnumerable<HistorySample> samples) => samples
        .OrderBy(sample => sample.Timestamp)
        .ThenBy(sample => sample.Id)
        .TakeLast(_maximumEntries)
        .ToArray();

    private static string BuildDefaultPath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new HistoryStoreException("系统没有返回 LocalApplicationData 目录。");
        }

        return Path.Combine(localApplicationData, "WiFiHealthConsole", "history.json");
    }

    private static void RemoveWifiSnapshotAliases(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(WifiSnapshot))
        {
            return;
        }

        string[] aliases =
        [
            "SSID", "BSSID", "Channel", "ChannelWidth", "RSSI", "Noise", "SNR",
            "RxRateMbps", "TxRateMbps", "BandValue", "PrimaryChannelValue",
            "ChannelWidthValue", "RssiValue",
        ];
        foreach (var property in typeInfo.Properties
            .Where(property => aliases.Contains(property.Name, StringComparer.Ordinal))
            .ToArray())
        {
            typeInfo.Properties.Remove(property);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record HistoryDocument
    {
        public int SchemaVersion { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public required IReadOnlyList<HistorySample> Samples { get; init; }
    }
}
