using System.Reflection;
using System.Runtime.Loader;

namespace DitaStudio.Core.Plugins;

public sealed record PluginLoadResult<T>(IReadOnlyList<T> Instances, IReadOnlyList<string> Warnings) where T : class;

/// <summary>
/// Общий загрузчик плагинов — обобщён по интерфейсу контракта (IValidationRulePlugin,
/// IPublishFormatPlugin, или свой в App — например IAuthorCommandPlugin), поэтому сам ничего не
/// знает о конкретных контрактах и остаётся в ядре, не завязанном на WPF.
///
/// Каждая .dll из папки грузится в свой AssemblyLoadContext (изолированный, чтобы разные плагины
/// не конфликтовали версиями СВОИХ ЗАВИСИМОСТЕЙ); но сам контракт (DitaStudio.Core.dll и то, что
/// плагин ссылается без копирования — Private=false/CopyLocal=false) резолвится через возврат
/// null из Load — тогда рантайм откатывается к уже загруженной в хосте сборке, и типы совпадают
/// (иначе typeof(T).IsAssignableFrom(type) не сработает: "тот же класс, но другая сборка").
/// Сломанный/несовместимый плагин не должен ронять всё приложение — ошибки идут в Warnings.
/// </summary>
public static class PluginLoader
{
    public static PluginLoadResult<T> Load<T>(string pluginsDirectory) where T : class
    {
        var instances = new List<T>();
        var warnings = new List<string>();

        if (!Directory.Exists(pluginsDirectory))
        {
            return new PluginLoadResult<T>(instances, warnings);
        }

        foreach (var dllPath in Directory.EnumerateFiles(pluginsDirectory, "*.dll"))
        {
            try
            {
                var context = new PluginLoadContext(dllPath);
                var assembly = context.LoadFromAssemblyPath(dllPath);

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract || !typeof(T).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        continue;
                    }

                    if (Activator.CreateInstance(type) is T instance)
                    {
                        instances.Add(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(dllPath)}: {ex.Message}");
            }
        }

        return new PluginLoadResult<T>(instances, warnings);
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
