using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Storage;

/// <summary>
/// Tiny durable key/value store: one JSON file per key inside a folder, with an in-memory copy.
/// Writes go to a temp file and are moved into place so a crash never leaves a half-written file.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _folder;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, T> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private bool _loaded;

    public JsonFileStore(string folder, ILogger logger)
    {
        _folder = folder;
        _logger = logger;
        Directory.CreateDirectory(folder);
    }

    public int Count
    {
        get
        {
            EnsureLoaded();
            return _memory.Count;
        }
    }

    public IEnumerable<T> Values
    {
        get
        {
            EnsureLoaded();
            return _memory.Values;
        }
    }

    public T? Get(string key)
    {
        EnsureLoaded();
        return _memory.TryGetValue(key, out var v) ? v : null;
    }

    public async Task SetAsync(string key, T value, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        _memory[key] = value;
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(key);
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Laurels: could not persist {Key} to {Folder}", key, _folder);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Remove(string key)
    {
        _memory.TryRemove(key, out _);
        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Laurels: could not delete cache entry {Key}", key);
        }
    }

    public void Clear()
    {
        _memory.Clear();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Laurels: could not clear {Folder}", _folder);
        }
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_folder, safe + ".json");
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_memory)
        {
            if (_loaded)
            {
                return;
            }

            var loaded = 0;
            foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var value = JsonSerializer.Deserialize<T>(stream, SerializerOptions);
                    if (value is not null)
                    {
                        _memory[Path.GetFileNameWithoutExtension(file)] = value;
                        loaded++;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    _logger.LogWarning(ex, "Laurels: skipping unreadable cache file {File}", file);
                }
            }

            _logger.LogInformation("Laurels: loaded {Count} entries from {Folder}", loaded, _folder);
            _loaded = true;
        }
    }
}
