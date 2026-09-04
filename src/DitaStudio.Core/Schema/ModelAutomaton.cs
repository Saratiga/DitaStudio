namespace DitaStudio.Core.Schema;

/// <summary>
/// Конечный автомат по контент-модели. Отвечает на три вопроса, нужные редактору:
/// корректна ли последовательность детей, что можно вставить в позицию N,
/// и какой элемент ожидался там, где нашлась ошибка.
/// </summary>
public sealed class ModelAutomaton
{
    private const string AnySymbol = "*";

    private readonly List<List<int>> _epsilon = new();
    private readonly List<List<(string Symbol, int Target)>> _moves = new();
    private readonly int _start;
    private readonly int _accept;
    private bool[] _canReachAccept = Array.Empty<bool>();

    public ModelAutomaton(ContentModel model)
    {
        Model = model;
        _start = NewState();
        var end = Build(model, _start);
        _accept = end;
        ComputeReachability();
        AllowsAny = model is ContentModel.Any;
        AllowsText = model.AllowsText();
        AllowedNames = model.CollectNames().Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public ContentModel Model { get; }

    public bool AllowsAny { get; }

    public bool AllowsText { get; }

    public IReadOnlyList<string> AllowedNames { get; }

    // ------------------------------------------------------------ построение

    private int NewState()
    {
        _epsilon.Add(new List<int>());
        _moves.Add(new List<(string, int)>());
        return _epsilon.Count - 1;
    }

    private void AddEpsilon(int from, int to) => _epsilon[from].Add(to);

    private void AddMove(int from, string symbol, int to) => _moves[from].Add((symbol, to));

    private int Build(ContentModel model, int from)
    {
        switch (model)
        {
            case ContentModel.Empty:
                return from;

            case ContentModel.Pcdata:
                return from;

            case ContentModel.Any:
            {
                var s = NewState();
                AddEpsilon(from, s);
                AddMove(s, AnySymbol, s);
                return s;
            }

            case ContentModel.Name n:
            {
                var s = NewState();
                AddMove(from, n.Value, s);
                return s;
            }

            case ContentModel.Sequence seq:
            {
                var cur = from;
                foreach (var item in seq.Items)
                {
                    cur = Build(item, cur);
                }

                return cur;
            }

            case ContentModel.Choice choice:
            {
                var end = NewState();
                foreach (var item in choice.Items)
                {
                    var branchStart = NewState();
                    AddEpsilon(from, branchStart);
                    var branchEnd = Build(item, branchStart);
                    AddEpsilon(branchEnd, end);
                }

                return end;
            }

            case ContentModel.Repeat rep:
            {
                if (rep.Min == 0 && rep.Max == 1)
                {
                    var end = NewState();
                    var inner = Build(rep.Item, from);
                    AddEpsilon(inner, end);
                    AddEpsilon(from, end);
                    return end;
                }

                if (rep.Min == 0)
                {
                    // (item)*
                    var loop = NewState();
                    AddEpsilon(from, loop);
                    var innerStart = NewState();
                    AddEpsilon(loop, innerStart);
                    var innerEnd = Build(rep.Item, innerStart);
                    AddEpsilon(innerEnd, loop);
                    return loop;
                }

                // (item)+  ==  item, (item)*
                var once = Build(rep.Item, from);
                var loop2 = NewState();
                AddEpsilon(once, loop2);
                var s2 = NewState();
                AddEpsilon(loop2, s2);
                var e2 = Build(rep.Item, s2);
                AddEpsilon(e2, loop2);
                return loop2;
            }

            default:
                return from;
        }
    }

    private void ComputeReachability()
    {
        var count = _epsilon.Count;
        var reverse = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            reverse[i] = new List<int>();
        }

        for (var i = 0; i < count; i++)
        {
            foreach (var t in _epsilon[i])
            {
                reverse[t].Add(i);
            }

            foreach (var (_, t) in _moves[i])
            {
                reverse[t].Add(i);
            }
        }

        _canReachAccept = new bool[count];
        var queue = new Queue<int>();
        _canReachAccept[_accept] = true;
        queue.Enqueue(_accept);
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            foreach (var p in reverse[s])
            {
                if (!_canReachAccept[p])
                {
                    _canReachAccept[p] = true;
                    queue.Enqueue(p);
                }
            }
        }
    }

    // -------------------------------------------------------------- симуляция

    public HashSet<int> StartSet() => Closure(new HashSet<int> { _start });

    private HashSet<int> Closure(HashSet<int> states)
    {
        var stack = new Stack<int>(states);
        var result = new HashSet<int>(states);
        while (stack.Count > 0)
        {
            var s = stack.Pop();
            foreach (var t in _epsilon[s])
            {
                if (result.Add(t))
                {
                    stack.Push(t);
                }
            }
        }

        return result;
    }

    public HashSet<int> Step(HashSet<int> states, string symbol)
    {
        var next = new HashSet<int>();
        foreach (var s in states)
        {
            foreach (var (sym, target) in _moves[s])
            {
                if (sym == symbol || sym == AnySymbol)
                {
                    next.Add(target);
                }
            }
        }

        return Closure(next);
    }

    public bool IsAccepting(HashSet<int> states) => states.Contains(_accept);

    public bool CanComplete(HashSet<int> states) => states.Any(s => _canReachAccept[s]);

    /// <summary>Имена, допустимые непосредственно в данном состоянии.</summary>
    public IEnumerable<string> ExpectedAt(HashSet<int> states)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in states)
        {
            foreach (var (sym, _) in _moves[s])
            {
                if (sym != AnySymbol)
                {
                    result.Add(sym);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Проверяет последовательность имён. Возвращает индекс первого «лишнего» ребёнка
    /// или -1, если ошибок нет; в <paramref name="expected"/> — что ожидалось.
    /// </summary>
    public bool Validate(IReadOnlyList<string> names, out int errorIndex, out IReadOnlyList<string> expected)
    {
        var states = StartSet();
        for (var i = 0; i < names.Count; i++)
        {
            var next = Step(states, names[i]);
            if (next.Count == 0)
            {
                errorIndex = i;
                expected = ExpectedAt(states).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                return false;
            }

            states = next;
        }

        if (!IsAccepting(states))
        {
            errorIndex = names.Count;
            expected = ExpectedAt(states).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return false;
        }

        errorIndex = -1;
        expected = Array.Empty<string>();
        return true;
    }

    /// <summary>Можно ли вставить элемент <paramref name="candidate"/> на позицию index.</summary>
    public bool CanInsertAt(IReadOnlyList<string> names, int index, string candidate)
    {
        if (AllowsAny)
        {
            return true;
        }

        var states = StartSet();
        for (var i = 0; i < index && i < names.Count; i++)
        {
            states = Step(states, names[i]);
            if (states.Count == 0)
            {
                return false; // документ уже невалиден до точки вставки
            }
        }

        states = Step(states, candidate);
        if (states.Count == 0)
        {
            return false;
        }

        for (var i = index; i < names.Count; i++)
        {
            states = Step(states, names[i]);
            if (states.Count == 0)
            {
                return false;
            }
        }

        return CanComplete(states);
    }

    /// <summary>Все элементы, которые можно вставить на позицию index.</summary>
    public IReadOnlyList<string> InsertableAt(IReadOnlyList<string> names, int index)
    {
        if (AllowsAny)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var candidate in AllowedNames)
        {
            if (CanInsertAt(names, index, candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>Можно ли удалить ребёнка с индексом index, не ломая модель.</summary>
    public bool CanRemoveAt(IReadOnlyList<string> names, int index)
    {
        if (AllowsAny || index < 0 || index >= names.Count)
        {
            return AllowsAny;
        }

        var reduced = new List<string>(names.Count - 1);
        for (var i = 0; i < names.Count; i++)
        {
            if (i != index)
            {
                reduced.Add(names[i]);
            }
        }

        return Validate(reduced, out _, out _);
    }
}
