using System.Text.Json;

namespace App.MAUI.Services;

public sealed class MauiActivityEventStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public MauiActivityEventStore()
    {
        _path = Path.Combine(FileSystem.AppDataDirectory, "user-activity-events.json");
    }

    public async Task<IReadOnlyList<StoredUserActivityEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return [];

            await using var fs = File.OpenRead(_path);
            var list =
                await JsonSerializer.DeserializeAsync<List<StoredUserActivityEvent>>(fs, SerializerOptions,
                    cancellationToken);
            return list ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(StoredUserActivityEvent e, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<StoredUserActivityEvent> list;
            if (File.Exists(_path))
                await using (var read = File.OpenRead(_path))
                {
                    list = await JsonSerializer.DeserializeAsync<List<StoredUserActivityEvent>>(read, SerializerOptions,
                               cancellationToken)
                           ?? [];
                }
            else
                list = [];

            list.Add(e);
            await using var write = File.Create(_path);
            await JsonSerializer.SerializeAsync(write, list, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
