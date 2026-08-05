# Keyboard shortcuts

Also available in-app: **Help → Keyboard Shortcuts** (or **F1**).

## File
| Shortcut | Action |
|---|---|
| Ctrl+N | New image |
| Ctrl+O | Open |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+E | Export flattened image |
| Ctrl+W | Close document |

## Edit
| Shortcut | Action |
|---|---|
| Ctrl+Z | Undo |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / Copy / Paste (paste creates a new layer) |
| Ctrl+Alt+V | Paste into new image |
| Delete | Clear selected pixels |
| Ctrl+A | Select all |
| Ctrl+D / Ctrl+Shift+A / Esc | Deselect (Esc when nothing is in progress; a plain click with a selection tool also deselects) |
| Ctrl+I | Invert selection |

## Image & layers
| Shortcut | Action |
|---|---|
| Ctrl+R | Resize image |
| Ctrl+Shift+R | Canvas size |
| Ctrl+Shift+X | Crop to selection |
| Ctrl+Shift+F | Flatten |
| Ctrl+Shift+N | New layer |
| Ctrl+Shift+D | Duplicate layer |
| Ctrl+M | Merge layer down |
| F4 | Layer properties |

## View
| Shortcut | Action |
|---|---|
| Mouse wheel | Scroll vertically |
| Shift+wheel | Scroll horizontally |
| Ctrl+wheel | Zoom (cursor-centered) |
| + / − | Zoom in / out |
| Ctrl+B | Fit to window |
| Ctrl+Shift+1 | Actual size (100%) |
| Space+drag / middle-drag | Pan (wheel-scrolling works at the same time) |
| F6 / F7 / F8 | Toggle History / Layers / Colors panels |

## Tools (single keys)
| Key | Tool |
|---|---|
| S | Rectangle Select |
| W | Magic Wand |
| M | Move Selected Pixels |
| B | Paintbrush |
| E | Eraser |
| P | Pencil |
| F | Paint Bucket |
| G | Gradient |
| K | Color Picker |
| Q | Color Remover |
| T | Text |
| H | Pan |
| Z | Zoom |

## While working
| Shortcut | Action |
|---|---|
| Escape | Cancel the active operation (stroke, shape, floating move, text) |
| Enter | Commit the active operation (floating move, curve, text via Escape) |
| X | Swap primary/secondary colors |
| Ctrl (drag with selection tool) | Add to selection |
| Alt (drag with selection tool) | Subtract from selection |
| Shift (drag) | Constrain shape/selection to square/circle; ×10 arrow-key nudge |
| Ctrl+click (Clone Stamp) | Set clone source |
| Right mouse button | Secondary color variant of the action |

Shortcuts are registered centrally in `MainWindow.RegisterShortcuts` / `OnGlobalKeyDown`, so rebinding or making them configurable later is a single-file change.
