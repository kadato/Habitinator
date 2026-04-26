using System.Text.Json;
using Microsoft.Maui.Storage;

namespace App.MAUI.Services;

public sealed class MauiActivityEventStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public MauiActivityEventStore()
    {
        _path = Path.Combine(FileSystem.AppDataDirectory, "user-activity-events.json");
    }

    public async Task<IReadOnlyList<StoredUserActivityEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            await using FileStream fs = File.OpenRead(_path);
            List<StoredUserActivityEvent>? list =
                await JsonSerializer.DeserializeAsync<List<StoredUserActivityEvent>>(fs, SerializerOptions, cancellationToken);
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
            {
                await using (FileStream read = File.OpenRead(_path))
                {
                    list = await JsonSerializer.DeserializeAsync<List<StoredUserActivityEvent>>(read, SerializerOptions, cancellationToken)
                           ?? [];
                }
            }
            else
            {
                list = [];
            }

            list.Add(e);
            await using FileStream write = File.Create(_path);
            await JsonSerializer.SerializeAsync(write, list, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
