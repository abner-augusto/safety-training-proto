# Drill Setup Notes

Setup steps:
- Add `HandGrabUseInteractable` to the drill prefab (or confirm it already exists).
- Configure `UseFingers` on the interactable to match the sample hand use rules.
- Assign `DrillUse` as the `_handUseDelegate` on `HandGrabUseInteractable`.
- Wire the trigger transform, rotation curve, axis mask, strength curve, and thresholds on `DrillUse`.
- Assign `motorAudio` (looping) and optional `clutchAudio`.
- Set `bitTip`, `forward`, `rpm`, sphere cast radius/distance, and `fastenerMask`.
- Add `FastenerSocket` to screw targets, set the `axis` forward direction into the surface, and optionally set `seat` and `head`.
- Put the `FastenerSocket` colliders on a dedicated layer and match that layer in `fastenerMask`.

What changed:
- Added `DrillUse` and `FastenerSocket` scripts for hand grab use, motor control, and fastener driving.
- Added this setup note for configuring drill use and fastener sockets.
