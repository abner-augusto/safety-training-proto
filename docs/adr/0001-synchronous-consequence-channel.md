# 0001: The consequence channel is synchronous

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `Assets/_SafetyProto/Scripts/Core/Events/ConsequenceEvents.cs:24-38`

## Context
Every other protocol event travels through the deferred `EventBus` queue and reaches its subscribers on the next pump. A scripted consequence, such as a hazard reaction triggered by the `InspectionGateValidator`, plays animation and audio. Deferring those by a frame misaligns them with the moment the participant caused them.

## Decision
`DirectConsequenceChannel` is a static class with two plain C# events, `OnConsequenceStarted` and `OnConsequenceEnded`. `InspectionGateValidator` raises them directly, so subscribers run in the same frame as the raise. This is the only place the "everything is queued" rule is broken. Subscribers are audio and visual only (`EventGameObjectListener`, `SessionSimulator`) and have no ordering dependency on the queued protocol.

## Consequences
Consequence feedback lands in the frame of the trigger. The channel is outside the bus, so it is not stamped and not carried through the protocol. Reviewers must reject any subscriber that changes game state (score, task state, PPE state), because a synchronous handler can run in the middle of a queued handler's work and reorder the protocol.

## Revisit when
A subscriber to the synchronous channel needs to change game state, or a second synchronous channel is proposed.
