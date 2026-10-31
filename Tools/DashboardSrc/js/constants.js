    /* ================================================================
     * CONSTANTS
     * ============================================================== */
    export const WS_PORT           = 7071;
    export const WS_PATH           = '/eval';
    export const RECONNECT_BASE_MS = 1_500;
    export const RECONNECT_MAX_MS  = 30_000;
    export const RECONNECT_JITTER  = 0.25;
    export const MAX_LOG_ITEMS     = 60;
    // Meta Quest player casting (opened on demand in a side-by-side popup).
    export const CAST_URL          = 'https://horizon.meta.com/casting/';
    // Evaluator commands: how long the app has to acknowledge one before the page gives up.
    export const COMMAND_ACK_TIMEOUT_MS = 5_000;
