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

    public ScriptedActor(IEventBus bus, IReadOnlyList<ScriptStepDef> steps)
    {
        _bus = bus;
        _steps = steps;
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

                default:
                    System.Console.Error.WriteLine($"[ScriptedActor] Unknown step kind: {step.Kind}");
                    break;
            }
        }
    }
}
