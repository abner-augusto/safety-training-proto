# 0002: `ScoreService.Instance` is a static accessor

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `Assets/_SafetyProto/Scripts/Domain/Scoring/ScoreService.cs:9-22`

## Context
The domain layer injects engine capabilities and otherwise avoids global state (principle 4 in ARCHITECTURE.md). The score is different: ten runtime components read it, and a session has exactly one score scope. Passing a score reference to each of those readers adds wiring without adding an option anyone uses.

## Decision
`ScoreService` exposes a static `Instance` that is created lazily on first access, and `DestroyInstance()` discards it. The class still implements `IScoreService` and `ISessionResettable`, and code that can receive the interface by injection does so. The static accessor is the one standing deviation from injection in the domain.

## Consequences
Runtime components read the score without a serialized reference or a constructor argument. Tests construct their own `ScoreService` directly, so the static accessor does not constrain them. The cost is hidden global state: every reader that uses `Instance` shares the one score, and the instance must be destroyed or reset at session boundaries. Reviewers should reject new uses of `Instance` in code that can take `IScoreService` instead.

## Revisit when
A second score scope exists, for example two participants in one process or per-group scores.
