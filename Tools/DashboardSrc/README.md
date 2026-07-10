# Dashboard source

`index.html`, `style.css`, and `app.js` in this folder are **the** source for
the evaluator dashboard (the web page served by the running Unity app so an
evaluator on the same LAN can watch a VR training session). Edit only these
files.

## Generated copies

`Assets/_SafetyProto/Resources/Dashboard/{index,style,app}.txt` are
**generated copies** of the files here. They exist only because Unity imports
`.txt` (not `.js`/`.css`) as a `TextAsset`, which is how the on-device HTTP
server (`MiniHttpServer`) gets its bytes. Each generated `.txt` file carries a
GENERATED banner and is overwritten by every sync — **both copies are
deliberately committed to git** so a fresh clone works without ever running
the sync. Expect every dashboard edit to appear twice in diffs (once here,
once in the generated `.txt`).

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
