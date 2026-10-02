using DitaStudio.Core.Model;
using DitaStudio.Core.Localization;

namespace DitaStudio.Core.Project;

public sealed partial class DitaProject
{
    // ---------------------------------------------------------------- ключи

    /// <summary>Область видимости ключей (keyscope): свои ключи плюс именованные дочерние
    /// области. Несколько имён keyscope на одном узле — алиасы одной и той же области.</summary>
    private sealed class KeySpace
    {
        public Dictionary<string, KeyDefinition> Keys { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, KeySpace> Scopes { get; } = new(StringComparer.Ordinal);
    }

    public void RebuildKeySpace()
    {
        _rootKeySpace.Keys.Clear();
        _rootKeySpace.Scopes.Clear();
        foreach (var map in Maps.ToList())
        {
            var doc = TryGetDocument(map.FullPath);
            if (doc is null)
            {
                continue;
            }

            CollectKeys(doc, doc.Root, map.FullPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), _rootKeySpace);
        }
    }

    private void CollectKeys(DitaDocument mapDoc, DitaNode node, string mapPath, HashSet<string> visited, KeySpace space)
    {
        foreach (var child in node.ElementChildren())
        {
            var childSpace = ResolveChildKeySpace(child, space);
            RegisterKeys(child, mapPath, childSpace);
            CollectKeysFromNestedMap(child, mapPath, visited, childSpace);
            CollectKeys(mapDoc, child, mapPath, visited, childSpace);
        }
    }

    /// <summary>Если у узла задан keyscope — заводит для него новую область и регистрирует её
    /// под всеми именами-алиасами в родительской; иначе ключи узла идут в ту же область.</summary>
    private static KeySpace ResolveChildKeySpace(DitaNode child, KeySpace space)
    {
        var keyscope = child.GetAttribute("keyscope");
        if (string.IsNullOrWhiteSpace(keyscope))
        {
            return space;
        }

        var childSpace = new KeySpace();
        foreach (var name in keyscope!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            space.Scopes[name] = childSpace;
        }

        return childSpace;
    }

    private static void RegisterKeys(DitaNode child, string mapPath, KeySpace childSpace)
    {
        var keys = child.GetAttribute("keys");
        if (string.IsNullOrWhiteSpace(keys))
        {
            return;
        }

        var href = child.GetAttribute("href");
        var resolved = href is null ? null : RefResolver.ResolvePath(mapPath, href);
        foreach (var key in keys!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!childSpace.Keys.ContainsKey(key))
            {
                childSpace.Keys[key] = new KeyDefinition(
                    key, href, resolved, child, mapPath,
                    child.GetAttribute("scope"), child.GetAttribute("format"));
            }
        }
    }

    /// <summary>Вложенные карты добавляют свои ключи в ту же область (свою — если у узла задан
    /// keyscope).</summary>
    private void CollectKeysFromNestedMap(DitaNode child, string mapPath, HashSet<string> visited, KeySpace childSpace)
    {
        if (child.Name != "mapref" && child.GetAttribute("format") != "ditamap")
        {
            return;
        }

        var href = child.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        var target = RefResolver.ResolvePath(mapPath, href!);
        if (target is null || !File.Exists(target) || !visited.Add(target))
        {
            return;
        }

        var sub = TryGetDocument(target);
        if (sub is not null)
        {
            CollectKeys(sub, sub.Root, target, visited, childSpace);
        }
    }

    /// <summary>Разрешает ключ в корневой области (без учёта keyscope) — как раньше.</summary>
    public KeyDefinition? ResolveKey(string key) => ResolveKey(key, null);

    /// <summary>Разрешает ключ с учётом области (keyscope), в которой находится ссылающийся
    /// топик (см. MapItem.KeyScopeChain) — так у веток с одинаковыми именами ключей могут быть
    /// разные значения. Ключ вида "область.ключ" ищется строго в указанной области (без подъёма
    /// наверх); ключ без точки ищется от ближайшей области цепочки к корневой — первое совпадение
    /// побеждает. Без цепочки (null) — как <see cref="ResolveKey(string)"/>, только в корне.</summary>
    public KeyDefinition? ResolveKey(string key, IReadOnlyList<string>? scopeChain)
    {
        if (key.Contains('.'))
        {
            var segments = key.Split('.');
            var space = _rootKeySpace;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!space.Scopes.TryGetValue(segments[i], out space!))
                {
                    return null;
                }
            }

            return space.Keys.TryGetValue(segments[^1], out var qualified) ? qualified : null;
        }

        if (scopeChain is null || scopeChain.Count == 0)
        {
            return _rootKeySpace.Keys.TryGetValue(key, out var rootDef) ? rootDef : ResolveKeyInReferencedProjects(key);
        }

        var chain = new List<KeySpace> { _rootKeySpace };
        var current = _rootKeySpace;
        foreach (var name in scopeChain)
        {
            if (!current.Scopes.TryGetValue(name, out current!))
            {
                break;
            }

            chain.Add(current);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Keys.TryGetValue(key, out var def))
            {
                return def;
            }
        }

        return ResolveKeyInReferencedProjects(key);
    }

    /// <summary>Сколько всего ключей в проекте, считая вложенные keyscope-области — в отличие от
    /// Keys (только корневая область). Разница между ними — повод показать в UI подсказку, что
    /// часть ключей объявлена внутри keyscope и не попадает в плоский список.</summary>
    public int TotalKeyCount => CountKeys(_rootKeySpace, new HashSet<KeySpace>());

    private static int CountKeys(KeySpace space, HashSet<KeySpace> visited)
    {
        var count = space.Keys.Count;
        foreach (var child in space.Scopes.Values)
        {
            if (visited.Add(child))
            {
                count += CountKeys(child, visited);
            }
        }

        return count;
    }

    /// <summary>Есть ли такой ключ хоть в какой-то области проекта (корневой или вложенной по
    /// keyscope)? В отличие от ResolveKey не требует знания конкретной цепочки областей — для
    /// проверки "ключ вообще существует" при валидации ссылок вне контекста карты, где топик
    /// может встречаться сразу в нескольких ветках с разными областями.</summary>
    public bool KeyExistsAnywhere(string key)
    {
        if (key.Contains('.'))
        {
            return ResolveKey(key) is not null;
        }

        return KeyExistsInSpace(_rootKeySpace, key) || ResolveKeyInReferencedProjects(key) is not null;
    }

    private static bool KeyExistsInSpace(KeySpace space, string key)
    {
        if (space.Keys.ContainsKey(key))
        {
            return true;
        }

        foreach (var child in space.Scopes.Values)
        {
            if (KeyExistsInSpace(child, key))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------- проекты-источники ключей

    /// <summary>Подключает проект-источник ключей и пересканирует источники.</summary>
    public void AddReferencedProject(string path)
    {
        if (Settings.AddReferencedProject(path))
        {
            RebuildReferencedProjects();
        }
    }

    public void RemoveReferencedProject(string path)
    {
        if (Settings.RemoveReferencedProject(path))
        {
            RebuildReferencedProjects();
        }
    }

    /// <summary>Пересканирует все подключённые проекты-источники — вызывается при каждом Scan(),
    /// правки в них подхватываются сами, как и связанный .ditaval.</summary>
    private void RebuildReferencedProjects()
    {
        _referencedProjects.Clear();
        if (!_allowReferencedProjects)
        {
            return;
        }

        foreach (var path in Settings.ReferencedProjectPaths)
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            try
            {
                var referenced = new DitaProject(path, allowReferencedProjects: false);
                referenced.Scan();
                _referencedProjects.Add(referenced);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // недоступный проект-источник пропускаем — не мешаем работе с основным
                Settings.Report(Loc.T("Core_TheKeySourceProject0Is", path, ex.Message));
            }
        }
    }

    private KeyDefinition? ResolveKeyInReferencedProjects(string key)
    {
        foreach (var referenced in _referencedProjects)
        {
            if (referenced.Keys.TryGetValue(key, out var def))
            {
                return def;
            }
        }

        return null;
    }
}
