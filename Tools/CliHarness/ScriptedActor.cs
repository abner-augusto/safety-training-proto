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
                    // Simulates the participant pressing a phase gate in Evaluation mode:
                    // every pending task in the current group closes as Omitted and the
                    // session advances (next group or SessionCompleted). In Guided mode the
                    // real gates block instead — scripted Guided runs should not use this.
                    var omitted = _taskManager.MarkPendingTasksOmitted();
                    System.Console.WriteLine($"[ScriptedActor] gate: {omitted.Count} task(s) omitted.");
                    break;

                default:
                    System.Console.Error.WriteLine($"[ScriptedActor] Unknown step kind: {step.Kind}");
                    break;
            }
        }
    }
}
