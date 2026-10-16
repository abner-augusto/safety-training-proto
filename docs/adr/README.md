# Architecture decision records

These records capture the places where the code knowingly departs from the rules in [ARCHITECTURE.md](../../ARCHITECTURE.md), and why. Each record states the decision as a rule the code follows and names the condition under which it should be reopened.

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-synchronous-consequence-channel.md) | The consequence channel is synchronous | Accepted | 2026-10-16 |
| [0002](0002-score-service-static-instance.md) | `ScoreService.Instance` is a static accessor | Accepted | 2026-10-16 |
| [0003](0003-dashboard-gate-process-wide-flag.md) | `DashboardGate` is a process-wide flag | Accepted | 2026-10-16 |
| [0004](0004-session-pause-hold-count.md) | Pausing is a hold count | Accepted | 2026-10-16 |
| [0005](0005-one-source-two-compilations.md) | One source, two compilations | Accepted | 2026-10-16 |

Status is one of `Accepted`, `Superseded by NNNN`, or `Deprecated`.

## Template

```markdown
# NNNN: <Title>

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `<file:line>` of the thing the decision governs

## Context
What forces were in play. Two to five sentences.

## Decision
What was chosen, stated as a rule the code follows.

## Consequences
What this makes easier, what it makes harder, what reviewers must watch.

## Revisit when
The concrete condition under which this decision should be reopened.
```
