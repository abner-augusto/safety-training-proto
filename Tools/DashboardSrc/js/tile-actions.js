/* Quick actions on a headset tile of the multi-headset shell. Pure data and rules, shared by the
   shell (which draws the buttons) and the embedded station page (which accepts only these
   commands from its parent), so a new action is one entry here plus its strings in i18n.js. */

export const TILE_ACTIONS = [
  { command: 'return_all_ppe', labelKey: 'tileReturnObjects', confirmKey: 'commandConfirmButton',
    successKey: 'returnObjectsSuccess', failedKey: 'returnObjectsFailed' },
  { command: 'recenter_player', labelKey: 'tileRecenter', confirmKey: 'commandConfirmButton',
    successKey: 'recenterSuccess', failedKey: 'recenterFailed' },
  { command: 'highlight_target', labelKey: 'tileHighlight', confirmKey: 'commandConfirmButton',
    successKey: 'highlightSuccess', failedKey: 'highlightFailed', needsTarget: true },
  { command: 'reset_session', labelKey: 'tileReset', confirmKey: 'tileResetConfirm',
    successKey: 'resetSessionSuccess', failedKey: 'resetSessionFailed', danger: true, needsSession: true },
  { command: 'end_practice', labelKey: 'tileEndPractice', confirmKey: 'commandConfirmButton',
    successKey: 'endPracticeSuccess', failedKey: 'endPracticeFailed', onlyWhilePracticing: true },
];

/* Boundary check for a message from the parent window: only a listed command with a request id
   reaches the headset's socket. */
export function isTileCommand(envelope) {
  return !!envelope && envelope.eventType === 'Command' && !!envelope.requestId
    && TILE_ACTIONS.some(action => action.command === envelope.command);
}

/* `status` is the tile's last report, or null while the headset is offline or silent. Mirrors the
   station page, which refuses a restart on the waiting screen and allows it on apps that predate
   StationStatus. */
export function isActionBlocked(action, status) {
  if (!status || !status.connected) return true;
  if (action.needsTarget && !(Array.isArray(status.highlightTargets) && status.highlightTargets.length)) return true;
  if (action.needsSession) return !!status.hasStationStatus && !status.sessionActive && !status.sessionCompleted;
  return false;
}

/* Whether the tile draws the button at all. Most actions stay on the tile and are only blocked;
   an `onlyWhilePracticing` action appears with the practice area and goes with it, so the tile
   never carries a button that can only be refused. */
export function isActionVisible(action, status) {
  if (!action.onlyWhilePracticing) return true;
  return !!status && !!status.connected && status.practice === true;
}
