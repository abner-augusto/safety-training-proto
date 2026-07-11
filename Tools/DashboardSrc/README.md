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
| `js/router.js` | event router: handler map + `route` |
| `js/viewport.js` | `EvaluatorViewport` (Three.js instrument scene) |
| `js/resize.js` | `SidebarResizeManager` (drag-to-resize) |

Import specifiers are explicit with the `.js` extension (native ESM has no
resolution magic): `app.js` imports from `./js/state.js`; modules inside `js/`
import each other with `./state.js`. The import graph is acyclic —
`router.js` is the only module that imports `history.js`.

Adding a module: create `js/<name>.js`, import it where needed. The sync
mirrors it to `Resources/Dashboard/js/<name>.txt` and
`EvaluatorDashboardBootstrap` auto-routes everything in
`Resources/Dashboard/js/` to `/js/<name>.js` — **no C# change needed**.

## Generated copies

`Assets/_SafetyProto/Resources/Dashboard/{index,style,app}.txt` are
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
OrbitControls, fonts) are **not** managed by this sync.
