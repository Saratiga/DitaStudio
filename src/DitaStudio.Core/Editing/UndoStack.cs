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

    private sealed record Snapshot(string Xml, string Description);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? NextRedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    public event EventHandler? Changed;

    /// <summary>Запоминает состояние документа ДО изменения.</summary>
    public void Push(DitaDocument document, string description)
    {
        _undo.Add(new Snapshot(document.ToXmlString(), description));
        if (_undo.Count > _limit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo(DitaDocument document)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Snapshot(document.ToXmlString(), snapshot.Description));
        Restore(document, snapshot.Xml);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo(DitaDocument document)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Snapshot(document.ToXmlString(), snapshot.Description));
        Restore(document, snapshot.Xml);
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
