using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class ConfigStore : IConfigStore
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _data;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
        _data = new Dictionary<string, JsonElement>();
    }

    public T Get<T>(string key) where T : new()
    {
        if (_data.TryGetValue(key, out var element))
        {
            try { return element.Deserialize<T>() ?? new T(); }
            catch { return new T(); }
        }
        return new T();
    }

    public void Set<T>(string key, T value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        _data[key] = element;
    }

    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_data, options);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            _data = new Dictionary<string, JsonElement>();
            return;
        }
        var json = await File.ReadAllTextAsync(_filePath);
        _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? new Dictionary<string, JsonElement>();
    }
}
