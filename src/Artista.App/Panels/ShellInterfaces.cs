using Artista.App.Models;
using Artista.Core.History;
using Artista.Core.Imaging;

namespace Artista.App.Panels;

/// <summary>Services the docked panels use to talk to the main window.</summary>
public interface IShellHost
{
    DocumentWorkspace? ActiveWorkspace { get; }
    ToolEnvironment Env { get; }
    AppSettings Settings { get; }

    void PushHistory(HistoryMemento memento, string? iconKey = null);
    void InvalidateDocument(RectInt rect);
    void RefreshAllPanels();
    void SetStatus(string text);
    void CommitActiveTool();
    void ActivateLayer(int layerId);

    // Layer operations (implemented centrally so menus and panel share them).
    void LayerAdd();
    void LayerDelete();
    void LayerDuplicate();
    void LayerMergeDown();
    void LayerMoveUp();
    void LayerMoveDown();
    void LayerProperties();
}
