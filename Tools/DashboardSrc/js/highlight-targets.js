/* The "Chamar atenção" picker's rules, shared by the station page and the multi-headset tiles: how
   the headset's HighlightTargets list is grouped, and what a highlight_target command carries. The
   labels come from the app, in Portuguese; only the group names are translated here. */

export const HIGHLIGHT_DURATIONS = [5, 8, 15];
export const DEFAULT_HIGHLIGHT_SECONDS = 8;

/* Group order follows the training: PPE room first, then the scaffold. */
const KIND_ORDER = ['ppe', 'slot', 'distractor', 'anchor', 'lanyard', 'piece', 'socket', 'action', 'tool'];

/** Targets grouped by kind, in training order; unknown kinds go last under 'tool'. */
export function groupTargets(targets) {
  const groups = new Map(KIND_ORDER.map(kind => [kind, []]));
  for (const target of Array.isArray(targets) ? targets : []) {
    if (!target || !target.id) continue;
    const kind = groups.has(target.kind) ? target.kind : 'tool';
    groups.get(kind).push({ id: target.id, label: target.label || target.id });
  }
  return KIND_ORDER.filter(kind => groups.get(kind).length > 0)
    .map(kind => ({ kind, labelKey: `highlightKind_${kind}`, targets: groups.get(kind) }));
}

/** The label the app gave a target id, or the id itself. */
export function targetLabel(targets, id) {
  const match = (Array.isArray(targets) ? targets : []).find(target => target && target.id === id);
  return match ? (match.label || match.id) : id;
}

/** Arguments for highlight_target, or the i18n key of what is missing. */
export function highlightArgs(targetId, seconds) {
  if (!targetId) return { errorKey: 'highlightPickTarget' };
  const n = Number(seconds);
  return { args: { targetId, durationSeconds: HIGHLIGHT_DURATIONS.includes(n) ? n : DEFAULT_HIGHLIGHT_SECONDS } };
}

/** A cheap key that changes when the list does, so a picker is rebuilt only then. */
export function targetsSignature(targets) {
  return (Array.isArray(targets) ? targets : []).map(target => target && target.id).join('|');
}
