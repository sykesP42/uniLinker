using System.Reflection;
using System.Runtime.Loader;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PluginLoader
{
    public List<LoadedPlugin> LoadedPlugins { get; } = new();

    public void DiscoverAndLoad(string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir)) return;

        foreach (var dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var plugin = LoadPlugin(dllPath);
                if (plugin != null)
                    LoadedPlugins.Add(plugin);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to load plugin {dllPath}: {ex.Message}");
            }
        }
    }

    private LoadedPlugin? LoadPlugin(string dllPath)
    {
        var alc = new AssemblyLoadContext(
            Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);

        var assembly = alc.LoadFromAssemblyPath(dllPath);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract)
                continue;

            if (Activator.CreateInstance(type) is IPlugin plugin)
            {
                return new LoadedPlugin(plugin, alc, dllPath);
            }
        }

        alc.Unload();
        return null;
    }
}

public class LoadedPlugin
{
    public IPlugin Plugin { get; }
    public AssemblyLoadContext Context { get; }
    public string AssemblyPath { get; }

    public LoadedPlugin(IPlugin plugin, AssemblyLoadContext context, string assemblyPath)
    {
        Plugin = plugin;
        Context = context;
        AssemblyPath = assemblyPath;
    }
}
