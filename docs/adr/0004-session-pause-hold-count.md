# 0004: Pausing is a hold count

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `Assets/_SafetyProto/Scripts/Core/Events/SessionPause.cs:34-66`

## Context
Three sources can pause the session: application focus loss, an open popup, and the pause menu. They overlap. A popup opening over a paused session, or focus returning while the pause menu is open, must not resume the session or publish a second pause event.

## Decision
`SessionPause` is the single entry point. A source calls `Hold(PauseSource)` and `Release(PauseSource)`; it never raises `SessionPaused` or `SessionResumed` itself. The holds live in a set keyed by `PauseSource`, and the session is paused while the set is non-empty. `SessionPaused` is published only on the 0 to 1 transition and `SessionResumed` only on the 1 to 0 transition. `IsPaused` reflects the holds immediately, while the events arrive on the next bus pump. `Reset()` drops every hold without raising `SessionResumed`, for session teardown.

## Consequences
Every listener sees one balanced pause and resume pair per pause, however many sources overlap, and the countdown timer freezes for exactly as long as any hold exists. The state is a process-wide static, so it is cleared on play entry and at session reset. A source destroyed while holding must be released or covered by `Reset()`, or the next session starts paused.

## Revisit when
A pause source needs per-source behaviour beyond hold and release.
