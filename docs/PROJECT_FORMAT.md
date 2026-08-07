# The `.artz` project format

Artista's native layered format. A `.artz` file is a standard **ZIP archive** — you can open one with any zip tool.

## Contents

```
document.json      required   document + layer metadata
layers/0.png       required   bottom layer pixels (32-bit RGBA PNG)
layers/1.png       …one per layer, index = z-order (0 = bottom)
pasteboard/0.png   optional   reusable pixel piece parked outside the canvas
selection.png      optional   selection mask, 8-bit grayscale PNG (255 = selected)
```

## `document.json`

```json
{
  "Version": 1,
  "Width": 1920,
  "Height": 1080,
  "ActiveLayerIndex": 1,
  "Layers": [
    {
      "Name": "Background",
      "Visible": true,
      "Locked": false,
      "AlphaLocked": false,
      "Opacity": 255,
      "BlendMode": "Normal"
    },
    {
      "Name": "Sketch",
      "Visible": true,
      "Locked": false,
      "AlphaLocked": false,
      "Opacity": 128,
      "BlendMode": "Multiply"
    }
  ],
  "Metadata": { "author": "..." },
  "HasSelection": true,
  "Pasteboard": [
    {
      "Name": "Spare eye",
      "X": -180,
      "Y": 220,
      "Width": 96,
      "Height": 64
    }
  ]
}
```

- `Opacity` is 0–255.
- `BlendMode` is one of `Normal`, `Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`, `Difference`, `Additive`. Unknown values load as `Normal` (forward compatibility).
- `Metadata` is an optional free-form string map.
- `HasSelection` indicates whether `selection.png` should be applied.
- `Pasteboard` is optional. Each entry points to the same-index PNG under `pasteboard/` and stores its position in document coordinates; negative coordinates place pieces left of or above the canvas.

## Semantics & guarantees

- Layer PNGs are full-canvas size with straight (non-premultiplied) alpha; a layer PNG whose size mismatches the canvas is a load error.
- Pasteboard PNGs retain their natural size and alpha. They are saved with the project but excluded from flattened image export until placed back onto a layer.
- Saving uses **safe-save**: content is written to a temp file in the target directory and atomically swapped in (`File.Replace`), so a failed save never destroys the existing file.
- Loading never keeps a file handle open after returning.
- Pasteboard pieces round-trip with their names, positions, dimensions, pixels, and transparency.
- Round-trip (save → load) preserves: canvas size, layer count/order/names/pixels, visibility, opacity, blend modes, lock flags, active layer, selection mask, metadata — pinned by `FileIoTests.ArtzRoundTripPreservesFullDocumentStructure`.

## Versioning

`Version` is 1. Readers should accept files with a version they understand and fail with a clear message otherwise. Add new optional JSON fields freely; bump the version only for breaking changes.

## Why not `.pdn`?

The historical OpenPDN source implements `.pdn` via .NET binary serialization of its own class graph — unreliable to reimplement partially and unsafe to consume with modern .NET. Rather than ship a `.pdn` reader that silently corrupts edge cases, Artista defines this documented, zip-based format. (Flat images in `.pdn` can be exported from Paint.NET as PNG and opened here.)
