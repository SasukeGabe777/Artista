using Artista.Core.Documents;

namespace Artista.Core.History;

/// <summary>
/// Undo/redo stack for one document. Stores mementos (state deltas, not full
/// snapshots) and enforces a configurable memory budget by discarding the
/// oldest undo steps.
/// </summary>
public sealed class HistoryStack
{
    private readonly Document _document;
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    /// <summary>Maximum bytes retained across all mementos (default 512 MB).</summary>
    public long MemoryLimit { get; set; } = 512L * 1024 * 1024;

    public event EventHandler? Changed;

    public HistoryStack(Document document) => _document = document;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<HistoryEntry> UndoEntries => _undo;
    public IReadOnlyList<HistoryEntry> RedoEntries => _redo;

    public string? PeekUndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? PeekRedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>Records a completed, already-applied action.</summary>
    public void Push(HistoryMemento memento, string? iconKey = null)
    {
        _undo.Add(new HistoryEntry(memento.Name, memento, iconKey));
        _redo.Clear();
        TrimToBudget();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var inverse = entry.Memento.Apply(_document);
        _redo.Add(new HistoryEntry(entry.Name, inverse, entry.IconKey));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var inverse = entry.Memento.Apply(_document);
        _undo.Add(new HistoryEntry(entry.Name, inverse, entry.IconKey));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Jumps to a specific point in history. Index is measured in "actions
    /// performed": 0 = initial state, UndoEntries.Count = current state.
    /// </summary>
    public void JumpTo(int targetIndex)
    {
        targetIndex = Math.Clamp(targetIndex, 0, _undo.Count + _redo.Count);
        while (_undo.Count > targetIndex && CanUndo)
            Undo();
        while (_undo.Count < targetIndex && CanRedo)
            Redo();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public long TotalSize =>
        _undo.Sum(e => e.Memento.SizeEstimate) + _redo.Sum(e => e.Memento.SizeEstimate);

    private void TrimToBudget()
    {
        long total = TotalSize;
        while (total > MemoryLimit && _undo.Count > 1)
        {
            total -= _undo[0].Memento.SizeEstimate;
            _undo.RemoveAt(0);
        }
    }
}

public sealed record HistoryEntry(string Name, HistoryMemento Memento, string? IconKey);
