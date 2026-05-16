# EventBus Benchmark Results

**Date:** 2026-05-16
**Machine:** AMD Ryzen 7 3800X 8-Core Processor, 16 cores, Microsoft Windows NT 10.0.26200.0
**Runtime:** .NET 10.0.7
**Methodology:** 5 runs per metric, median reported, 10 000-iteration warm-up discarded.

## 1. Dispatch latency (single subscriber)

| Iterations | Median latency (μs/event) |
|---|---|
| 1 000 000 | 0,14 |

## 2. Throughput

| Events/sec |
|---|
| 7.026.454 |

## 3. Subscriber scaling

| Subscribers | Latency (μs/event) | Slowdown vs N=1 |
|---|---|---|
| 1 | 0,16 | 1.00× |
| 10 | 0,16 | 0,99× |
| 100 | 0,41 | 2,53× |
| 1000 | 2,70 | 16,62× |

## 4. Memory overhead

| Configuration | Memory delta (KB) |
|---|---|
| HarnessEventBus + 10 handlers × N event types | 31,3 |

(N = 13)

## Raw output

```
=== EventBus Benchmark ===

--- Benchmark 1 & 2: Dispatch latency / Throughput ---
  Run 1: 4437543 ticks, 443,75 ms
  Run 2: 2336171 ticks, 233,62 ms
  Run 3: 1323322 ticks, 132,33 ms
  Run 4: 1338157 ticks, 133,82 ms
  Run 5: 1423193 ticks, 142,32 ms
  Median latency: 0,14 μs/event
  Throughput:    7026454 events/sec

--- Benchmark 3: Subscriber scaling ---
  N=1 Run 1: 1882750 ticks, 188,28 ms
  N=1 Run 2: 1625519 ticks, 162,55 ms
  N=1 Run 3: 1427829 ticks, 142,78 ms
  N=1 Run 4: 1399842 ticks, 139,98 ms
  N=1 Run 5: 1766830 ticks, 176,68 ms
  N=1: 0,16 μs/event, 1,00× vs N=1
  N=10 Run 1: 1801075 ticks, 180,11 ms
  N=10 Run 2: 1544116 ticks, 154,41 ms
  N=10 Run 3: 1599777 ticks, 159,98 ms
  N=10 Run 4: 1603058 ticks, 160,31 ms
  N=10 Run 5: 1605570 ticks, 160,56 ms
  N=10: 0,16 μs/event, 0,99× vs N=1
  N=100 Run 1: 3865031 ticks, 386,50 ms
  N=100 Run 2: 4116951 ticks, 411,70 ms
  N=100 Run 3: 4075636 ticks, 407,56 ms
  N=100 Run 4: 4112948 ticks, 411,29 ms
  N=100 Run 5: 4310716 ticks, 431,07 ms
  N=100: 0,41 μs/event, 2,53× vs N=1
  N=1000 Run 1: 27456207 ticks, 2745,62 ms
  N=1000 Run 2: 27013606 ticks, 2701,36 ms
  N=1000 Run 3: 27319281 ticks, 2731,93 ms
  N=1000 Run 4: 26133446 ticks, 2613,34 ms
  N=1000 Run 5: 26250112 ticks, 2625,01 ms
  N=1000: 2,70 μs/event, 16,62× vs N=1

--- Benchmark 4: Memory overhead ---
  Found 13 event types: ActionAttemptedEvent, CriticalSafetyFailureEventArgs, PPEStateChangedEventArgs, SafetyErrorEventArgs, SafetyViolationEventArgs, ScoreChangedEventArgs, SessionCompletedEventArgs, SessionEndedEventArgs, SessionPausedEventArgs, SessionResumedEventArgs, SessionStartedEventArgs, TaskEventArgs, TaskGroupEventArgs
  Baseline:    73720 bytes
  After setup: 105760 bytes
  Delta:       31,3 KB


```