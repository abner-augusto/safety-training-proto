# Dashboard scene layout: walkable floors and scaffold markers

## Goal

The evaluator dashboard viewport shows only the tracked participant over a grid
at y = 0. It knows nothing about the scene. Two consequences:

- In the scaffold phase the participant stands at y ≈ 21.7 m, so the grid,
  floor disc and plumb line sit 21 m below them, and the posture chip
  (`headY ≤ 1.15` = crouched) always reads "standing".
- The evaluator cannot see where the scaffold-inspection tasks happen or
  whether each one is still pending.

This change sends a static **scene layout** to the dashboard: the walkable floor
rectangles of both phases, and four task markers for the scaffold-inspection
group (guardrail, toeboard, anchor point, safety-mesh irregularity). Each marker
shows its location and a pending/completed state.

## Success criteria

1. The canteiro and the scaffold deck each appear as a translucent rectangle at
   their real floor height, with their dimensions labelled on the edges.
2. Grid, floor disc and plumb line sit on the floor under the participant.
3. Posture is computed from head height above that floor: crouching on the
   scaffold deck reads "Agachado".
4. Once the `scaffold_inspection` group starts, the four markers appear in
   amber. Each one turns neutral when its task completes.
5. A dashboard that connects mid-session receives the layout and renders the
   current marker states.

## Scene facts (measured in the loaded scene, 2026-10-11)

| Element | Object | World geometry |
|---|---|---|
| Canteiro floor | `[Enviroment]/floor house` (ProBuilder quad, MeshCollider) | X −3.33…4.00, Z −1.93…3.25 (7.33 × 5.18 m), y = 0.05 |
| Scaffold deck | `[Enviroment]/andaime array point_ignore/ScaffoldCollapsePivot/andaime_Array_008/andaime.piso` (BoxCollider) | X −5.21…0.72, Z 12.01…12.91 (5.93 × 0.90 m), top y = 21.728 |
| Guardrail install spot | `GuardRailSlot` (preview renderer of `[Interactive Objects]/GuardRailPiece`, `install_guardrail`) | centre (0.79, 22.87, 12.45), 0.05 × 0.20 × 1.10 m |
| Toeboard install spot | `ScaffoldSlot` (preview renderer of `[Interactive Objects]/ScaffoldPiece`, `install_toeboard`) | centre (−0.75, 21.78, 11.98), 3.09 × 0.30 × 0.05 m |
| Anchor point | `[Interactive Objects]/LanyardAnchor` (`AnchorPoint`, correct) | (−0.23, 22.93, 13.05), ~8 cm |
| Safety-mesh irregularity | `[Interactive Objects]/MeshRip_Location` (`SafetyIssueReporter`) | (0.11, 23.02, 11.92), 15 × 5 cm renderer |

Task ids in the scenario (`scaffold_inspection` group): `connect_lanyard`,
`install_guardrail`, `install_toeboard`, `report_damaged_safety_mesh`.

The deck lives under `ScaffoldCollapsePivot`, which moves during the collapse
consequence. The layout is captured once and treated as static: a collapse
ends the session.

## Design

### Unity: one authoring component

`DashboardSceneLayout` (MonoBehaviour, Networking assembly, next to
`EvaluatorDashboardBootstrap`) is placed once in the scene, on a new GameObject
under `[GameManagers]`. It holds two lists:

- `floors`: entries `{ id, source: Component }`, using the same bounds rules as
  markers. The rectangle is the top face of the source's **local** bounds, transformed to world space. The centre is
  the top-face centre, the size is X/Z extent × lossy scale, and the yaw is the
  source transform's Y rotation. Using local bounds keeps a rotated floor
  correct.
- `markers`: entries `{ id, kind, taskId, source: Component, minSize }`.
  - `kind` is an enum: `Guardrail`, `Toeboard`, `Anchor`, `Irregularity`.
  - `taskId` is a string matching the scenario task id.
  - `source` is a Component whose local bounds give an oriented box (centre,
    size, rotation). The bounds come from `Renderer.localBounds` for a
    Renderer, from `center`/`size` for a BoxCollider, and from the
    `sharedMesh` bounds for a MeshCollider. Any other component gives a point
    at its transform with size 0.
  - Each axis of the size is clamped up to `minSize` so small items stay
    visible. The default is 0.12 m.

Initial authoring:

- floors: `canteiro` → `floor house`; `andaime` → `andaime.piso` (Array_008).
- markers:
  - `guardrail` (`install_guardrail`) → `GuardRailSlot`
  - `toeboard` (`install_toeboard`) → `ScaffoldSlot`
  - `anchor` (`connect_lanyard`) → `LanyardAnchor`
  - `irregularity` (`report_damaged_safety_mesh`) → `MeshRip_Location` renderer

Geometry extraction is a pure static method over
`(Matrix4x4 localToWorld, Bounds localBounds)`, so EditMode tests can cover it
without a scene.

The layout is built once, on first request, and cached. Pieces move
(guardrail, toeboard), but their install **spots** do not, and the markers
point at the spots.

### Wire protocol: `SceneLayout`

A new DTO lives in `DashboardDtos.cs`. Positions are Unity world space, and the
dashboard mirrors Z the same way it does for poses.

```json
{
  "floors":  [{ "id": "andaime", "px": -2.246, "py": 21.728, "pz": 12.458,
                "sx": 5.925, "sz": 0.902,
                "qx": 0, "qy": 0, "qz": 0, "qw": 1 }],
  "markers": [{ "id": "guardrail", "kind": "Guardrail", "taskId": "install_guardrail",
                "px": 0.791, "py": 22.870, "pz": 12.446,
                "sx": 0.12, "sy": 0.20, "sz": 1.098,
                "qx": 0, "qy": 0, "qz": 0, "qw": 1 }]
}
```

Floors carry a full rotation, not a yaw angle, so they go through the same
Unity → Three conversion as every other pose. In practice the rotation is a
yaw-only rotation.

Sending: `HandleRequestSync` sends `SceneLayout` **before** its session-id
early return. The dashboard sends `RequestSync` on every (re)connect, so it
has the floors even before a session starts. A session reset reloads the scene
with identical geometry, and the dashboard keeps its layout across
`SessionReset`, so there is no rebroadcast on `SessionStarted`. The bootstrap
is `DontDestroyOnLoad` while the layout component is a scene object, so the
bootstrap looks the component up on each request (`FindFirstObjectByType`),
as it already does for `TaskManager`.

The layout carries no task state and no group. The dashboard derives both from
the manifest it already has. The marker's state comes from its task's status.
Its visibility comes from the task's `groupName`: the marker shows once any
task in that group is `active` or `completed`. This rule works for a dashboard
that connects mid-session, where no `GroupStarted` is replayed.

### Dashboard (`Tools/DashboardSrc`, then synced)

New module `js/scene-layout.js` owns the layout meshes, so `viewport.js` (742
lines) does not grow further. `EvaluatorViewport` exposes:

- `setLayout(layout)`: (re)builds floors and markers.
- `refreshMarkers(tasks)`: takes the `state.tasks` Map and recomputes every
  marker's state and visibility. The router calls it after `SceneLayout`,
  `SessionManifest`, `TaskStarted`, `TaskCompleted` and `SessionReset`.
- Pure logic (the floor lookup, marker state and visibility) lives in
  `js/layout-math.js`, which imports nothing, so `node --test` can test it.
- `floorAt(x, z, headY)`: the highest floor whose rectangle, grown by a
  0.3 m margin, contains (x, z) and whose y is below `headY`. Otherwise returns
  null, which means ground y = 0. The margin covers a participant leaning over
  the 0.90 m deck edge.

Rendering:

- **Floor:** a translucent fill (accent at low opacity) and a solid outline, plus
  two dimension labels on adjacent edges in the existing label overlay
  (`5,93 m`, `0,90 m`, PT decimal comma).
- **Markers:** a box with `EdgesGeometry` and a faint fill. Pending is amber,
  using the loose-PPE materials; completed is neutral, using the worn-PPE
  materials. Each marker carries a PT label from i18n: `Guarda-corpo`,
  `Rodapé`, `Ponto de ancoragem`, `Irregularidade`. EN strings go into the
  toggle table.
- **Visibility:** a marker is hidden until its group is active or completed.
- **Floor-relative body:** `_updateBody` computes `floorY = floorAt(H.x, H.z, H.y)`.
  The grid, disc and plumb-line bottom move to `floorY`, and the grid only
  moves when `floorY` changes. The posture uses `headY − floorY` with the
  current thresholds (1.15 / 1.42), and the chip shows that relative height.
- Theme changes re-apply colours to the layout materials. `clearSession` keeps
  the layout, because the geometry is scene-static, and resets markers to
  pending.

### Error handling

- An empty source, missing collider or missing renderer: the entry is skipped
  with a `SafetyLog.Warning` naming it (an English engine diagnostic, not
  operator-facing).
- No `DashboardSceneLayout` in the scene: no message is sent, and the dashboard
  behaves as today (floor 0, no markers).
- A malformed or missing layout on the dashboard is ignored, and the viewport
  keeps working.

## Testing

- **EditMode:**
  - Geometry extraction: an axis-aligned box, a yawed box, a scaled box, the
    top-face height, and the `minSize` clamp.
  - DTO serialization field names.
- **Node (`node --test`):** `layout-math.js`. This covers the floor lookup
  (inside, the margin, below the head, the highest floor winning, a rotated
  floor) and the marker state and visibility rules (unknown task, group not
  started, active, completed, `not_performed`).
- **Browser:**
  - Inject `SceneLayout` and poses through `__spRoute` / `__spPose`.
  - Participant standing and crouching on the deck: the chip changes, the grid
    sits at 21.73.
  - Participant on the canteiro: floor 0.05.
  - The first `TaskStarted` of the scaffold group shows the markers, and
    `TaskCompleted` turns each one neutral.
  - Both light and dark themes.
- **Editor Play:** the dashboard at `localhost:8080` receives the real layout,
  and the floors line up with the participant at both spawns.

## Out of scope

- Incorrect anchors, wrong-order or error states (option 3 in the discussion).
- Walk-area boundary violations: the rectangle is only drawn, not enforced.
- Following the collapse animation.
