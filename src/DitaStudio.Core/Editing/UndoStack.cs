using DitaStudio.Core.Model;

namespace DitaStudio.Core.Editing;

/// <summary>
/// Отмена и повтор действий на основе снимков документа. Снимок — это XML-текст,
/// поэтому история устойчива к любым изменениям дерева, включая правку исходного кода.
/// </summary>
public sealed class UndoStack
{
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private readonly int _limit;

    public UndoStack(int limit = 100)
    {
        _limit = limit;
    }

    private sealed record Snapshot(string Xml, string Description, string? Caret);

    /// <summary>
    /// Положение курсора, сохранённое со снимком, который восстановила последняя <see cref="Undo"/>/<see cref="Redo"/>
    /// (строка оболочки, ядро её не разбирает; null — не сохранялось).
    /// </summary>
    public string? RestoredCaret { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? NextRedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    public event EventHandler? Changed;

    /// <summary>
    /// Запоминает состояние документа ДО изменения. <paramref name="caret"/> — где поставить курсор, когда этот снимок
    /// вернут отменой. Снимок, совпадающий с последним, не добавляется: между ними ничего не изменилось (например,
    /// команда записала снимок, а затем её же правка текста в блоке начала «ввод») — иначе отмена потребовала бы
    /// лишнего нажатия.
    /// </summary>
    public void Push(DitaDocument document, string description, string? caret = null)
    {
        var xml = document.ToXmlString();
        if (_undo.Count > 0 && _undo[^1].Xml == xml)
        {
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _undo.Add(new Snapshot(xml, description, caret));
        if (_undo.Count > _limit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Возвращает последний снимок. <paramref name="caret"/> — положение курсора сейчас (вернётся при повторе).</summary>
    public bool Undo(DitaDocument document, string? caret = null)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Snapshot(document.ToXmlString(), snapshot.Description, caret));
        Restore(document, snapshot.Xml);
        RestoredCaret = snapshot.Caret;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo(DitaDocument document, string? caret = null)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Snapshot(document.ToXmlString(), snapshot.Description, caret));
        Restore(document, snapshot.Xml);
        RestoredCaret = snapshot.Caret;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Restore(DitaDocument document, string xml)
    {
        var parsed = DitaDocument.Parse(xml);
        document.Root = parsed.Root;
        document.DoctypeName = parsed.DoctypeName;
        document.DoctypePublicId = parsed.DoctypePublicId;
        document.DoctypeSystemId = parsed.DoctypeSystemId;
        document.IsDirty = true;
    }
}
