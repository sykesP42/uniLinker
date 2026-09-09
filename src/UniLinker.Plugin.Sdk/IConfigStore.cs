namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Key-value configuration store backed by a JSON file.
/// Plugins use this to persist settings across sessions.
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Retrieve a configuration value by key, deserialized to type T.
    /// Returns a default T instance if the key is missing or deserialization fails.
    /// </summary>
    T Get<T>(string key) where T : new();

    /// <summary>Store a configuration value by key.</summary>
    void Set<T>(string key, T value);

    /// <summary>Persist all in-memory configuration to disk.</summary>
    Task SaveAsync();
}
