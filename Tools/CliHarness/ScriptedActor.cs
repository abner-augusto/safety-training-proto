using System.Collections.Generic;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.CliHarness;

public sealed class ScriptedActor
{
    private readonly IEventBus _bus;
    private readonly IReadOnlyList<ScriptStepDef> _steps;
    private readonly SafetyProto.Domain.Tasks.TaskManagerCore _taskManager;

    public ScriptedActor(IEventBus bus, IReadOnlyList<ScriptStepDef> steps,
        SafetyProto.Domain.Tasks.TaskManagerCore taskManager)
    {
        _bus = bus;
        _steps = steps;
        _taskManager = taskManager;
    }

    public async Task PlayAsync()
    {
        foreach (var step in _steps)
        {
            if (step.DelayMs > 0)
            {
                await Task.Delay(step.DelayMs);
            }

            switch (step.Kind)
            {
                case "ppe":
                    if (Enum.TryParse<PPEType>(step.PpeType, ignoreCase: true, out var ppe))
                    {
                        _bus.Publish(new PPEStateChangedEventArgs(ppe, step.IsWearing));
                    }
                    else
                    {
                        System.Console.Error.WriteLine($"[ScriptedActor] Unknown PPE type: {step.PpeType}");
                    }
                    break;

                case "action":
                    _bus.Publish(new ActionAttemptedEvent(step.ActionId ?? string.Empty));
                    break;

                case "gate":
                    RunGate(step);
                    break;

                default:
                    System.Console.Error.WriteLine($"[ScriptedActor] Unknown step kind: {step.Kind}");
                    break;
            }
        }
    }

    /// <summary>
    /// Group whose gate is the Evaluation phase-advance button — <c>PhaseController.targetGroupId</c>
    /// in the scene. Same convention <c>SessionSimulator</c> uses to resolve an empty gateTarget.
    /// </summary>
    private const string PhaseGateGroupId = "ppe_selection";

    /// <summary>
    /// Simulates the participant pressing a gate in Evaluation mode, mirroring what the
    /// corresponding scene component does so a scripted run closes the session the same
    /// way a Unity run does:
    /// <list type="bullet">
    /// <item><c>phase1</c> — <c>PhaseController</c>: closes its group, marking any PPE the
    /// participant skipped as NotPerformed. It only acts while that group is current;
    /// pressing it after the group already completed on its own is a no-op, exactly as in the
    /// scene, where the click is rejected for not matching <c>targetGroupId</c>.</item>
    /// <item><c>inspection</c> — <c>InspectionGateValidator</c>: closes the current group the
    /// same way. Both gates run the one closer; only the group they act on differs.</item>
    /// </list>
    /// An empty target is inferred from the current group, keeping older CLI scripts working.
    /// In Guided mode the real gates block instead — scripted Guided runs should not use this.
    /// Note the phase-1 order penalty (ORDER_VIOLATION) is NOT mirrored here.
    /// </summary>
    private void RunGate(ScriptStepDef step)
    {
        var currentGroup = _taskManager.GetCurrentGroup();
        var target = (step.GateTarget ?? string.Empty).Trim().ToLowerInvariant();
        if (target.Length == 0)
            target = currentGroup?.id == PhaseGateGroupId ? "phase1" : "inspection";

        switch (target)
        {
            case "phase" or "phase1" or "fase1":
                if (currentGroup?.id != PhaseGateGroupId)
                {
                    System.Console.WriteLine(
                        $"[ScriptedActor] gate phase1: no-op — current group is " +
                        $"'{currentGroup?.id ?? "(none)"}', not '{PhaseGateGroupId}'.");
                    return;
                }
                var skipped = _taskManager.CloseCurrentGroup();
                System.Console.WriteLine($"[ScriptedActor] gate phase1: group closed, {skipped.Count} task(s) not performed.");
                break;

            case "inspection" or "final" or "inspecao":
                var pending = _taskManager.CloseCurrentGroup();
                System.Console.WriteLine($"[ScriptedActor] gate inspection: {pending.Count} task(s) not performed.");
                break;

            default:
                System.Console.Error.WriteLine($"[ScriptedActor] Unknown gate target: {step.GateTarget}");
                break;
        }
    }
}
