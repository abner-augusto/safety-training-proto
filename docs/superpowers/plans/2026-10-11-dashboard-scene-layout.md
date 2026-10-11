# Dashboard Scene Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send the walkable floors of both phases and the four scaffold-inspection task spots to the evaluator dashboard. The viewport draws them, colours each marker by task state, and measures posture from the floor under the participant.

**Architecture:** A scene component, `DashboardSceneLayout`, lists floor and marker sources. A pure helper, `SceneLayoutGeometry`, turns each source's local bounds into world-space DTOs. `EvaluatorDashboardBootstrap` sends the resulting `SceneLayout` message on every `RequestSync`. On the dashboard, a pure module `layout-math.js` (testable under Node) owns floor lookup and marker state. A Three.js module `scene-layout.js` draws them, and `viewport.js` uses the floor height for the grid, the plumb line and the posture.

**Tech Stack:** Unity 6 C# (NUnit EditMode tests, `JsonUtility`), vanilla ES modules + Three.js (dashboard), `node --test` (Node 26) for the pure JS module.

**Spec:** `docs/superpowers/specs/2026-10-11-dashboard-scene-layout-design.md`

## Global Constraints

- Code comments, identifiers and technical docs are English. Evaluator-facing dashboard copy is Portuguese, with an EN entry in the toggle table.
- Comments only where they state a reason, a constraint or a trap (AGENTS.md, "Comments"). No section banners, no change narration.
- Never hand-edit `Assets/_SafetyProto/Resources/Dashboard/**`. Edit `Tools/DashboardSrc/` and regenerate with the menu `SafetyProto → Sync Dashboard Source`, via `unity --format json command menu --path "SafetyProto/Sync Dashboard Source"`.
- Wire field names are protocol. The dashboard reads them off the JSON.
- Unity → Three conversion is the same everywhere: position `(px, py, -pz)`, quaternion `(-qx, -qy, qz, qw)`.
- Thresholds stay as they are: crouched `≤ 1.15 m`, lowering `≤ 1.42 m`, both measured from the floor. The floor margin is `0.3 m`, and the marker `minSize` default is `0.12 m`.
- Commits use Conventional Commits, without `Co-Authored-By`, and only once the user has authorized committing this work.
- After C# changes: recompile through the Unity workflow (`unity --format json command recompile`, then poll `recompile_status`) and run `graphify update .` at the end.

## Review Focus

1. **A source referenced as a bare Transform when it should be a Renderer.** Dragging a GameObject into a `Component` field assigns its Transform, which turns the deck into a zero-size point. Expected: `OnValidate` upgrades a Transform to the Renderer, else the Collider, on the same GameObject. Pinned by `SceneLayoutGeometryTests.TryGetLocalBounds_*` and the Task 3 JSON check.
2. **A participant leaning past the 0.90 m deck edge.** Expected: the floor stays the deck, thanks to the 0.3 m margin, so posture does not flip to "standing at 21 m". Pinned by `layout-math.test.mjs` "margin".
3. **A dashboard opened mid-session, or reloaded after the scaffold group started.** Expected: the markers appear with the correct states with no `GroupStarted` replay. Pinned by `markerState` tests and the Task 5 browser check (layout and manifest injected in either order).
4. **The language toggle and the theme toggle after the layout is drawn.** Expected: the labels switch PT/EN, and the colours follow the theme without rebuilding the layout. Pinned in the Task 5 browser checks.
5. **A `SessionReset`, which reloads the Unity scene.** Expected: the layout stays drawn, the markers hide (no tasks), and the next session works. Pinned by `markerState` with an empty Map and the Task 5 reset check.

---

### Task 1: Wire DTOs and the geometry helper

**Files:**
- Modify: `Assets/_SafetyProto/Scripts/Domain/Dashboard/DashboardDtos.cs` (append before the closing `}` of the namespace)
- Create: `Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/SceneLayoutGeometry.cs`
- Test: `Assets/_SafetyProto/Tests/Editor/SceneLayoutGeometryTests.cs`

**Interfaces:**
- Produces:
  - `SafetyProto.Domain.Dashboard.SceneLayoutDto { SceneFloorDto[] floors; SceneMarkerDto[] markers; }`
  - `SceneFloorDto { string id; float px, py, pz, sx, sz, qx, qy, qz, qw; }`
  - `SceneMarkerDto { string id, kind, taskId; float px, py, pz, sx, sy, sz, qx, qy, qz, qw; }`
  - `SafetyProto.Networking.Dashboard.SceneLayoutGeometry`:
    - `static bool TryGetLocalBounds(Component source, out Bounds bounds)`
    - `static SceneFloorDto Floor(string id, Matrix4x4 localToWorld, Bounds local)`
    - `static SceneMarkerDto Marker(string id, string kind, string taskId, Matrix4x4 localToWorld, Bounds local, float minSize)`

- [ ] **Step 1: Write the failing tests**

Create `Assets/_SafetyProto/Tests/Editor/SceneLayoutGeometryTests.cs`:

```csharp
using NUnit.Framework;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Networking.Dashboard;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class SceneLayoutGeometryTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void Marker_AxisAligned_OffsetsCentreAndKeepsSize()
        {
            var m = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.one);
            var dto = SceneLayoutGeometry.Marker("g", "Guardrail", "install_guardrail", m,
                new Bounds(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 1f, 4f)), 0.12f);

            Assert.AreEqual(1.5f, dto.px, Eps);
            Assert.AreEqual(2f, dto.py, Eps);
            Assert.AreEqual(3f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(1f, dto.sy, Eps);
            Assert.AreEqual(4f, dto.sz, Eps);
            Assert.AreEqual(1f, dto.qw, Eps);
            Assert.AreEqual("Guardrail", dto.kind);
            Assert.AreEqual("install_guardrail", dto.taskId);
        }

        [Test]
        public void Marker_Yawed_RotatesCentreAndKeepsLocalSize()
        {
            var rot = Quaternion.Euler(0f, 90f, 0f);
            var m = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);
            var dto = SceneLayoutGeometry.Marker("g", "Toeboard", "t", m,
                new Bounds(new Vector3(1f, 0f, 0f), new Vector3(2f, 1f, 4f)), 0f);

            Assert.AreEqual(0f, dto.px, Eps);
            Assert.AreEqual(-1f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(4f, dto.sz, Eps);
            Assert.AreEqual(rot.y, dto.qy, Eps);
            Assert.AreEqual(rot.w, dto.qw, Eps);
        }

        [Test]
        public void Marker_Scaled_ScalesCentreAndSize()
        {
            var m = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2f, 1f, 0.5f));
            var dto = SceneLayoutGeometry.Marker("g", "Anchor", "t", m,
                new Bounds(new Vector3(1f, 0f, 2f), new Vector3(1f, 1f, 4f)), 0f);

            Assert.AreEqual(2f, dto.px, Eps);
            Assert.AreEqual(1f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(2f, dto.sz, Eps);
        }

        [Test]
        public void Marker_MinSize_ClampsThinAxesOnly()
        {
            var dto = SceneLayoutGeometry.Marker("i", "Irregularity", "t", Matrix4x4.identity,
                new Bounds(Vector3.zero, new Vector3(0.15f, 0.05f, 0f)), 0.12f);

            Assert.AreEqual(0.15f, dto.sx, Eps);
            Assert.AreEqual(0.12f, dto.sy, Eps);
            Assert.AreEqual(0.12f, dto.sz, Eps);
        }

        [Test]
        public void Floor_UsesTopFaceOfScaffoldDeck()
        {
            // Measured andaime.piso (Array_008): pivot and mesh bounds from the loaded scene.
            var m = Matrix4x4.TRS(new Vector3(-3.844f, 21.713f, 12.461f), Quaternion.identity, Vector3.one);
            var dto = SceneLayoutGeometry.Floor("andaime", m,
                new Bounds(new Vector3(1.599f, 0f, -0.003f), new Vector3(5.925f, 0.030f, 0.902f)));

            Assert.AreEqual("andaime", dto.id);
            Assert.AreEqual(-2.245f, dto.px, 1e-3f);
            Assert.AreEqual(21.728f, dto.py, 1e-3f);
            Assert.AreEqual(12.458f, dto.pz, 1e-3f);
            Assert.AreEqual(5.925f, dto.sx, Eps);
            Assert.AreEqual(0.902f, dto.sz, Eps);
            Assert.AreEqual(1f, dto.qw, Eps);
        }

        [Test]
        public void TryGetLocalBounds_BoxCollider_UsesCentreAndSize()
        {
            var go = new GameObject("box");
            try
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size = new Vector3(2f, 1f, 3f);

                Assert.IsTrue(SceneLayoutGeometry.TryGetLocalBounds(box, out var b));
                Assert.AreEqual(new Vector3(0f, 0.5f, 0f), b.center);
                Assert.AreEqual(new Vector3(2f, 1f, 3f), b.size);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryGetLocalBounds_BareTransform_IsZeroSizePoint()
        {
            var go = new GameObject("point");
            try
            {
                Assert.IsTrue(SceneLayoutGeometry.TryGetLocalBounds(go.transform, out var b));
                Assert.AreEqual(Vector3.zero, b.center);
                Assert.AreEqual(Vector3.zero, b.size);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryGetLocalBounds_Null_ReturnsFalse()
        {
            Assert.IsFalse(SceneLayoutGeometry.TryGetLocalBounds(null, out _));
        }

        [Test]
        public void SceneLayoutDto_SerializesProtocolFieldNames()
        {
            var json = JsonUtility.ToJson(new SceneLayoutDto
            {
                floors = new[] { new SceneFloorDto { id = "andaime", sx = 1f, sz = 2f, qw = 1f } },
                markers = new[] { new SceneMarkerDto { id = "guardrail", kind = "Guardrail", taskId = "install_guardrail", qw = 1f } }
            });

            StringAssert.Contains("\"floors\":[{\"id\":\"andaime\"", json);
            StringAssert.Contains("\"sz\":2.0", json);
            StringAssert.Contains("\"kind\":\"Guardrail\"", json);
            StringAssert.Contains("\"taskId\":\"install_guardrail\"", json);
        }
    }
}
```

- [ ] **Step 2: Recompile and confirm the tests fail to compile**

Run: `unity --format json command recompile`, then poll `unity --format json command recompile_status` until it finishes.
Expected: compile errors `SceneLayoutGeometry` / `SceneLayoutDto` not found.

- [ ] **Step 3: Add the DTOs**

Append inside the `SafetyProto.Domain.Dashboard` namespace of `DashboardDtos.cs`, after `CommandAckDto`:

```csharp
    /// <summary>Static scene geometry for the dashboard viewport, sent on every RequestSync.
    /// Positions and rotations are Unity world space; the dashboard mirrors Z.</summary>
    [Serializable]
    public struct SceneLayoutDto
    {
        public SceneFloorDto[] floors;
        public SceneMarkerDto[] markers;
    }

    /// <summary>Walkable floor rectangle: centre of its top face, X/Z extent, rotation.</summary>
    [Serializable]
    public struct SceneFloorDto
    {
        public string id;
        public float px, py, pz;
        public float sx, sz;
        public float qx, qy, qz, qw;
    }

    /// <summary>Oriented box marking where a task happens. State comes from the task, not from here.</summary>
    [Serializable]
    public struct SceneMarkerDto
    {
        public string id;
        public string kind;
        public string taskId;
        public float px, py, pz;
        public float sx, sy, sz;
        public float qx, qy, qz, qw;
    }
```

- [ ] **Step 4: Add the geometry helper**

Create `Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/SceneLayoutGeometry.cs`:

```csharp
using SafetyProto.Domain.Dashboard;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Converts a component's local bounds into the world-space floor and marker DTOs the
    /// dashboard draws. Pure over (matrix, bounds) so it is testable without a scene.
    /// </summary>
    public static class SceneLayoutGeometry
    {
        /// <summary>Local-space bounds of <paramref name="source"/>: Renderer.localBounds, a
        /// BoxCollider's box, a MeshCollider's mesh bounds, or a zero-size point for any other
        /// component. False only for a missing source.</summary>
        public static bool TryGetLocalBounds(Component source, out Bounds bounds)
        {
            switch (source)
            {
                case null:
                    bounds = default;
                    return false;
                case Renderer renderer:
                    bounds = renderer.localBounds;
                    return true;
                case BoxCollider box:
                    bounds = new Bounds(box.center, box.size);
                    return true;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    bounds = mesh.sharedMesh.bounds;
                    return true;
                default:
                    bounds = new Bounds(Vector3.zero, Vector3.zero);
                    return true;
            }
        }

        /// <summary>Floor rectangle at the top face of <paramref name="local"/>.</summary>
        public static SceneFloorDto Floor(string id, Matrix4x4 localToWorld, Bounds local)
        {
            var top = localToWorld.MultiplyPoint3x4(local.center + Vector3.up * local.extents.y);
            var size = WorldSize(localToWorld, local.size);
            var rot = localToWorld.rotation;
            return new SceneFloorDto
            {
                id = id,
                px = top.x, py = top.y, pz = top.z,
                sx = size.x, sz = size.z,
                qx = rot.x, qy = rot.y, qz = rot.z, qw = rot.w
            };
        }

        /// <summary>Oriented box around <paramref name="local"/>, each axis at least
        /// <paramref name="minSize"/> so thin or point-like spots stay visible.</summary>
        public static SceneMarkerDto Marker(string id, string kind, string taskId,
            Matrix4x4 localToWorld, Bounds local, float minSize)
        {
            var centre = localToWorld.MultiplyPoint3x4(local.center);
            var size = Vector3.Max(WorldSize(localToWorld, local.size), Vector3.one * minSize);
            var rot = localToWorld.rotation;
            return new SceneMarkerDto
            {
                id = id, kind = kind, taskId = taskId,
                px = centre.x, py = centre.y, pz = centre.z,
                sx = size.x, sy = size.y, sz = size.z,
                qx = rot.x, qy = rot.y, qz = rot.z, qw = rot.w
            };
        }

        private static Vector3 WorldSize(Matrix4x4 localToWorld, Vector3 localSize)
        {
            var s = localToWorld.lossyScale;
            return new Vector3(Mathf.Abs(localSize.x * s.x), Mathf.Abs(localSize.y * s.y), Mathf.Abs(localSize.z * s.z));
        }
    }
}
```

- [ ] **Step 5: Recompile and run the tests**

Run: `unity --format json command recompile` (poll `recompile_status`), then
`unity --format json command run_tests --mode editor --filter "SafetyProto.Tests.Editor.SceneLayoutGeometryTests" --timeout 300`
Expected: 9 passed, 0 failed. Also confirm the Domain DTOs still build outside Unity: `dotnet build SafetyProto.sln` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/_SafetyProto/Scripts/Domain/Dashboard/DashboardDtos.cs Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/SceneLayoutGeometry.cs* Assets/_SafetyProto/Tests/Editor/SceneLayoutGeometryTests.cs*
git commit -m "feat(dashboard): add scene layout dtos and geometry helper"
```

---

### Task 2: `DashboardSceneLayout` component and the `SceneLayout` message

**Files:**
- Create: `Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/DashboardSceneLayout.cs`
- Modify: `Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/EvaluatorDashboardBootstrap.cs:296-299` (`HandleRequestSync`)
- Test: `Assets/_SafetyProto/Tests/Editor/DashboardSceneLayoutTests.cs`

**Interfaces:**
- Consumes: `SceneLayoutGeometry.TryGetLocalBounds/Floor/Marker`, `SceneLayoutDto` (Task 1).
- Produces:
  - `enum DashboardMarkerKind { Guardrail, Toeboard, Anchor, Irregularity }`
  - `DashboardSceneLayout.FloorEntry { string id; Component source; }`
  - `DashboardSceneLayout.MarkerEntry { string id; DashboardMarkerKind kind; string taskId; Component source; float minSize; }`
  - `DashboardSceneLayout.Build() : SceneLayoutDto` (cached)
  - `static DashboardSceneLayout.BuildFrom(FloorEntry[] floors, MarkerEntry[] markers, Object logContext) : SceneLayoutDto`
  - The serialized field names `floors` / `markers` are used by the Task 3 authoring script.
  - The WS event name `"SceneLayout"`, consumed by Task 5.

- [ ] **Step 1: Write the failing tests**

Create `Assets/_SafetyProto/Tests/Editor/DashboardSceneLayoutTests.cs`:

```csharp
using NUnit.Framework;
using SafetyProto.Networking.Dashboard;
using UnityEngine;
using UnityEngine.TestTools;

namespace SafetyProto.Tests.Editor
{
    public class DashboardSceneLayoutTests
    {
        private GameObject _go = null!;

        [SetUp]
        public void Setup() => _go = new GameObject("layout-test");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void BuildFrom_SkipsEntriesWithoutSource_AndWarns()
        {
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "ghost", source = null } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost"));
            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(0, dto.floors.Length);
            Assert.AreEqual(0, dto.markers.Length);
        }

        [Test]
        public void BuildFrom_MarkerWithoutMinSize_UsesDefault()
        {
            var markers = new[]
            {
                new DashboardSceneLayout.MarkerEntry
                {
                    id = "anchor", kind = DashboardMarkerKind.Anchor, taskId = "connect_lanyard",
                    source = _go.transform, minSize = 0f
                }
            };

            var dto = DashboardSceneLayout.BuildFrom(new DashboardSceneLayout.FloorEntry[0], markers, null);

            Assert.AreEqual(1, dto.markers.Length);
            Assert.AreEqual("Anchor", dto.markers[0].kind);
            Assert.AreEqual("connect_lanyard", dto.markers[0].taskId);
            Assert.AreEqual(DashboardSceneLayout.DefaultMinSize, dto.markers[0].sx, 1e-5f);
        }

        [Test]
        public void BuildFrom_FloorFromBoxCollider_UsesTopFace()
        {
            _go.transform.position = new Vector3(0f, 10f, 0f);
            var box = _go.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.2f, 1f);
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "deck", source = box } };

            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(10.1f, dto.floors[0].py, 1e-4f);
            Assert.AreEqual(4f, dto.floors[0].sx, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Recompile and confirm it fails to compile**

Run: `unity --format json command recompile` and poll `recompile_status`.
Expected: compile errors `DashboardSceneLayout` / `DashboardMarkerKind` not found.

- [ ] **Step 3: Implement the component**

Create `Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/DashboardSceneLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Dashboard;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>What a dashboard task marker represents; sent as its name.</summary>
    public enum DashboardMarkerKind
    {
        Guardrail,
        Toeboard,
        Anchor,
        Irregularity
    }

    /// <summary>
    /// Authoring list of the walkable floors and task spots the evaluator dashboard draws.
    /// One per scene; <see cref="EvaluatorDashboardBootstrap"/> looks it up on every
    /// RequestSync because the bootstrap outlives scene reloads and this component does not.
    /// </summary>
    public class DashboardSceneLayout : MonoBehaviour
    {
        /// <summary>Smallest drawn extent per axis when an entry leaves minSize at 0.</summary>
        public const float DefaultMinSize = 0.12f;

        [Serializable]
        public struct FloorEntry
        {
            public string id;
            [Tooltip("Renderer, BoxCollider or MeshCollider whose top face is the walkable floor.")]
            public Component source;
        }

        [Serializable]
        public struct MarkerEntry
        {
            public string id;
            public DashboardMarkerKind kind;
            [Tooltip("Scenario task id whose status colours this marker (e.g. install_guardrail).")]
            public string taskId;
            [Tooltip("Renderer or Collider of the task spot; any other component draws a point.")]
            public Component source;
            [Tooltip("Minimum drawn size per axis, in metres. 0 uses the default.")]
            [Min(0f)] public float minSize;
        }

        [SerializeField] private FloorEntry[] floors = Array.Empty<FloorEntry>();
        [SerializeField] private MarkerEntry[] markers = Array.Empty<MarkerEntry>();

        private SceneLayoutDto? _cached;

        /// <summary>The layout DTO, built on first call. Install spots do not move, so it is cached.</summary>
        public SceneLayoutDto Build() => _cached ??= BuildFrom(floors, markers, this);

        public static SceneLayoutDto BuildFrom(FloorEntry[] floors, MarkerEntry[] markers, UnityEngine.Object logContext)
        {
            var floorDtos = new List<SceneFloorDto>();
            foreach (var entry in floors ?? Array.Empty<FloorEntry>())
            {
                if (!SceneLayoutGeometry.TryGetLocalBounds(entry.source, out var bounds))
                {
                    SafetyLog.Warning($"[DashboardSceneLayout] Floor '{entry.id}' has no source; skipped.", logContext);
                    continue;
                }
                floorDtos.Add(SceneLayoutGeometry.Floor(entry.id, entry.source.transform.localToWorldMatrix, bounds));
            }

            var markerDtos = new List<SceneMarkerDto>();
            foreach (var entry in markers ?? Array.Empty<MarkerEntry>())
            {
                if (!SceneLayoutGeometry.TryGetLocalBounds(entry.source, out var bounds))
                {
                    SafetyLog.Warning($"[DashboardSceneLayout] Marker '{entry.id}' has no source; skipped.", logContext);
                    continue;
                }
                var minSize = entry.minSize > 0f ? entry.minSize : DefaultMinSize;
                markerDtos.Add(SceneLayoutGeometry.Marker(entry.id, entry.kind.ToString(), entry.taskId,
                    entry.source.transform.localToWorldMatrix, bounds, minSize));
            }

            return new SceneLayoutDto { floors = floorDtos.ToArray(), markers = markerDtos.ToArray() };
        }

#if UNITY_EDITOR
        // Dropping a GameObject on a Component field assigns its Transform, which would turn a
        // floor into a zero-size point. Prefer the Renderer, then any Collider, on that object.
        private void OnValidate()
        {
            for (int i = 0; i < floors.Length; i++) floors[i].source = Upgrade(floors[i].source);
            for (int i = 0; i < markers.Length; i++) markers[i].source = Upgrade(markers[i].source);
            _cached = null;
        }

        private static Component Upgrade(Component source)
        {
            if (!(source is Transform t)) return source;
            if (t.TryGetComponent<Renderer>(out var renderer)) return renderer;
            if (t.TryGetComponent<Collider>(out var collider)) return collider;
            return source;
        }
#endif
    }
}
```

- [ ] **Step 4: Send it on RequestSync**

In `EvaluatorDashboardBootstrap.HandleRequestSync`, insert at the very top, **before** `var sessionId = EventContext.CurrentSessionId;` and its early return:

```csharp
            // Scene geometry does not depend on a session: send it even before one starts.
            var sceneLayout = FindFirstObjectByType<DashboardSceneLayout>();
            if (sceneLayout != null)
                _wsServer.SendToClient(client, "SceneLayout", sceneLayout.Build());
```

- [ ] **Step 5: Recompile and run the tests**

Run: `unity --format json command recompile` (poll), then
`unity --format json command run_tests --mode editor --filter "SafetyProto.Tests.Editor.DashboardSceneLayoutTests" --timeout 300`
Expected: 3 passed. Re-run `SceneLayoutGeometryTests`: 9 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/DashboardSceneLayout.cs* Assets/_SafetyProto/Scripts/Networking/EvaluatorDashboard/EvaluatorDashboardBootstrap.cs Assets/_SafetyProto/Tests/Editor/DashboardSceneLayoutTests.cs*
git commit -m "feat(dashboard): send scene layout on sync"
```

---

### Task 3: Author the layout in `SafetyTraining.unity`

This changes the scene. It runs through `eval` with Undo and an explicit save. Per `.agents/guides/unity-automation.md`, persistent Editor changes need the user's approval: confirm before Step 2.

**Files:**
- Modify: `Assets/_SafetyProto/Scene/SafetyTraining.unity`. Do not read or grep this file. Use the Editor only.
- Scratch: `<scratchpad>/author_layout.cs` (not committed)

**Interfaces:**
- Consumes: `DashboardSceneLayout` with the serialized arrays `floors` / `markers` and the entry fields `id`, `source`, `kind`, `taskId`, `minSize` (Task 2).
- Produces: one `[GameManagers]/DashboardSceneLayout` GameObject whose `Build()` yields 2 floors and 4 markers.

- [ ] **Step 1: Confirm the sources resolve uniquely**

Write `<scratchpad>/check_sources.cs`:

```csharp
var sb = new System.Text.StringBuilder();
foreach (var n in new[] { "GuardRailSlot", "ScaffoldSlot" })
{
    int c = 0;
    foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
        if (r.name == n) c++;
    sb.AppendLine(n + " renderers=" + c);
}
foreach (var p in new[] { "/[Enviroment]/floor house", "/[Enviroment]/andaime array point_ignore/ScaffoldCollapsePivot/andaime_Array_008/andaime.piso", "/[Interactive Objects]/LanyardAnchor", "/[Interactive Objects]/MeshRip_Location", "/[GameManagers]" })
{
    var go = UnityEngine.GameObject.Find(p);
    sb.AppendLine(p + " found=" + (go != null) + " childRenderer=" + (go != null && go.GetComponentInChildren<UnityEngine.Renderer>(true) != null));
}
return sb.ToString();
```

Run: `unity --format json command eval --code "$(cat <scratchpad>/check_sources.cs)" --timeout 20000`
Expected: each slot `renderers=1`, every path `found=True`, every `childRenderer=True` except `[GameManagers]`. Stop and report if anything differs.

- [ ] **Step 2: Create and populate the component**

Write `<scratchpad>/author_layout.cs`:

```csharp
System.Type layoutType = null, kindType = null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
{
    System.Type[] ts; try { ts = asm.GetTypes(); } catch { continue; }
    foreach (var t in ts)
    {
        if (t.FullName == "SafetyProto.Networking.Dashboard.DashboardSceneLayout") layoutType = t;
        if (t.FullName == "SafetyProto.Networking.Dashboard.DashboardMarkerKind") kindType = t;
    }
}
if (layoutType == null) return "DashboardSceneLayout type not found";
if (UnityEngine.Object.FindFirstObjectByType(layoutType) != null) return "already present; aborting";

UnityEngine.Renderer Slot(string n)
{
    foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
        if (r.name == n) return r;
    return null;
}
UnityEngine.Renderer Child(string path) => UnityEngine.GameObject.Find(path).GetComponentInChildren<UnityEngine.Renderer>(true);

var go = new UnityEngine.GameObject("DashboardSceneLayout");
UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add DashboardSceneLayout");
go.transform.SetParent(UnityEngine.GameObject.Find("/[GameManagers]").transform, false);
var comp = UnityEditor.Undo.AddComponent(go, layoutType);
var so = new UnityEditor.SerializedObject(comp);

var floors = new (string id, UnityEngine.Component src)[]
{
    ("canteiro", UnityEngine.GameObject.Find("/[Enviroment]/floor house").GetComponent<UnityEngine.Renderer>()),
    ("andaime", UnityEngine.GameObject.Find("/[Enviroment]/andaime array point_ignore/ScaffoldCollapsePivot/andaime_Array_008/andaime.piso").GetComponent<UnityEngine.Renderer>()),
};
var fp = so.FindProperty("floors");
fp.arraySize = floors.Length;
for (int i = 0; i < floors.Length; i++)
{
    var e = fp.GetArrayElementAtIndex(i);
    e.FindPropertyRelative("id").stringValue = floors[i].id;
    e.FindPropertyRelative("source").objectReferenceValue = floors[i].src;
}

var markers = new (string id, string kind, string task, UnityEngine.Component src)[]
{
    ("guardrail", "Guardrail", "install_guardrail", Slot("GuardRailSlot")),
    ("toeboard", "Toeboard", "install_toeboard", Slot("ScaffoldSlot")),
    ("anchor", "Anchor", "connect_lanyard", Child("/[Interactive Objects]/LanyardAnchor")),
    ("irregularity", "Irregularity", "report_damaged_safety_mesh", Child("/[Interactive Objects]/MeshRip_Location")),
};
var mp = so.FindProperty("markers");
mp.arraySize = markers.Length;
for (int i = 0; i < markers.Length; i++)
{
    var e = mp.GetArrayElementAtIndex(i);
    e.FindPropertyRelative("id").stringValue = markers[i].id;
    e.FindPropertyRelative("kind").enumValueIndex = (int)System.Enum.Parse(kindType, markers[i].kind);
    e.FindPropertyRelative("taskId").stringValue = markers[i].task;
    e.FindPropertyRelative("source").objectReferenceValue = markers[i].src;
    e.FindPropertyRelative("minSize").floatValue = 0f;
}
so.ApplyModifiedProperties();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
var dto = layoutType.GetMethod("Build").Invoke(comp, null);
return UnityEngine.JsonUtility.ToJson(dto);
```

Run: `unity --format json command eval --code "$(cat <scratchpad>/author_layout.cs)" --timeout 20000`

Expected JSON (values ±0.01):
- `canteiro`: px 0.34, py 0.05, pz 0.66, sx 7.33, sz 5.18
- `andaime`: px −2.25, py 21.73, pz 12.46, sx 5.93, sz 0.90
- `guardrail` centre ≈ (0.79, 22.87, 12.45)
- `toeboard` centre ≈ (−0.75, 21.78, 11.98)
- `anchor` centre ≈ (−0.23, 22.93, 13.12)
- `irregularity` centre ≈ (0.11, 23.02, 11.92), sy and sz ≥ 0.12

If a value is off, `Undo.PerformUndo()` via eval, fix the script, and retry.

- [ ] **Step 3: Save the scene**

Run: `unity --format json command save_scene`
Then `unity --format json command list_open_scenes` → `SafetyTraining` with `isDirty: false`.

- [ ] **Step 4: Commit**

```bash
git add Assets/_SafetyProto/Scene/SafetyTraining.unity
git commit -m "content(dashboard): author scene layout floors and scaffold markers"
```

---

### Task 4: `layout-math.js`, the pure dashboard logic

**Files:**
- Create: `Tools/DashboardSrc/js/layout-math.js`
- Test: `Tools/DashboardSrc/tests/layout-math.test.mjs` (outside `js/`, so the sync does not mirror it)

**Interfaces:**
- Consumes: the `SceneLayout` payload shape from Task 1 (`floors[]`, `markers[]`), and the `state.tasks` Map values `{ id, groupName, status }` from `js/state.js`.
- Produces:
  - `FLOOR_MARGIN = 0.3`
  - `toThreeFloor(dto) → { id, cx, cy, cz, sx, sz, qx, qy, qz, qw }` (Three space)
  - `toThreeMarker(dto) → { id, kind, taskId, cx, cy, cz, sx, sy, sz, qx, qy, qz, qw }`
  - `floorAt(floors, x, z, headY, margin = FLOOR_MARGIN) → floor | null`
  - `markerState(taskId, tasks) → { visible: boolean, done: boolean }`
  - `formatMeters(value, lang) → string` (`'5,93 m'` in pt, `'5.93 m'` in en)

- [ ] **Step 1: Write the failing tests**

Create `Tools/DashboardSrc/tests/layout-math.test.mjs`:

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toThreeFloor, toThreeMarker, floorAt, markerState, formatMeters } from '../js/layout-math.js';

const deck = toThreeFloor({ id: 'andaime', px: -2.246, py: 21.728, pz: 12.458, sx: 5.925, sz: 0.902, qx: 0, qy: 0, qz: 0, qw: 1 });
const yard = toThreeFloor({ id: 'canteiro', px: 0.336, py: 0.05, pz: 0.661, sx: 7.328, sz: 5.178, qx: 0, qy: 0, qz: 0, qw: 1 });

test('toThreeFloor mirrors Z on position and rotation', () => {
  const f = toThreeFloor({ id: 'f', px: 1, py: 2, pz: 3, sx: 4, sz: 5, qx: 0.1, qy: 0.2, qz: 0.3, qw: 0.9 });
  assert.deepEqual([f.cx, f.cy, f.cz, f.qx, f.qy, f.qz, f.qw], [1, 2, -3, -0.1, -0.2, 0.3, 0.9]);
});

test('toThreeMarker keeps kind, task and size', () => {
  const m = toThreeMarker({ id: 'g', kind: 'Guardrail', taskId: 'install_guardrail', px: 0, py: 1, pz: 2, sx: 0.1, sy: 0.2, sz: 1.1, qx: 0, qy: 0, qz: 0, qw: 1 });
  assert.equal(m.kind, 'Guardrail');
  assert.equal(m.taskId, 'install_guardrail');
  assert.equal(m.cz, -2);
  assert.equal(m.sz, 1.1);
});

test('floorAt finds the deck under a participant standing on it', () => {
  assert.equal(floorAt([yard, deck], -1.0, -12.5, 23.3)?.id, 'andaime');
});

test('floorAt margin keeps the deck while leaning past its 0.90 m edge', () => {
  assert.equal(floorAt([deck], -1.0, -12.458 - 0.6, 23.3)?.id, 'andaime');
  assert.equal(floorAt([deck], -1.0, -12.458 - 0.8, 23.3), null);
});

test('floorAt ignores floors at or above the head', () => {
  assert.equal(floorAt([deck], -1.0, -12.5, 21.0), null);
});

test('floorAt picks the highest floor below the head', () => {
  const low = { ...deck, id: 'low', cy: 10 };
  assert.equal(floorAt([low, deck], -1.0, -12.5, 23.3)?.id, 'andaime');
});

test('floorAt honours a yawed floor', () => {
  const s = Math.SQRT1_2;
  const rotated = toThreeFloor({ id: 'r', px: 0, py: 0, pz: 0, sx: 4, sz: 1, qx: 0, qy: s, qz: 0, qw: s });
  assert.equal(floorAt([rotated], 0, 1.5, 1.7, 0)?.id, 'r');
  assert.equal(floorAt([rotated], 1.5, 0, 1.7, 0), null);
});

test('floorAt on the canteiro returns its 0.05 m height', () => {
  assert.equal(floorAt([yard, deck], 0, 0.75, 1.7)?.cy, 0.05);
});

const tasks = (entries) => new Map(entries.map(t => [t.id, t]));

test('markerState hides an unknown task and an empty session', () => {
  assert.deepEqual(markerState('install_guardrail', new Map()), { visible: false, done: false });
});

test('markerState hides markers until their group starts', () => {
  const m = tasks([
    { id: 'equip_helmet', groupName: 'EPI', status: 'active' },
    { id: 'install_guardrail', groupName: 'Andaime', status: 'pending' },
  ]);
  assert.deepEqual(markerState('install_guardrail', m), { visible: false, done: false });
});

test('markerState shows a pending marker once any task in its group is active', () => {
  const m = tasks([
    { id: 'connect_lanyard', groupName: 'Andaime', status: 'active' },
    { id: 'install_guardrail', groupName: 'Andaime', status: 'pending' },
  ]);
  assert.deepEqual(markerState('install_guardrail', m), { visible: true, done: false });
});

test('markerState marks a completed task done', () => {
  const m = tasks([{ id: 'install_toeboard', groupName: 'Andaime', status: 'completed' }]);
  assert.deepEqual(markerState('install_toeboard', m), { visible: true, done: true });
});

test('markerState keeps a not-performed task visible and pending', () => {
  const m = tasks([{ id: 'report_damaged_safety_mesh', groupName: 'Andaime', status: 'not_performed' }]);
  assert.deepEqual(markerState('report_damaged_safety_mesh', m), { visible: true, done: false });
});

test('formatMeters uses the locale decimal separator', () => {
  assert.equal(formatMeters(5.925, 'pt'), '5,93 m');
  assert.equal(formatMeters(0.902, 'en'), '0.90 m');
});
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `node --test Tools/DashboardSrc/tests/layout-math.test.mjs`
Expected: FAIL, `Cannot find module .../js/layout-math.js`.

- [ ] **Step 3: Implement**

Create `Tools/DashboardSrc/js/layout-math.js`:

```js
/* Pure scene-layout logic: no Three.js, no DOM, so node --test can import it. */

/* Lets a participant lean over the 0.90 m scaffold deck edge without the floor
   snapping back to the ground 21 m below. */
export const FLOOR_MARGIN = 0.3;

const STARTED = new Set(['active', 'completed', 'not_performed']);

/* Unity is left-handed: mirror Z on positions and conjugate rotations the same way
   the pose stream does (see EvaluatorViewport._updateObject). */
export function toThreeFloor(d) {
  return { id: d.id, cx: d.px, cy: d.py, cz: -d.pz, sx: d.sx, sz: d.sz,
           qx: -d.qx, qy: -d.qy, qz: d.qz, qw: d.qw };
}

export function toThreeMarker(d) {
  return { id: d.id, kind: d.kind, taskId: d.taskId,
           cx: d.px, cy: d.py, cz: -d.pz, sx: d.sx, sy: d.sy, sz: d.sz,
           qx: -d.qx, qy: -d.qy, qz: d.qz, qw: d.qw };
}

/* Floors are yaw-only, so only the Y rotation is undone. */
function toLocalXZ(f, x, z) {
  const yaw = 2 * Math.atan2(f.qy, f.qw);
  const dx = x - f.cx, dz = z - f.cz;
  const c = Math.cos(yaw), s = Math.sin(yaw);
  return [dx * c - dz * s, dx * s + dz * c];
}

/** Highest floor below the head whose (margin-grown) rectangle contains (x, z); null = ground. */
export function floorAt(floors, x, z, headY, margin = FLOOR_MARGIN) {
  let best = null;
  for (const f of floors) {
    if (f.cy >= headY) continue;
    const [lx, lz] = toLocalXZ(f, x, z);
    if (Math.abs(lx) > f.sx / 2 + margin || Math.abs(lz) > f.sz / 2 + margin) continue;
    if (!best || f.cy > best.cy) best = f;
  }
  return best;
}

/* Visibility comes from the manifest, not from GroupStarted, so a dashboard that
   connects mid-session still shows the markers. */
export function markerState(taskId, tasks) {
  const task = tasks.get(taskId);
  if (!task) return { visible: false, done: false };
  let started = false;
  for (const other of tasks.values()) {
    if (other.groupName === task.groupName && STARTED.has(other.status)) { started = true; break; }
  }
  return { visible: started, done: task.status === 'completed' };
}

export function formatMeters(value, lang) {
  const text = value.toFixed(2);
  return `${lang === 'pt' ? text.replace('.', ',') : text} m`;
}
```

- [ ] **Step 4: Run the tests**

Run: `node --test Tools/DashboardSrc/tests/layout-math.test.mjs`
Expected: 14 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Tools/DashboardSrc/js/layout-math.js Tools/DashboardSrc/tests/layout-math.test.mjs
git commit -m "feat(dashboard): add floor lookup and marker state logic"
```

---

### Task 5: Draw the layout and make the body floor-relative

**Files:**
- Create: `Tools/DashboardSrc/js/scene-layout.js`
- Modify:
  - `Tools/DashboardSrc/js/viewport.js` (constructor ~L102-194, `setTheme` L209-244, `dispose` L308-320, `_updateBody` L353-418, `_updatePosture` L479-487, `_animate` L630-642, and a new public API after `showInteraction`)
  - `Tools/DashboardSrc/js/router.js` (`SessionManifest`, `SessionReset`, `upsertTask`, and a new `SceneLayout` handler)
  - `Tools/DashboardSrc/js/i18n.js` (en block near L19, pt block near L100)
  - `Tools/DashboardSrc/index.html` (legend, L199-206)
  - `Tools/DashboardSrc/style.css` (swatches near L759, labels near L818)
  - `Tools/DashboardSrc/README.md` (module table)
- Generated by the sync: `Assets/_SafetyProto/Resources/Dashboard/**` (never edit by hand)

**Interfaces:**
- Consumes: `toThreeFloor`, `toThreeMarker`, `floorAt`, `markerState`, `formatMeters` (Task 4); `t`, `getLang` from `i18n.js`; the `SceneLayout` event (Task 2).
- Produces:
  - `class SceneLayoutView`: `constructor(scene, overlayEl)`, `setTheme(tok)`, `setLayout(dto)`, `refreshMarkers(tasks)`, `floorAt(x, z, headY)`, `updateLabels(project)`, `dispose()`
  - `EvaluatorViewport.setLayout(dto)` and `EvaluatorViewport.refreshMarkers(tasks)`
  - i18n keys: `markerGuardrail`, `markerToeboard`, `markerAnchor`, `markerIrregularity`, `legendFloor`, `legendTaskSpot`

- [ ] **Step 1: Create `scene-layout.js`**

```js
import * as THREE from 'three';
import { getLang } from './i18n.js';
import { toThreeFloor, toThreeMarker, floorAt, markerState, formatMeters } from './layout-math.js';

const LABEL_KEYS = {
  Guardrail: 'markerGuardrail', Toeboard: 'markerToeboard',
  Anchor: 'markerAnchor', Irregularity: 'markerIrregularity',
};

/* Walkable floors and task-spot markers. Owns its meshes, materials and labels;
   EvaluatorViewport only forwards theme, layout, task state and label projection. */
export class SceneLayoutView {
  constructor(scene, overlayEl) {
    this._overlay = overlayEl;
    this._root = new THREE.Group();
    scene.add(this._root);
    this._floors = [];    // { data, group, labels: [{ el, local, value }] }
    this._markers = [];   // { data, group, fill, edges, label, done, visible }
    this._box = new THREE.Box3();

    this._matFloorFill  = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.08, depthWrite: false, side: THREE.DoubleSide });
    this._matFloorEdge  = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.7 });
    this._matSpotFill   = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.18, depthWrite: false });
    this._matSpotDone   = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.12, depthWrite: false });
    this._matSpotEdge   = new THREE.LineBasicMaterial();
    this._matSpotEdgeDone = new THREE.LineBasicMaterial();
  }

  setTheme(tok) {
    this._matFloorFill.color = tok('--accent');
    this._matFloorEdge.color = tok('--accent');
    this._matSpotFill.color  = tok('--orange');
    this._matSpotEdge.color  = tok('--orange');
    this._matSpotDone.color  = tok('--ppe-worn');
    this._matSpotEdgeDone.color = tok('--ppe-worn');
  }

  setLayout(dto) {
    this._clear();
    for (const raw of dto?.floors ?? []) this._floors.push(this._buildFloor(toThreeFloor(raw)));
    for (const raw of dto?.markers ?? []) this._markers.push(this._buildMarker(toThreeMarker(raw)));
  }

  refreshMarkers(tasks) {
    for (const m of this._markers) {
      if (!tasks.has(m.data.taskId) && tasks.size > 0 && !m.warned) {
        console.warn(`[scene-layout] marker '${m.data.id}' references unknown task '${m.data.taskId}'`);
        m.warned = true;
      }
      const { visible, done } = markerState(m.data.taskId, tasks);
      m.visible = visible;
      m.group.visible = visible;
      m.fill.material  = done ? this._matSpotDone : this._matSpotFill;
      m.edges.material = done ? this._matSpotEdgeDone : this._matSpotEdge;
      m.label.classList.toggle('done', done);
    }
  }

  floorAt(x, z, headY) {
    return floorAt(this._floors.map(f => f.data), x, z, headY);
  }

  /** project(worldVec3) → { x, y, visible } in container pixels. */
  updateLabels(project) {
    const lang = getLang();
    for (const f of this._floors) {
      for (const l of f.labels) {
        const text = formatMeters(l.value, lang);
        if (l.el.textContent !== text) l.el.textContent = text;
        this._place(l.el, project(f.group.localToWorld(l.local.clone())), true);
      }
    }
    for (const m of this._markers) {
      this._box.setFromObject(m.group);
      const top = new THREE.Vector3(m.data.cx, this._box.max.y + 0.06, m.data.cz);
      this._place(m.label, project(top), m.visible);
    }
  }

  dispose() {
    this._clear();
    [this._matFloorFill, this._matFloorEdge, this._matSpotFill, this._matSpotDone,
     this._matSpotEdge, this._matSpotEdgeDone].forEach(m => m.dispose());
    this._root.removeFromParent();
  }

  _place(el, sp, visible) {
    el.style.display = visible && sp.visible ? 'block' : 'none';
    el.style.left = `${sp.x}px`;
    el.style.top  = `${sp.y}px`;
  }

  _label(className) {
    const el = document.createElement('div');
    el.className = `vp-label layout ${className}`;
    this._overlay.appendChild(el);
    return el;
  }

  _buildFloor(data) {
    const group = new THREE.Group();
    group.position.set(data.cx, data.cy + 0.003, data.cz);   /* above the deck mesh: no z-fighting */
    group.quaternion.set(data.qx, data.qy, data.qz, data.qw);

    const plane = new THREE.PlaneGeometry(data.sx, data.sz);
    plane.rotateX(-Math.PI / 2);
    group.add(new THREE.Mesh(plane, this._matFloorFill));
    group.add(new THREE.LineSegments(new THREE.EdgesGeometry(plane), this._matFloorEdge));
    this._root.add(group);

    const labels = [
      { el: this._label('dim'), local: new THREE.Vector3(0, 0, data.sz / 2), value: data.sx },
      { el: this._label('dim'), local: new THREE.Vector3(data.sx / 2, 0, 0), value: data.sz },
    ];
    return { data, group, labels };
  }

  _buildMarker(data) {
    const group = new THREE.Group();
    group.position.set(data.cx, data.cy, data.cz);
    group.quaternion.set(data.qx, data.qy, data.qz, data.qw);

    const box = new THREE.BoxGeometry(data.sx, data.sy, data.sz);
    const fill = new THREE.Mesh(box, this._matSpotFill);
    const edges = new THREE.LineSegments(new THREE.EdgesGeometry(box), this._matSpotEdge);
    group.add(fill, edges);
    group.visible = false;
    this._root.add(group);

    const label = this._label('spot');
    label.dataset.i18n = LABEL_KEYS[data.kind] ?? 'markerIrregularity';
    label.textContent = '';
    return { data, group, fill, edges, label, done: false, visible: false };
  }

  _clear() {
    for (const f of this._floors) {
      f.group.traverse(o => o.geometry?.dispose());
      f.labels.forEach(l => l.el.remove());
    }
    for (const m of this._markers) {
      m.group.traverse(o => o.geometry?.dispose());
      m.label.remove();
    }
    this._root.clear();
    this._floors = [];
    this._markers = [];
  }
}
```

Marker labels get their text from `applyTranslations()` (they carry `data-i18n`). So in Step 4 the router calls `applyTranslations()` after `setLayout`. The language toggle already calls it.

- [ ] **Step 2: Wire it into `viewport.js`**

1. Import it, next to the other imports:

```js
    import { SceneLayoutView } from './scene-layout.js';
```

2. In the constructor, right after the label overlay is appended (`containerEl.appendChild(this._labelOverlay);`), add:

```js
        /* Floors + task spots; built before setTheme, which colours it */
        this._layout = new SceneLayoutView(this._scene, this._labelOverlay);
        this._gridAt = { id: null, x: 0, y: 0, z: 0 };
```

3. In `setTheme`, after `this._matRing.color = tok('--accent');` add `this._layout.setTheme(tok);`. After `this._scene.add(this._grid);` add the line below, because a rebuilt grid must keep sitting on the current floor:

```js
        this._grid.position.set(this._gridAt.x, this._gridAt.y, this._gridAt.z);
```

4. Public API, after `showInteraction(...)`:

```js
      /** Static scene geometry from the SceneLayout event. */
      setLayout(layout) {
        this._layout.setLayout(layout);
        this._dirty = true;
      }

      /** Recolours/shows task spots from the tasks Map (state.js). */
      refreshMarkers(tasks) {
        this._layout.refreshMarkers(tasks);
        this._dirty = true;
      }
```

5. In `_updateBody`, replace these lines:

```js
        const headY = H.y;
        const shY   = headY - 0.24;
        const hipY  = Math.max(headY - 0.72, 0.25);
```

with:

```js
        const headY = H.y;
        const floor = this._layout.floorAt(H.x, H.z, headY);
        const floorY = floor ? floor.cy : 0;
        const shY   = headY - 0.24;
        const hipY  = Math.max(headY - 0.72, floorY + 0.25);
        this._placeGrid(floor);
```

Then set the plumb bottom `this._plumbPos[4] = floorY + 0.02;` (was `0.02`), the disc `this._disc.position.set(H.x, floorY + 0.005, H.z);` (was `0.005`), and the posture call `this._updatePosture(headY - floorY);`.

6. Add `_placeGrid` next to `_updatePosture`:

```js
      /* The grid follows the floor under the participant; on the ground it stays at the origin. */
      _placeGrid(floor) {
        const id = floor?.id ?? null;
        if (this._gridAt.id === id) return;
        this._gridAt = floor
          ? { id, x: floor.cx, y: floor.cy + 0.001, z: floor.cz }
          : { id: null, x: 0, y: 0, z: 0 };
        this._grid.position.set(this._gridAt.x, this._gridAt.y, this._gridAt.z);
      }
```

7. `_updatePosture(headY)`: rename the parameter to `heightAboveFloor`, and use it for both the thresholds and the `toFixed(2)` text. The thresholds stay 1.15 / 1.42.

8. In `_animate`, right after `this._updateLabels();` add:

```js
        this._layout.updateLabels(p => {
          const v = p.clone().project(this._camera);
          return { x: (v.x * 0.5 + 0.5) * this._container.clientWidth,
                   y: (v.y * -0.5 + 0.5) * this._container.clientHeight,
                   visible: v.z <= 1 };
        });
```

9. In `dispose()`, add `this._layout.dispose();` before `this._renderer.dispose();`. Leave `clearSession()` alone: the layout survives a reset by design.

- [ ] **Step 3: Add i18n keys, legend rows and styles**

`i18n.js`, en block (after `legendInteract`):

```js
        legendFloor: 'Walkable area',
        legendTaskSpot: 'Task spot (pending)',
        markerGuardrail: 'Guardrail',
        markerToeboard: 'Toeboard',
        markerAnchor: 'Anchor point',
        markerIrregularity: 'Irregularity',
```

pt block (after `legendInteract`):

```js
        legendFloor: 'Área de circulação',
        legendTaskSpot: 'Local da tarefa (pendente)',
        markerGuardrail: 'Guarda-corpo',
        markerToeboard: 'Rodapé',
        markerAnchor: 'Ponto de ancoragem',
        markerIrregularity: 'Irregularidade',
```

`index.html`, after the `legendInteract` row:

```html
        <div class="vp-legend-row"><span class="vp-swatch c-floor"></span><span data-i18n="legendFloor">Área de circulação</span></div>
        <div class="vp-legend-row"><span class="vp-swatch c-spot"></span><span data-i18n="legendTaskSpot">Local da tarefa (pendente)</span></div>
```

`style.css`, after `.vp-swatch.c-ring`:

```css
    .vp-swatch.c-floor  { background: color-mix(in srgb, var(--accent) 22%, transparent); border: 1px solid var(--accent); }
    .vp-swatch.c-spot   { background: color-mix(in srgb, var(--orange) 25%, transparent); border: 1.5px solid var(--orange); }
```

and after the `.vp-label.near { … }` block:

```css
    /* Layout labels are always legible; PPE labels stay dim until a hand comes near */
    .vp-label.layout       { opacity: 0.92; }
    .vp-label.layout.spot  { color: var(--orange); border-color: color-mix(in srgb, var(--orange) 45%, var(--border)); }
    .vp-label.layout.done  { color: var(--text-muted); border-color: var(--border); }
    .vp-label.layout.dim   { font-size: 10px; padding: 1px 5px; color: var(--accent); }
```

- [ ] **Step 4: Route the event and refresh markers**

In `router.js`, change the import from `./i18n.js` to also bring `applyTranslations`:

```js
    import { t, actionLabel, ppeLabel, violationLabel, violationMessage, applyTranslations } from './i18n.js';
```

Add a helper below the `on` definition:

```js
    const refreshMarkers = () => window.__spViewport?.refreshMarkers(tasks);
```

Add the handler next to the session lifecycle handlers:

```js
    on('SceneLayout', p => {
      window.__spViewport?.setLayout(p);
      applyTranslations();   /* marker labels take their text from data-i18n */
      refreshMarkers();
    });
```

Then call `refreshMarkers();`:
- at the end of the `SessionManifest` handler, after `renderTasks();`
- at the end of `upsertTask`, after `renderTasks();`
- in `SessionReset`, right after `tasks.clear();`

- [ ] **Step 5: Update the module table**

In `Tools/DashboardSrc/README.md`, add these rows below `js/viewport.js`:

```markdown
| `js/scene-layout.js` | `SceneLayoutView`: walkable floors + task-spot markers (Three.js) |
| `js/layout-math.js` | pure floor lookup / marker state / formatting (tested by `tests/layout-math.test.mjs`) |
```

- [ ] **Step 6: Sync and re-run the pure tests**

Run: `node --test Tools/DashboardSrc/tests/layout-math.test.mjs` → 14 passed.
Run: `unity --format json command menu --path "SafetyProto/Sync Dashboard Source"`
Expected: `Assets/_SafetyProto/Resources/Dashboard/js/scene-layout.txt` and `layout-math.txt` exist, and `viewport.txt`, `router.txt`, `i18n.txt`, `index.txt`, `style.txt` are updated (`git status`).

- [ ] **Step 7: Browser verification**

Enter Play Mode (`unity --format json command editor_play`, poll `editor_status`). Open `http://localhost:8080` in BrowserClaw (see `.agents/guides/dashboard-testing.md`; keep the tab foregrounded). In the page, through `evaluate`:

1. `__spRoute({eventType:'SceneLayout', payload:{floors:[{id:'canteiro',px:0.336,py:0.05,pz:0.661,sx:7.328,sz:5.178,qx:0,qy:0,qz:0,qw:1},{id:'andaime',px:-2.246,py:21.728,pz:12.458,sx:5.925,sz:0.902,qx:0,qy:0,qz:0,qw:1}], markers:[{id:'guardrail',kind:'Guardrail',taskId:'install_guardrail',px:0.791,py:22.87,pz:12.446,sx:0.12,sy:0.2,sz:1.098,qx:0,qy:0,qz:0,qw:1},{id:'toeboard',kind:'Toeboard',taskId:'install_toeboard',px:-0.753,py:21.775,pz:11.98,sx:3.09,sy:0.301,sz:0.12,qx:0,qy:0,qz:0,qw:1},{id:'anchor',kind:'Anchor',taskId:'connect_lanyard',px:-0.231,py:22.931,pz:13.124,sx:0.12,sy:0.12,sz:0.12,qx:0,qy:0,qz:0,qw:1},{id:'irregularity',kind:'Irregularity',taskId:'report_damaged_safety_mesh',px:0.108,py:23.024,pz:11.924,sx:0.15,sy:0.12,sz:0.12,qx:0,qy:0,qz:0,qw:1}]}})`
2. Inject a manifest with the scaffold group pending: `__spRoute({eventType:'SessionManifest', payload:{tasks:[{taskId:'equip_helmet',groupName:'EPI',status:'active'},{taskId:'connect_lanyard',groupName:'Andaime',status:'pending'},{taskId:'install_guardrail',groupName:'Andaime',status:'pending'},{taskId:'install_toeboard',groupName:'Andaime',status:'pending'},{taskId:'report_damaged_safety_mesh',groupName:'Andaime',status:'pending'}]}})`.
3. **Canteiro, standing:** `__spPose({hmd:{px:0,py:1.70,pz:-0.75,qx:0,qy:0,qz:0,qw:1}})`. Screenshot: the canteiro rectangle with `7,33 m` / `5,18 m`, the grid at the origin, the chip `EM PÉ · 1.65 m`, no markers.
4. **Deck, standing:** `__spPose({hmd:{px:-1.0,py:23.40,pz:12.5,qx:0,qy:0,qz:0,qw:1}})`, then `__spRoute({eventType:'TaskStarted', payload:{taskId:'connect_lanyard', taskName:'Conectar'}})`. Wait for the reframe (~1.5 s). Screenshot: the deck rectangle `5,93 m` / `0,90 m`, the grid on the deck, the chip `EM PÉ · 1.67 m`, four amber markers labelled Guarda-corpo / Rodapé / Ponto de ancoragem / Irregularidade.
5. **Deck, crouched:** `__spPose({hmd:{px:-1.0,py:22.80,pz:12.5,qx:0,qy:0,qz:0,qw:1}})`. The chip reads `AGACHADO · 1.07 m`.
6. **Leaning past the edge:** `__spPose({hmd:{px:-1.0,py:23.40,pz:13.1,qx:0,qy:0,qz:0,qw:1}})`. The chip stays `EM PÉ · 1.67 m`, and the grid stays on the deck.
7. **Completion:** `__spRoute({eventType:'TaskCompleted', payload:{taskId:'install_guardrail', taskName:'Guarda-corpo'}})`. The guardrail marker and its label turn neutral, and the others stay amber.
8. **Language and theme:** click the language FAB, and the labels read Guardrail / Toeboard / Anchor point / Irregularity with `5.93 m`. Click the theme FAB, and the colours follow without a rebuild. Restore PT and dark.
9. **Order independence:** reload the page, inject the manifest from step 2 (with `connect_lanyard` `active`) **before** the layout from step 1. The markers appear amber right after the layout.
10. **Reset:** `__spRoute({eventType:'SessionReset', payload:{}})`. The floors stay, the markers hide, and the console shows no errors.

Check the console for errors after each step (`evaluate` → `console` capture, or the `read` tool). Exit Play Mode: `unity --format json command editor_stop`.

- [ ] **Step 8: Commit**

```bash
git add Tools/DashboardSrc/js/scene-layout.js Tools/DashboardSrc/js/viewport.js Tools/DashboardSrc/js/router.js Tools/DashboardSrc/js/i18n.js Tools/DashboardSrc/index.html Tools/DashboardSrc/style.css Tools/DashboardSrc/README.md Assets/_SafetyProto/Resources/Dashboard
git commit -m "feat(dashboard): draw walkable floors and scaffold task spots"
```

---

### Task 6: End-to-end check in the Editor

**Files:** none changed (verification only), apart from `graphify-out/`.

**Interfaces:**
- Consumes: everything above. Real Unity → WebSocket → dashboard.

- [ ] **Step 1: Real layout over the wire**

Enter Play Mode and open `http://localhost:8080`. Through `evaluate`, hook the socket before reconnecting, or just read the effect. The canteiro rectangle (`7,33 m` / `5,18 m`) must appear without any injection, and the participant's disc and grid must sit on it at the Canteiro spawn.

- [ ] **Step 2: Run the scaffold phase**

Advance to the scaffold phase: the evaluator flow, or the session simulator (`docs/session-simulator.md`). After the teleport, the dashboard shows the deck with the grid on it, a posture chip near 1.6-1.7 m while the participant stands, and the four amber markers. Complete one task, through the simulator or the headset, and its marker turns neutral.

- [ ] **Step 3: Full test pass**

Run: `unity --format json command run_tests --mode editor --timeout 600` → no new failures compared to `main` before this work. Run `node --test Tools/DashboardSrc/tests/layout-math.test.mjs` → 14 passed.

- [ ] **Step 4: Refresh the knowledge graph**

Run: `graphify update .`

- [ ] **Step 5: Report**

Report the results, with screenshots of steps 1-2, to the user. No commit in this task.
