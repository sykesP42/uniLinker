namespace UniLinker.Plugin.Sdk;

public interface IConfigStore
{
    T Get<T>(string key) where T : new();
    void Set<T>(string key, T value);
    Task SaveAsync();
}
