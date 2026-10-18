using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediQueue.Consultant.Services;

/// <summary>Petit utilitaire : un objet JSON persistant dans un fichier (écriture atomique).</summary>
public sealed class JsonFileStore<T> where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileStore(string path) => _path = path;

    public async Task<T?> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // fichier corrompu : on repart d'un état vide plutôt que de planter
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T value)
    {
        await _gate.WaitAsync();
        try
        {
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }
}
