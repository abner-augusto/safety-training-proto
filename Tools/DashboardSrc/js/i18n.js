    /* ================================================================
     * TRANSLATIONS
     * ============================================================== */
    const i18n = {
      en: {
        currentScore: 'Score',
        elapsed: 'Elapsed',
        violations: 'Violations',
        ppeShort: 'PPE',
        activeTasks: 'Active Tasks',
        activityLog: 'Activity Log',
        sessionReport: 'Session Report',
        violationsOnly: 'Violations only',
        legendTitle: 'Scene Legend',
        legendHmd: 'HMD · gaze',
        legendHands: 'Hands',
        legendPpeLoose: 'PPE (loose)',
        legendPpeWorn: 'PPE (worn)',
        legendInteract: 'Interacting object',
        posture: 'Posture',
        postureStanding: 'STANDING',
        postureLowering: 'LOWERING',
        postureCrouched: 'CROUCHED',
        modeSequential: 'SEQUENTIAL',
        modeFreeOrder: 'FREE ORDER',
        group: 'GROUP',
        sessionRecorded: 'SESSION RECORDED',
        viewport3dFailed: 'Could not load the 3D view. The rest of the panel keeps working.',
        waitingSession: 'Waiting for session to start…',
        noActivity: 'No activity yet',
        reportAfterSession: 'Report available after session',
        viewFullReport: 'View Full Report',
        downloadJson: 'Download JSON',
        duration: 'Duration',
        finalScore: 'Final Score',
        tasks: 'Tasks',
        events: 'Events',
        totalDuration: 'Total Duration',
        tasksCompleted: 'Tasks Completed',
        totalTasks: 'Total Tasks',
        eventTimeline: 'Event Timeline',
        sessionHistory: 'Session History',
        noHistory: 'No saved sessions yet',
        exportAll: 'Export all',
        clearHistory: 'Clear',
        confirmClearHistory: 'Clear all saved sessions?',
        noSession: 'No Session',
        session: 'Session',
        participant: 'Participant',
        modeGuided: 'Guided',
        modeEvaluation: 'Assessment',
        disconnected: 'Disconnected',
        connected: 'Connected',
        connecting: 'Connecting…',
        pending: 'Pending', active: 'Active', completed: 'Done', failed: 'Failed',
        hideCompleted: 'Hide completed',
        // log (the typed glyph comes from the gutter column, not from the text)
        logConnected: 'Connected to training session',
        logDisconnected: 'Connection lost. Reconnecting…',
        logSessionStarted: 'Training session started',
        logStarted: 'Started', logCompleted: 'Completed', logTimeout: 'Timeout',
        logScorePlus: 'Score', logScoreMinus: 'Score', logTotal: 'Total',
        logViolation: 'Safety Violation', logCritical: 'CRITICAL',
        logSessionComplete: 'Session Complete! Score',
        logGroupStarted: 'Group Started', logGroupCompleted: 'Group Completed',
        logSessionReset: 'Session reset', logReportAvailable: 'Report available',
        logSessionPaused: 'Session paused', logSessionResumed: 'Session resumed',
        logSessionEnded: 'Session ended',
        logPpeEquipped: 'PPE Equipped', logPpeRemoved: 'PPE Removed',
        logActionAttempt: 'Action', logSafetyError: 'Safety Error', at: 'at',
        // report detail line (scaffolding labels)
        detailPpe: 'PPE', detailWorn: 'worn', detailRemoved: 'removed',
        detailDelta: 'Delta', detailTotal: 'Total', detailIn: 'in',
        detailTime: 'Time', detailScore: 'Score', detailCompleted: 'Completed',
        unknownAction: 'Uncatalogued action', unknownEvent: 'Unrecognized event',
        internalError: 'Internal system error', manualReset: 'The session was restarted manually.', origin: 'Source',
      },
      pt: {
        currentScore: 'Pontuação',
        elapsed: 'Tempo',
        violations: 'Violações',
        ppeShort: 'EPI',
        activeTasks: 'Tarefas Ativas',
        activityLog: 'Registro de Atividades',
        sessionReport: 'Relatório da Sessão',
        hideCompleted: 'Ocultar concluídas',
        violationsOnly: 'Só violações',
        legendTitle: 'Legenda do Espaço',
        legendHmd: 'HMD · olhar',
        legendHands: 'Mãos',
        legendPpeLoose: 'EPI solto',
        legendPpeWorn: 'EPI equipado',
        legendInteract: 'Objeto em interação',
        posture: 'Postura',
        postureStanding: 'EM PÉ',
        postureLowering: 'ABAIXANDO',
        postureCrouched: 'AGACHADO',
        modeSequential: 'SEQUENCIAL',
        modeFreeOrder: 'ORDEM LIVRE',
        group: 'GRUPO',
        sessionRecorded: 'SESSÃO REGISTRADA',
        viewport3dFailed: 'Não foi possível carregar a visualização 3D. O restante do painel continua funcionando.',
        waitingSession: 'Aguardando início da sessão…',
        noActivity: 'Nenhuma atividade ainda',
        reportAfterSession: 'Relatório disponível após a sessão',
        viewFullReport: 'Ver Relatório Completo',
        downloadJson: 'Baixar JSON',
        duration: 'Duração',
        finalScore: 'Pontuação Final',
        tasks: 'Tarefas',
        events: 'Eventos',
        totalDuration: 'Duração Total',
        tasksCompleted: 'Tarefas Concluídas',
        totalTasks: 'Total de Tarefas',
        eventTimeline: 'Linha do Tempo',
        sessionHistory: 'Histórico de Sessões',
        noHistory: 'Nenhuma sessão salva ainda',
        exportAll: 'Exportar tudo',
        clearHistory: 'Limpar histórico',
        confirmClearHistory: 'Limpar todas as sessões salvas?',
        noSession: 'Sem Sessão',
        session: 'Sessão',
        participant: 'Participante',
        modeGuided: 'Guiado',
        modeEvaluation: 'Avaliação',
        disconnected: 'Desconectado',
        connected: 'Conectado',
        connecting: 'Conectando…',
        pending: 'Pendente', active: 'Ativa', completed: 'Concluída', failed: 'Falhou',
        // log (the typed glyph comes from the gutter column, not from the text)
        logConnected: 'Conectado à sessão de treinamento',
        logDisconnected: 'Conexão perdida. Reconectando…',
        logSessionStarted: 'Sessão de treinamento iniciada',
        logStarted: 'Iniciada', logCompleted: 'Concluída', logTimeout: 'Tempo esgotado',
        logScorePlus: 'Pontos', logScoreMinus: 'Pontos', logTotal: 'Total',
        logViolation: 'Violação de Segurança', logCritical: 'CRÍTICO',
        logSessionComplete: 'Sessão Completa! Pontuação',
        logGroupStarted: 'Grupo Iniciado', logGroupCompleted: 'Grupo Concluído',
        logSessionReset: 'Sessão reiniciada', logReportAvailable: 'Relatório disponível',
        logSessionPaused: 'Sessão pausada', logSessionResumed: 'Sessão retomada',
        logSessionEnded: 'Sessão encerrada',
        logPpeEquipped: 'EPI Equipado', logPpeRemoved: 'EPI Removido',
        logActionAttempt: 'Ação', logSafetyError: 'Erro de Segurança', at: 'em',
        // report detail line (scaffolding labels)
        detailPpe: 'EPI', detailWorn: 'equipado', detailRemoved: 'removido',
        detailDelta: 'Variação', detailTotal: 'Total', detailIn: 'em',
        detailTime: 'Tempo', detailScore: 'Pontuação', detailCompleted: 'Concluídas',
        unknownAction: 'Ação não catalogada', unknownEvent: 'Evento não reconhecido',
        internalError: 'Erro interno do sistema', manualReset: 'A sessão foi reiniciada manualmente.', origin: 'Origem',
      }
    };

    const labels = {
      ppe: {
        en: {
          None: 'None', Helmet: 'Helmet', Goggles: 'Safety glasses', Harness: 'Safety harness',
          Vest: 'Safety vest', Boots: 'Safety boots', GloveLeft: 'Left glove', GloveRight: 'Right glove',
        },
        pt: {
          None: 'Nenhum', Helmet: 'Capacete', Goggles: 'Óculos de proteção', Harness: 'Cinto paraquedista',
          Vest: 'Colete de segurança', Boots: 'Botina de segurança', GloveLeft: 'Luva esquerda', GloveRight: 'Luva direita',
        },
      },
      action: {
        en: {
          connect_harness: 'Connect lanyard', install_guardrail: 'Install guardrail',
          install_toeboard: 'Install toe board', flag_safety_net: 'Report safety-net issue',
        },
        pt: {
          connect_harness: 'Conectar talabarte', install_guardrail: 'Instalar guarda-corpo',
          install_toeboard: 'Instalar rodapé', flag_safety_net: 'Reportar irregularidade na tela fachadeira',
        },
      },
      violation: {
        en: {
          ACTION_ID_MISSING: 'Unidentified action', NO_ACTIVE_GROUP: 'No active task group',
          WRONG_ACTION: 'Incorrect action', PPE_MISSING: 'Required PPE missing',
          TASK_OMITTED: 'Task omitted', GATE_FAILED: 'Inspection not approved',
          WRONG_PPE_SELECTED: 'Incorrect equipment selected', INSPECTION_INCOMPLETE: 'Inspection incomplete',
          ORDER_VIOLATION: 'Recommended order not followed',
        },
        pt: {
          ACTION_ID_MISSING: 'Ação não identificada', NO_ACTIVE_GROUP: 'Nenhum grupo de tarefas ativo',
          WRONG_ACTION: 'Ação incorreta', PPE_MISSING: 'EPI obrigatório ausente',
          TASK_OMITTED: 'Tarefa omitida', GATE_FAILED: 'Inspeção não aprovada',
          WRONG_PPE_SELECTED: 'Equipamento inadequado selecionado', INSPECTION_INCOMPLETE: 'Inspeção incompleta',
          ORDER_VIOLATION: 'Ordem recomendada não seguida',
        },
      },
    };

    let lang = localStorage.getItem('sp_lang') === 'en' ? 'en' : 'pt';
    export const t = k => (i18n[lang][k] ?? i18n.pt[k] ?? k);
    export function getLang() { return lang; }
    export function locale() { return lang === 'pt' ? 'pt-BR' : 'en-US'; }
    export function ppeLabel(value) { return labels.ppe[lang][value] ?? labels.ppe.pt[value] ?? t('ppeShort'); }
    export function actionLabel(value) {
      return labels.action[lang][value] ?? labels.action.pt[value] ?? `${t('unknownAction')} (${value || '—'})`;
    }
    export function violationLabel(value) {
      return labels.violation[lang][value] ?? labels.violation.pt[value] ?? `${t('logViolation')} (${value || '—'})`;
    }
    export function violationMessage(code, message) {
      if (!message) return '';
      if (lang !== 'pt') return message;
      if (code === 'ACTION_ID_MISSING' && /^Received action/i.test(message)) return 'Tentativa de ação recebida sem identificação válida.';
      if (code === 'NO_ACTIVE_GROUP' && /^Action attempted/i.test(message)) return 'Ação realizada sem um grupo de tarefas ativo.';
      if (code === 'WRONG_ACTION' && /^(Expected|Action )/i.test(message)) return 'A ação realizada não corresponde à etapa atual.';
      if (code === 'PPE_MISSING' && /^Required PPE/i.test(message)) return 'Faltam EPIs obrigatórios para a tarefa.';
      const portugueseMessages = {
        ACTION_ID_MISSING: /^Tentativa de ação /,
        NO_ACTIVE_GROUP: /^Ação realizada /,
        WRONG_ACTION: /^A (?:tarefa esperada|ação realizada) /,
        PPE_MISSING: /^Faltam EPIs /,
        TASK_OMITTED: /^Tarefa omitida /,
        GATE_FAILED: /^Tentou iniciar /,
        WRONG_PPE_SELECTED: /^Selecionou equipamento /,
        INSPECTION_INCOMPLETE: /^Tentou iniciar /,
        ORDER_VIOLATION: /^EPIs equipados /,
      };
      return portugueseMessages[code]?.test(message) ? message : '';
    }
    export function toggleLang() {
      lang = lang === 'pt' ? 'en' : 'pt';
      localStorage.setItem('sp_lang', lang);
      return lang;
    }

    export function applyTranslations() {
      document.querySelectorAll('[data-i18n]').forEach(el => {
        el.textContent = t(el.dataset.i18n);
      });
      // FAB shows the currently-active language (PT / EN)
      document.getElementById('lang-flag').textContent = lang === 'pt' ? 'PT' : 'EN';
      // keep the document language in sync for screen readers / hyphenation
      document.documentElement.lang = lang === 'pt' ? 'pt-BR' : 'en';
    }
