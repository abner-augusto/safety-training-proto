using System.Collections.Generic;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.CliHarness;

public sealed class ScriptedActor
{
    public sealed class Step
    {
        public string kind { get; set; } = string.Empty;
        public string? ppeType { get; set; }
        public bool isWearing { get; set; }
        public string? actionId { get; set; }
        public int delayMs { get; set; } = 100;
    }

    private readonly IEventBus _bus;
    private readonly IReadOnlyList<Step> _steps;

    public ScriptedActor(IEventBus bus, IReadOnlyList<Step> steps)
    {
        _bus = bus;
        _steps = steps;
    }

    public async Task PlayAsync()
    {
        foreach (var step in _steps)
        {
            if (step.delayMs > 0)
            {
                await Task.Delay(step.delayMs);
            }

            switch (step.kind)
            {
                case "ppe":
                    if (Enum.TryParse<PPEType>(step.ppeType, ignoreCase: true, out var ppe))
                    {
                        _bus.Publish(new PPEStateChangedEventArgs(ppe, step.isWearing));
                    }
                    else
                    {
                        System.Console.Error.WriteLine($"[ScriptedActor] Unknown PPE type: {step.ppeType}");
                    }
                    break;

                case "action":
                    _bus.Publish(new ActionAttemptedEvent(step.actionId ?? string.Empty));
                    break;

                default:
                    System.Console.Error.WriteLine($"[ScriptedActor] Unknown step kind: {step.kind}");
                    break;
            }
        }
    }
}
