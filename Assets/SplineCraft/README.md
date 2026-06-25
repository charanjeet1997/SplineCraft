# SplineCraft

General-purpose spline-based content placement for Unity (URP/HDRP).
Uses the native `com.unity.splines` package — no third-party dependencies.

---

## Architecture

Mirrors Unreal's component split:

- **`SplineContainer`** (Unity built-in) — owns path data only.
- **`SplineMeshDeformer`** — reads a spline reference, deforms a mesh along a sub-range.
- **`SplineInstancer`** — reads a spline reference, places repeated prefabs along a sub-range.
- **`SplineMathUtils`** — pure static math layer used by both above; no MonoBehaviour deps.

Multiple deformers/instancers can reference the same `SplineContainer` at different ranges —
the same pattern as placing multiple `SplineMeshComponent`s on one Unreal spline.

---

## Source Mesh Axis Convention (`SplineMeshDeformer`)

The source mesh must be authored as a straight segment along one local axis:

| `forwardAxis` | Mesh runs along | Right axis | Up axis |
|---|---|---|---|
| Z (default) | Z+ | X | Y |
| X | X+ | Y | Z |
| Y | Y+ | X | Z |

The mesh should be centered at the origin with the segment spanning `[-0.5, +0.5]` on the forward axis.
UV.x should run 0→1 along the forward axis — the deformer remaps it to real arc length so textures
don't stretch on curves.

---

## Sub-Range Pattern (Unreal-style multi-component)

Set `startDistance` / `endDistance` (in metres) to target only part of the spline.
Leave at -1 to use the full spline length.

**Example — wall with a gate:**
```
SplineContainer (120m total)
  ├─ SplineMeshDeformer  "WallA"  start=0   end=50   (wall mesh)
  └─ SplineMeshDeformer  "WallB"  start=55  end=120  (wall mesh)
  (5m gap at 50-55m = gate opening)
```

Each component shows its range as a coloured overlay in the Scene view so gaps/overlaps are obvious.

---

## Mode 1: SplineMeshDeformer — when to use

Continuous deformation — the source mesh bends to follow the spline.

Best for: walls, pipes, cables, road shoulders, conveyor housings, curved railings.

Key settings:
- `sourceMesh` — straight-segment mesh (see axis convention above)
- `forwardAxis` — which local axis is the mesh's "length"
- `uvTilingScale` — texture units per metre (prevents stretch)
- `widthCurve` / `heightCurve` — AnimationCurves to taper/widen over the range

---

## Mode 2: SplineInstancer — when to use

Discrete repeated placement at arc-length-even intervals.

Best for: fence posts, bollards, light poles, pipe brackets, mooring points, railing balusters.

Key settings:
- `itemPrefab` — placed at each position, oriented to spline tangent/up
- `spacing` — arc-length distance between instances (metres)
- `fitMode`:
  - `EvenRedistribute` — counts instances, then redistributes evenly (recommended default)
  - `FixedSpacingStretchLast` — strict spacing, last gap may differ
  - `FixedCount` — derives count from spacing, ignores leftover distance
- `connectorMesh` — optional mesh stretched straight between consecutive instances (e.g. fence rail)
- `outputMode`:
  - `LiveInstances` — actual GameObjects, editable individually
  - `BakedMesh` — single combined mesh (auto-chunked at 64k verts), better runtime perf

**Example — fence:**
```
SplineInstancer  "Fence"
  itemPrefab    = FencePost prefab
  connectorMesh = FenceRail mesh
  spacing       = 2.4m
  fitMode       = EvenRedistribute
  outputMode    = LiveInstances (switch to BakedMesh before shipping)
```

---

## Future Extension Points

The following were intentionally left out of v1 to keep scope clean:

- **Connector mesh following spline curvature** — currently connectors stretch straight between points.
  Following the arc between posts would require SplineMeshDeformer-style logic per gap.
- **Per-instance random rotation/scale variation** — add a `RandomSeed` + variance fields to Instancer.
- **LOD support** — Instancer could accept multiple prefabs at different LOD distances.
- **Runtime DOTS/GPU-instancing** — current output is plain MonoBehaviours; convert baked meshes to
  GPU instancing layer separately per use case.
- **Closed-loop placement** — Instancer placement on fully closed splines (wrap-around last gap).
