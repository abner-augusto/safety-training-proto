# Quest 2 performance report (October 2026)

The app was tuned on a Quest 3. On a Quest 2 (`hollywood`) it ran at about 40 fps against a
72 Hz display. A series of changes on 2026-10-23 and 2026-10-24 brought every measured view to
about 70 fps. This report records what was measured, what limited the frame at each stage, and
what is still open.

## Method

- **Build.** Development + AllowDebugging through the connected Editor, keeping Meta's thin LTO
  (`unity command build ... --options '["Development","AllowDebugging"]'`). `build-android.ps1
  -Development` skips LTO and runs ~5 fps slower on the CPU, so its numbers are not comparable.
- **Runs.** `scripts/quest-sim-run.ps1 -Mode avaliacao -MetricsCsv` with a hold script that
  drives the session to a view and stays there (now `-Hold <waypoint>`), nobody wearing the headset. Launch extras set
  the render settings without a rebuild:
  `-Extra safety_perf_scale=0.8,safety_perf_foveation=0`. See
  [session-simulator.md](session-simulator.md#performance-runs).
- **Metrics.** fps, CPU/GPU levels, app GPU time and GPU utilisation from the VrApi logcat line
  (`vrapi.txt` in each run folder), averaged over the hold.
- **CPU attribution.** The Unity Profiler connected to the headset over adb, ~400 frames
  aggregated per marker. Profiler overhead inflates absolute times, so it ranks costs and fps
  is always measured without it.
- **GPU attribution.** RenderDoc for Meta Quest frame captures (draw-call clocks).

## Stage 1: GPU bound

With the Quest 3 settings the Quest 2 GPU was saturated in the scaffold view (~40 fps), and
fixed foveation mirrored and blocked out the edges of the view.

| Change | Commit | Effect |
|---|---|---|
| Quest 2 profile: `eyeTextureResolutionScale` 0.8, foveation off | `fb938cc` | app GPU ~9 ms in the scaffold view (foveation on: ~12 ms) |
| `SafetyProto/SimpleLit` Lambert shader on opaque Standard materials | `fa6c611` | slab draw −37% clocks, frame draws −26% |
| Flat, opaque panel backgrounds (stock UI material) | `99c8df2` | drops the SDF panel shader |
| 560-triangle safety net on the scenery scaffolds (was 17,920) | `0bf9760` | fewer raster-bound triangles |

After this stage the GPU had headroom (app GPU ~9 ms of the 13.9 ms budget), but the frame
rate stayed at 40.9 fps at the irregularity view: the bottleneck had moved to the CPU.

## Stage 2: main-thread bound

Profiler at the irregularity hold, average frame 25.7 ms. The render thread spent 21 ms per
frame waiting for the main thread (`Gfx.WaitForGfxCommandsFromMainThread`).

| Main-thread cost | ms/frame |
|---|---|
| `HelmetChinstrap.FixedUpdate` (537 capsule overlaps/frame, ~1.9 fixed steps/frame) | 10.6 |
| `OVROverlayCanvas.Update` (7 canvases) | 2.3 |
| `FinishFrameRendering` camera render (main + PopupCamera) | 2.1 |
| `PhysicsFixedUpdate` | 1.3 |
| `VerletLanyard.FixedUpdate` | 1.0 |
| `OVRCameraRig.Update` | 0.9 |
| `InteractorGroup.Update` (Interaction SDK) | 0.9 |

The fixed timestep is 1/72 s, so at 40 fps every `FixedUpdate` cost is paid about twice per
frame. The cost grows as the frame rate falls.

| Change | Commit | Irregularity view |
|---|---|---|
| Baseline after stage 1 | | 40.9 fps |
| Chinstrap sleeps once its strap has settled and the helmet is still | `c4532a0` | 49.5 fps |
| Loose PPE hides with the canteiro zone at the phase transition | `dee9884` | 69.2 fps |

The second fix came from a profile that still showed the chinstrap at 11 ms after the first.
At the phase transition `PhaseController` hides the table and shelves, but not the PPE on them.
An item left behind fell to the floor, `ReturnObjectHome` sent it back to its home pose in
mid-air, and it fell again every ~7 s for the rest of the session. Each cycle woke the strap.
This is not specific to the simulator: it happens whenever a participant leaves PPE behind in
evaluation mode. Unworn, unheld PPE is now deactivated at the blackout, and a session reset
reloads the scene.

The session simulator did not wear PPE at the time, so the chinstrap stayed loose for the whole
run and these views overstate its cost. In real play the strap stops simulating once the helmet is
worn; the simulator now snaps the PPE onto its slots, so later runs show that state.

## Result

LTO development build, render scale 0.8, foveation off, simulator holds:

| View | fps | App GPU | GPU util |
|---|---|---|---|
| Irregularity | 69.2 | 8.4 ms | 87% |
| Guardrail | 70.1 | 8.3 ms | 77% |
| Andaime | 69.9 | 9.4 ms | 84% |

Run folders: `dist/quest-sim/q2-c5-loosehide`, `q2-c5-guardrail`, `q2-c5-andaime` (git-ignored).

## Stage 3: GPU headroom

Every panel prefab set `OVROverlayCanvas.superSample` on top of dynamic resolution, which already
renders each canvas at twice the eye-buffer density, so canvas textures ran at four times it. The
Quest 2 profile now turns super-sampling off (`0abcc8d`); the text looks almost the same on the
headset. Andaime view, two alternating rounds each, with the simulator wearing the PPE:

| Canvas super-sampling | fps | App GPU | GPU util |
|---|---|---|---|
| On | 70.1 | 11.64 ms | 92% |
| Off | 70.5 | 9.84 ms | 88% |

The frame rate barely moves because the main thread still sets it; the saving is headroom for
heavier views.

Part of that headroom then went back into the picture. The PPE room's spotlight is now baked,
with a lightmap swap that keeps its lights-on moment, and light probe grids cover the room and
the scaffold platform. None of that costs anything per frame. The scaffold metals regained
reflection-probe reflections in `SafetyProto/SimpleLit` (`safety_perf_metal_reflections` toggles
them on a Development build). Andaime view, three alternating rounds each:

| Metal reflections | fps | App GPU | GPU util |
|---|---|---|---|
| Off | 70.8 | 9.66 ms | 84% |
| On | 69.1 | 10.16 ms | 85% |

About 0.5 ms of GPU; the fps gap comes mostly from one round's dip. Identical runs differ by up
to ~3.5 ms of app GPU here, so judge a change by alternating rounds, never by a single run.

## Open candidates

Ranked by expected gain. None of them is measured yet.

- **Static canvases redraw every other frame (CPU).** Info boards and the inspection buttons use
  `renderInterval` 2. Content that never changes could use `manualRedraw` and redraw on change.
- **PopupCamera (CPU and GPU).** A second depth-clearing camera renders the cursors every frame.
  Its mid-pass clear also keeps hardware early-Z (LRZ) off for the eye pass.
- **CPU level.** VrApi already reports CPU level 4 / GPU level 5, so `suggestedCpuPerfLevel` is
  unlikely to help.
- **Vendor code.** `OVRCameraRig`, `InteractorGroup` and `OVRManager` together cost ~2 ms. Leave them alone.
