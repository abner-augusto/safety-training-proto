# 0003: `DashboardGate` is a process-wide flag

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `Assets/_SafetyProto/Scripts/Runtime/Analytics/DashboardGate.cs:13-15`

## Context
The evaluator dashboard receives a droppable pose stream at 10 Hz. A WebSocket send blocks the main thread, and a send during an expensive moment, such as the recenter teleport, causes a frame hitch that can drop the participant through a collider. Discrete gameplay events must keep flowing during those moments so no analytics data is lost.

## Decision
`DashboardGate.PoseBroadcastSuspended` is a public static bool. `RecenterService` sets it to true when a teleport begins and back to false when the sequence ends or is cancelled. `PoseSender` is the only reader and skips its broadcast while the flag is set. Only the pose stream is gated; discrete events are never gated.

## Consequences
The gate needs no reference between the recenter code and the networking code, and one writer and one reader keep the contract small. The flag is process-wide and unowned, so a second writer would need to coordinate with `RecenterService`, and a sequence that exits without clearing the flag would leave the pose stream off. The pose stream's cost was measured and closed as negligible, so the gate exists for the teleport hitch alone.

## Revisit when
A second gated stream appears, or the pose stream is moved off the main thread.
