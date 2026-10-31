# Dashboard source

`index.html`, `style.css`, and `app.js` in this folder are **the** source for
the evaluator dashboard (the web page served by the running Unity app so an
evaluator on the same LAN can watch a VR training session). Edit only these
files.

## Layout

`app.js` is the **entry point only** — the import header, the DOM wiring block,
and the boot sequence (`MAIN ENTRY POINT`). It stays small (~130 lines). Each
concern lives in its own ES module under `js/`, one file per concern:

| Module | Responsibility |
|---|---|
| `js/constants.js` | WebSocket + log + casting constants |
| `js/i18n.js` | translations, `lang` (private), `t`/`getLang`/`locale`/`toggleLang`/`applyTranslations` |
| `js/theme.js` | dark/light theme, `applyTheme`/`toggleTheme` |
| `js/state.js` | shared mutable session state (`state` object + `tasks`/`wornPpe`/`sessionTimer`) |
| `js/ui.js` | DOM rendering: cluster, tasks, log, report section + modal |
| `js/ws.js` | `WsManager` (reconnect/backoff WebSocket client) |
| `js/history.js` | session history persisted in localStorage |
| `js/station.js` | headset state (`StationStatus`), session start form, restart and in-session command buttons, pause and battery chips |
| `js/embed.js` | `?embed=1` mode inside the multi-headset shell: theme/language and tile commands from the shell, status and command acks up to it |
| `js/multi.js` | the multi-headset shell itself (entry point of `multi.html`, not of `app.js`): tiles, stuck alert and sound, quick actions |
| `js/stuck-math.js` | pure stuck-participant logic: folds routed events into a progress object, decides when a tile is stuck (tested by `tests/stuck-math.test.mjs`) |
| `js/interventions.js` | pure log of the evaluator commands of one session (tested by `tests/interventions.test.mjs`) |
| `js/tile-actions.js` | the quick actions of a headset tile and when they are blocked (tested by `tests/tile-actions.test.mjs`) |
| `js/router.js` | event router: handler map + `route` |
| `js/viewport.js` | `EvaluatorViewport` (Three.js instrument scene) |
| `js/scene-layout.js` | `SceneLayoutView`: walkable floors + task-spot markers (Three.js) |
| `js/layout-math.js` | pure floor lookup / marker state / formatting (tested by `tests/layout-math.test.mjs`) |
| `js/resize.js` | `SidebarResizeManager` (drag-to-resize) |

Import specifiers are explicit with the `.js` extension (native ESM has no
resolution magic): `app.js` imports from `./js/state.js`; modules inside `js/`
import each other with `./state.js`. The import graph is acyclic —
`history.js` is imported only by `router.js` and `station.js`.

`multi.html` is a second page: one iframe per headset
(`http://<headset>:8080/?embed=1`), side by side, so one evaluator can watch
and drive two headsets from one tab. Each iframe is a complete station page
talking to its own headset; the shell lays them out, forwards theme and
language, shows each station's status in its tile bar, and watches for a
participant who is stuck.

Stuck alert: the router folds every event into `state.progress`
(`stuck-math.js`), the embed snapshot carries it to the shell, and the shell
re-evaluates every tile once a second, because a participant who stops moving
sends no event. A tile is stuck after N seconds without progress (a task
started or completed, a PPE item put on, or an action attempt the rule engine
did not refuse; N
defaults to 60 and is kept in `localStorage` as `sp_stuck_seconds`), at once when
the session is paused by a focus token (`ApplicationFocus`, `ApplicationPause`,
`XrSessionUnfocused`; `Popup` and `PauseMenu` are not stuck), and at once on a
`HintShown` event of level 3. Resuming or progressing clears it. The chime is
synthesized with Web Audio (no asset to ship) and plays once per alert; browsers
start audio only after a click in the shell, so the sound button says "clique
para ativar" until then.

Quick actions: each tile has confirm-then-send buttons built from `TILE_ACTIONS`
in `js/tile-actions.js`. The shell posts the `Command` envelope to the tile's
iframe, the embedded page sends it over its own WebSocket, and the `CommandAck`
comes back up as a message. To add an action (for example "Chamar atenção"),
append an entry to `TILE_ACTIONS`, add its strings to both tables in
`js/i18n.js` and its label as `command_<wire name>`; the embedded page accepts
only commands listed there.

Intervention history: the station page records every command it sends, from its
own buttons or the shell's tiles, in `state.interventions` (time, command label,
ack reason) and lists them in the "Intervenções" section. A new session, or a
reset to the waiting screen, clears the list, so a restart or a start belongs to
the session it was sent in. The tile shows the count.

The pure modules import nothing that touches `document` or `localStorage`, so
`node --test Tools/DashboardSrc/tests/<name>.test.mjs` runs their tests (pass
file paths; the directory form does not work on every Node version).

Adding a module: create `js/<name>.js`, import it where needed. The sync
mirrors it to `Resources/Dashboard/js/<name>.txt` and
`EvaluatorDashboardBootstrap` auto-routes everything in
`Resources/Dashboard/js/` to `/js/<name>.js` — **no C# change needed**.

## Generated copies

`Assets/_SafetyProto/Resources/Dashboard/{index,style,app,multi}.txt` are
**generated copies** of the files here. They exist only because Unity imports
`.txt` (not `.js`/`.css`) as a `TextAsset`, which is how the on-device HTTP
server (`MiniHttpServer`) gets its bytes. Each generated `.txt` file carries a
GENERATED banner and is overwritten by every sync — **both copies are
deliberately committed to git** so a fresh clone works without ever running
the sync. Expect every dashboard edit to appear twice in diffs (once here,
once in the generated `.txt`).

The `js/` modules are mirrored the same way to
`Resources/Dashboard/js/*.txt`, **with deletion mirroring**: because
`Resources/Dashboard/js/` is 100% generated, renaming or removing a
`js/<name>.js` file deletes the orphaned `js/<name>.txt` (and its `.meta`) on
the next sync.

## Sync triggers

The sync (`Assets/_SafetyProto/Scripts/Editor/DashboardSourceSync.cs`) runs
automatically:

- On entering Play mode in the editor.
- Before every player build.

It can also be run manually via the Unity menu
`SafetyProto → Sync Dashboard Source`.

## Out of scope

`vendor/` files under `Resources/Dashboard/` (vendored three.js,
OrbitControls, fonts, and the `models/ppe.bytes` GLB of PPE silhouettes) are
**not** managed by this sync. The GLB is built by
`Assets-3D/Dashboard-PPE/build_dashboard_ppe.py` in the thesis prototype folder
and copied in by hand; its mesh names must match `PPEType` names.
