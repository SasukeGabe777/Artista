using Artista.Core.Documents;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.Layers;

namespace Artista.Tests;

public class HistoryTests
{
    private static Document MakeDoc(uint fill = 0)
    {
        var doc = new Document(8, 8);
        var layer = new Layer(8, 8, "Background");
        if (fill != 0) layer.Surface.Clear(fill);
        doc.Layers.Add(layer);
        return doc;
    }

    [Fact]
    public void UndoRestoresPixels_RedoReappliesThem()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;
        var rect = new RectInt(0, 0, 4, 4);

        var before = layer.Surface.ExtractRect(rect);
        layer.Surface.FillRect(rect, ColorBgra.Pack(0, 0, 255, 255));
        history.Push(new SurfaceRegionMemento("Fill", layer, rect, before));

        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal(0u, layer.Surface[1, 1]);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), layer.Surface[1, 1]);
    }

    [Fact]
    public void RepeatedUndoRedoPreservesIntegrity()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;

        for (int i = 1; i <= 5; i++)
        {
            var rect = new RectInt(i, i, 2, 2);
            var before = layer.Surface.ExtractRect(rect);
            layer.Surface.FillRect(rect, ColorBgra.Pack((byte)(i * 40), 0, 0, 255));
            history.Push(new SurfaceRegionMemento($"Step {i}", layer, rect, before));
        }
        var finalState = layer.Surface.Clone();

        for (int cycle = 0; cycle < 3; cycle++)
        {
            while (history.CanUndo) history.Undo();
            Assert.Equal(0u, layer.Surface[1, 1]);
            while (history.CanRedo) history.Redo();
            Assert.Equal(finalState.Pixels, layer.Surface.Pixels);
        }
    }

    [Fact]
    public void NewActionClearsRedoStack()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;

        var r1 = new RectInt(0, 0, 2, 2);
        var b1 = layer.Surface.ExtractRect(r1);
        layer.Surface.FillRect(r1, 0xFF0000FFu);
        history.Push(new SurfaceRegionMemento("A", layer, r1, b1));
        history.Undo();
        Assert.True(history.CanRedo);

        var b2 = layer.Surface.ExtractRect(r1);
        layer.Surface.FillRect(r1, 0xFF00FF00u);
        history.Push(new SurfaceRegionMemento("B", layer, r1, b2));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void LayerAddUndoRedo()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var newLayer = new Layer(8, 8, "Layer 2");
        doc.Layers.Add(newLayer);
        doc.ActiveLayerIndex = 1;
        history.Push(new LayerAddedMemento("Add Layer", newLayer));

        history.Undo();
        Assert.Single(doc.Layers);
        history.Redo();
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal("Layer 2", doc.Layers[1].Name);
    }

    [Fact]
    public void LayerReorderUndo()
    {
        var doc = MakeDoc();
        var second = new Layer(8, 8, "L2");
        doc.Layers.Add(second);
        var history = new HistoryStack(doc);

        // Move layer 1 to index 0.
        int oldIndex = 1;
        doc.Layers.RemoveAt(1);
        doc.Layers.Insert(0, second);
        history.Push(new LayerOrderMemento("Reorder", second.Id, oldIndex));

        Assert.Equal("L2", doc.Layers[0].Name);
        history.Undo();
        Assert.Equal("L2", doc.Layers[1].Name);
        history.Redo();
        Assert.Equal("L2", doc.Layers[0].Name);
    }

    [Fact]
    public void LayerPropertiesUndoRestoresOpacityAndName()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;

        history.Push(new LayerPropertiesMemento("Properties", layer));
        layer.Opacity = 100;
        layer.Name = "Renamed";

        history.Undo();
        Assert.Equal(255, layer.Opacity);
        Assert.Equal("Background", layer.Name);
        history.Redo();
        // Redo restores the state at the time properties were changed... the memento
        // captured pre-change state, so redo brings back the changed values.
        Assert.Equal(100, layer.Opacity);
        Assert.Equal("Renamed", layer.Name);
    }

    [Fact]
    public void MemoryLimitTrimsOldestEntries()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc) { MemoryLimit = 2000 };
        var layer = doc.ActiveLayer;

        for (int i = 0; i < 20; i++)
        {
            var rect = new RectInt(0, 0, 8, 8); // 256 bytes each
            var before = layer.Surface.ExtractRect(rect);
            layer.Surface.FillRect(rect, (uint)(0xFF000000u | (uint)i));
            history.Push(new SurfaceRegionMemento($"S{i}", layer, rect, before));
        }
        Assert.True(history.UndoEntries.Count < 20);
        Assert.True(history.TotalSize <= 2000 || history.UndoEntries.Count == 1);
    }

    [Fact]
    public void JumpToWalksHistoryBothDirections()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;

        for (int i = 1; i <= 4; i++)
        {
            var rect = new RectInt(0, 0, 1, 1);
            var before = layer.Surface.ExtractRect(rect);
            layer.Surface[0, 0] = 0xFF000000u + (uint)i;
            history.Push(new SurfaceRegionMemento($"S{i}", layer, rect, before));
        }

        history.JumpTo(1);
        Assert.Equal(0xFF000001u, layer.Surface[0, 0]);
        history.JumpTo(4);
        Assert.Equal(0xFF000004u, layer.Surface[0, 0]);
        history.JumpTo(0);
        Assert.Equal(0u, layer.Surface[0, 0]);
    }

    [Fact]
    public void CompositeMementoAppliesAllChildrenAtomically()
    {
        var doc = MakeDoc();
        var history = new HistoryStack(doc);
        var layer = doc.ActiveLayer;

        var rect = new RectInt(0, 0, 2, 2);
        var pixelsBefore = layer.Surface.ExtractRect(rect);
        var propsBefore = new LayerPropertiesMemento("x", layer);
        layer.Surface.FillRect(rect, 0xFFFF0000u);
        layer.Opacity = 42;
        history.Push(new CompositeMemento("Both", new HistoryMemento[]
        {
            new SurfaceRegionMemento("x", layer, rect, pixelsBefore),
            propsBefore,
        }));

        history.Undo();
        Assert.Equal(0u, layer.Surface[0, 0]);
        Assert.Equal(255, layer.Opacity);
        history.Redo();
        Assert.Equal(0xFFFF0000u, layer.Surface[0, 0]);
        Assert.Equal(42, layer.Opacity);
    }
}
