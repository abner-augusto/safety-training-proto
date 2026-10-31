# Feedback channels

Which channels (text, voice, sound, visual) carry each feedback event the participant meets in the training.
A participant in a noisy room without earphones must see every important sound, and a participant looking away must hear every important text.
Keep this table in step with the code: a new feedback event gets a row, and a row with an empty cell needs a reason in the last column.

Popups follow the head (`MenuFollowHmd` on `PopupCanvas_new`), so a popup comes back into view when the participant turns.
Popups play no opening sound; the voice line is what makes a popup audible to a participant looking elsewhere, so every popup below has one.

## Blocking and corrective feedback (Guided mode)

| Event | Text | Voice | Sound | Visual | Notes |
|---|---|---|---|---|---|
| Decoy PPE released on a body slot | popup "EPI Incorreto" | `distractor:<id>` | — | popup; item returns home | stays until the next action |
| PPE out of order | popup "Ordem Incorreta" (+ task hint) | `ordem_incorreta` | — | popup; item returns home | stays until the next action |
| Task refused, lanyard not anchored | popup "Conecte-se Primeiro" | `conecte_se_primeiro` | — | popup; the scaffold piece returns when the popup closes, or after 10 s | stays until the next action |
| Task completed without its PPE | popup "EPI Incorreto" (`ppeAdvice`) | `ppe_advice:<taskId>` | unsafe-success sound | popup | stays until the next action |
| Two-hand piece held with one hand | — | first time in the session | short cue (cooldown 8 s) | second grab spot pulses | no text: the cue is about the hands, not the task |

"Stays until the next action": the popup holds the session clock for its reading time (6 s, longer when the narration runs longer), exactly like the self-closing popup it replaced, then stays visible without holding the clock until the participant grabs an object, attempts an action, changes PPE, or a task advances, with a 30 s ceiling (`PopupService.ShowFeedbackWarning`, `StickyPopupPolicy`).

## Placement feedback (scaffold pieces and lanyard)

| State | Sound | Visual |
|---|---|---|
| Held away from the socket | silent | ghost orange |
| In position, wrong angle | tick, repeated every 0.6 s | ghost yellow |
| Valid pose (with both hands, for a two-hand piece) | chime, repeated every 1 s | ghost green |
| Released near the socket in a wrong pose | invalid-release sound | ghost flashes orange for 1 s (setting the piece down away from the socket is silent) |
| Installed | install sound | piece locks in place, ghost hides |
| Lanyard carabiner within reach of an anchor | carabiner opens (animation) | the anchor pulses |
| Lanyard locked | lock sound | carabiner stays on the anchor |

Volumes and repeat intervals are per piece on `ScaffoldPieceInstaller`.

## Progress feedback

| Event | Text | Voice | Sound | Visual |
|---|---|---|---|---|
| PPE snapped to the body | — | — | snap sound | item on the body, slot occupied |
| Task completed | checklist tick | success advice when authored as narrated | success | checklist |
| Phase completed | popup "Fase concluída" | `fase_concluida` | success | popup |
| Inspection passed | popup | `inspecao_concluida` | success | popup |

## Evaluator-triggered and guided cues

| Event | Sound | Visual |
|---|---|---|
| "Chamar atenção" from the dashboard | positional ping every 1.5 s | target pulses yellow |
| Guided hint, level 1 (no progress for ~15 s) | — | next object or slot pulses |
| Guided hint, level 2 (~30 s) | narration of the next step | pulse continues |
| Guided hint, level 3 (~60 s) | — | evaluator alert on the dashboard |

## Gaps left on purpose

- Haptics: hand tracking has none, so nothing above relies on vibration.
- Popup text size and contrast on the Quest 2: checked only on the headset; not changed here.
