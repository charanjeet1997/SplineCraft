# SplineCraft

Spline-based content placement system for Unity (URP/HDRP).
Built on Unity's native `com.unity.splines` package — no third-party dependencies.

---

## Requirements

- Unity 2022.3 LTS or higher
- `com.unity.splines` 2.8.4+
- URP or HDRP (works with both)

---

## Quick Start

Go to **Tools → SplineCraft** in the Unity menu bar.

| Menu Item | What It Does |
|---|---|
| Create Spline Path | Adds a SplineContainer to the scene |
| Add Mesh Deformer | Creates a deformer wired to the nearest SplineContainer |
| Add Instancer | Creates an instancer wired to the nearest SplineContainer |
| Quick Setup — Deformer Scene | One click: SplinePath + MeshDeformer, fully wired |
| Quick Setup — Instancer Scene | One click: SplinePath + Instancer, fully wired |

---

## Two Modes

### SplineMeshDeformer — Continuous Deformation

Bends a mesh continuously along a spline. Best for:
- Walls, barriers, road shoulders
- Pipes, cables, conduits
- Conveyor housings, curved railings

**Setup:**
1. Quick Setup — Deformer Scene
2. Edit the spline knots in Scene view
3. Assign a **Source Mesh** (straight segment, authored along the Z axis by default)
4. Click **Rebuild Mesh**

**Key settings:**
- `Forward Axis` — which local axis of your mesh runs along the spline
- `UV Tiling Scale` — texture units per metre (prevents stretch on curves)
- `Width Curve` / `Height Curve` — AnimationCurves to taper or widen the mesh over its range

---

### SplineInstancer — Repeated Placement

Places a prefab at arc-length-even intervals along a spline. Best for:
- Fence panels, bollards, barriers
- Light poles, signage, mooring points
- Pipe support brackets, railing balusters

**Setup:**
1. Quick Setup — Instancer Scene
2. Edit the spline knots in Scene view
3. Assign an **Item Prefab**
4. Set **Forward Axis** to match which axis of your prefab faces along the path
5. Adjust **Spacing Multiplier** (1 = mesh-width tight, 1.1 = 10% gap)

**Key settings:**
- `Forward Axis` — X, NegX, Y, NegY, Z, NegZ
- `Spacing Multiplier` — multiplied by the prefab's mesh bounds on the forward axis
- `Fit Mode`:
  - `EvenRedistribute` — counts instances, distributes evenly *(recommended)*
  - `FixedSpacingStretchLast` — strict spacing, last gap may vary
  - `FixedCount` — derives count from spacing, ignores leftover
- `Connector Mesh` — optional mesh stretched straight between instances (e.g. fence rail)
- `Output Mode`:
  - `Live Instances` — real GameObjects, editable individually
  - `Baked Mesh` — single combined mesh, better runtime performance
- **Bake to Static** button — converts to baked mesh and disables the component (use before shipping)

---

## Sub-Range Pattern (Multiple Components Per Spline)

Both components have `Start Distance` and `End Distance` fields (metres, -1 = full spline).
Set these to cover only part of the spline — multiple components can share one SplineContainer.

**Example — wall with a gate:**
```
SplineContainer           (draw 120m perimeter path)
  WallA  → SplineMeshDeformer   start=0    end=50
  WallB  → SplineMeshDeformer   start=55   end=120
  Posts  → SplineInstancer      start=0    end=120
```
The 5m gap at 50–55m is where the gate sits. Each component shows its covered range
as a coloured overlay in the Scene view so gaps and overlaps are immediately visible.

---

## Source Mesh Convention (SplineMeshDeformer)

| Forward Axis | Mesh runs along | Mesh should span |
|---|---|---|
| Z (default) | Z+ | -0.5 to +0.5 on Z |
| X | X+ | -0.5 to +0.5 on X |
| Y | Y+ | -0.5 to +0.5 on Y |

UV.x should run 0→1 along the forward axis — the deformer remaps it to real arc length
so textures don't stretch on curves.

---

## Example Configurations

**Security fence:**
```
SplineInstancer
  Item Prefab       = FencePanel prefab
  Forward Axis      = X  (if panel width runs along X)
  Spacing Multiplier = 1.0
  Fit Mode          = EvenRedistribute
  Output Mode       = LiveInstances → BakedMesh before shipping
```

**Pipe run:**
```
SplineMeshDeformer
  Source Mesh       = StraightPipe mesh (Z axis)
  Forward Axis      = Z
  UV Tiling Scale   = 0.5
```

**Quay edge with mooring points:**
```
SplineContainer           (quay edge path)
  QuayWall → SplineMeshDeformer   (concrete wall mesh)
  Bollards → SplineInstancer      (bollard prefab, spacing multiplier = 1.0)
```

---

## Future Extension Points

- Connector mesh following spline curvature (currently stretches straight)
- Per-instance random rotation/scale variation
- Closed-loop placement wrap-around
- LOD support on Instancer prefabs
- Runtime GPU instancing layer
