# Feature: Window Positions

## Scope (v1)

Keep the existing 11 positions; they operate on the **work area** of the target monitor
(see runtime-geometry.md). No new positions are added in v1 — vertical/horizontal thirds,
halves-quadrants, and user-defined zones are post-v1 (configurable-zones milestone).

| Position | Columns | Col span | Rows | Row span | Notes |
|---|---|---|---|---|---|
| `Full` | 1 | 1 | 1 | 1 | whole work area |
| `LeftHalf` | 2 | 1 | 1 | 1 | |
| `MiddleHalf` | 4 | 2 | 1 | 1 | centered 50% width |
| `RightHalf` | 2 | 1 | 1 | 1 | |
| `LeftThird` | 3 | 1 | 1 | 1 | |
| `MiddleThird` | 3 | 1 | 1 | 1 | |
| `RightThird` | 3 | 1 | 1 | 1 | |
| `LeftTwoThirds` | 3 | 2 | 1 | 1 | |
| `RightTwoThirds` | 3 | 2 | 1 | 1 | |
| `LeftQuarter` | 4 | 1 | 1 | 1 | |
| `RightQuarter` | 4 | 1 | 1 | 1 | |

## Math

A position is expressed as a grid `(cols, colStart, rows, rowStart)` over the work area.
This subsumes the current special-cased arithmetic in `Repositioner.CalculatePosition`
(replacing the `(int)(W / 1.5)` and `W * 0.75` hacks).

```
cellW = floor(workWidth / cols);        cellH = floor(workHeight / rows)
x     = workX + colStart * cellW
y     = workY + rowStart * cellH
w     = (colSpan == cols) ? workX + workWidth - x : colSpan * cellW
h     = (rowSpan == rows) ? workY + workHeight - y : rowSpan * cellH
```

The remainder distribution rule: **the trailing cell absorbs the leftover pixels**
(`cellSpan == last → extend to the edge`), so columns always tile the work area exactly
with no gaps and no overflow. Example: 1920 wide / 3 cols → 640+640+640; 1919 → 639+639+641.

Rounding: integer pixels only. Min size guard: if computed `w < 100 || h < 100` the tile
is rejected with "monitor too small for this position" (exit 4) rather than producing a
degenerate window.

## Behavior rules

- Target = work area of the monitor **containing the focused window's center point**
  (center, not top-left — prevents a maximized-then-untilled window snapping to the wrong
  monitor in overlapping/misaligned setups; see runtime-geometry.md).
- Applying a position **never maximizes** the window; it sets an exact geometry.
- Re-applying the same position is idempotent (no-op if already at that rect, within 1px).
- Position names are case-insensitive on the CLI; canonical names above are what
  keybindings and config store.

## Out of scope (post-v1, listed for traceability)

- Vertical positions (`TopHalf`, `BottomHalf`, 4-quadrants, 6-grid) — same grid model
  already supports them; only the table entry is missing.
- Custom user zones (configurable zones milestone): will extend the grid model to
  user-supplied `(cols, rows, cell)` or absolute rect definitions in GSettings.

## Testing

`TileCalculator` is pure: property tests over monitor rects (including odd widths/heights,
work-area insets, multi-monitor offsets) asserting: (a) tiles fit inside the work area,
(b) adjacent positions in the same grid are exactly contiguous, (c) idempotence.
