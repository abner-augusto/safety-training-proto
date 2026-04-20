using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.CliHarness;

public sealed class InteractiveActor
{
    private readonly IEventBus _bus;
    private readonly IReadOnlyList<ITaskGroup> _taskGroups;

    private static readonly PPEType[] AvailablePPE =
        Enum.GetValues(typeof(PPEType))
            .Cast<PPEType>()
            .Where(p => p != PPEType.None)
            .ToArray();

    public InteractiveActor(IEventBus bus, IReadOnlyList<ITaskGroup> taskGroups)
    {
        _bus = bus;
        _taskGroups = taskGroups;
    }

    public async Task PlayAsync()
    {
        foreach (var group in _taskGroups)
        {
            PrintSectionHeader($"GROUP: {group.groupName}  [{group.executionMode}]");

            foreach (var task in group.tasks)
            {
                await RunTaskAsync(task);
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== All tasks completed ===");
        Console.ResetColor();
    }

    private async Task RunTaskAsync(ISafetyTask task)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"┌─ Task: {task.taskName}");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(task.taskDescription))
            Console.WriteLine($"│  {task.taskDescription}");

        Console.WriteLine($"│  Action ID  : {task.ResolveExpectedActionId()}");
        Console.WriteLine($"│  Points     : +{task.successPoints}");
        if (task.failurePenalty > 0)
            Console.WriteLine($"│  Fail Penalty: -{task.failurePenalty}");
        if (task.ppePenalty > 0)
            Console.WriteLine($"│  PPE Penalty : -{task.ppePenalty}");
        if (!string.IsNullOrWhiteSpace(task.hintText))
            Console.WriteLine($"│  Hint       : {task.hintText}");

        bool hasPPE = task.requiredPPE != null && task.requiredPPE.Count > 0;

        if (hasPPE)
        {
            Console.WriteLine($"│  Required PPE: {string.Join(", ", task.requiredPPE ?? Enumerable.Empty<PPEType>())}");
            Console.WriteLine("│");
        }
        else
        {
            Console.WriteLine("│  (no PPE required)");
            Console.WriteLine("│");
        }

        bool taskDone = false;
        Action<TaskEventArgs> onTaskCompleted = null!;
        var completionSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void SubscribeTaskCompleted()
        {
            onTaskCompleted = args =>
            {
                if (args.Phase != TaskPhase.Completed) return;
                if (args.Task?.taskName != task.taskName) return;
                completionSource.TrySetResult(true);
            };

            _bus.Subscribe(onTaskCompleted);
        }

        SubscribeTaskCompleted();

        try
        {
            while (!taskDone)
            {
                if (hasPPE)
                    await PromptPPESelectionAsync(task);

                bool actionAccepted = await PromptActionAsync(task);

                if (actionAccepted)
                {
                    var timeout = Task.Delay(2000);
                    var completed = await Task.WhenAny(completionSource.Task, timeout);

                    if (completed == completionSource.Task)
                    {
                        taskDone = true;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  [WARN] Task did not complete within timeout. Retrying.");
                        Console.ResetColor();
                        completionSource = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        _bus.Unsubscribe(onTaskCompleted);
                        SubscribeTaskCompleted();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  ↺  Wrong action. Try again.");
                    Console.ResetColor();
                }
            }
        }
        finally
        {
            _bus.Unsubscribe(onTaskCompleted);
        }
    }

    private async Task PromptPPESelectionAsync(ISafetyTask task)
    {
        Console.WriteLine("│  Select PPE to equip (separate multiple numbers with spaces):");
        Console.WriteLine("│  [0] Skip — equip nothing");

        for (int i = 0; i < AvailablePPE.Length; i++)
        {
            bool isRequired = task.requiredPPE.Contains(AvailablePPE[i]);
            string marker = isRequired ? " ◄ required" : "";
            Console.WriteLine($"│  [{i + 1}] {AvailablePPE[i]}{marker}");
        }

        Console.Write("└─ > ");
        string input = Console.ReadLine() ?? string.Empty;

        foreach (var ppe in AvailablePPE)
            _bus.Publish(new PPEStateChangedEventArgs(ppe, false));

        if (input.Trim() == "0" || string.IsNullOrWhiteSpace(input))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  → No PPE equipped.");
            Console.ResetColor();
            await Task.Yield();
            return;
        }

        var chosen = new List<PPEType>();
        foreach (var token in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out int idx) && idx >= 1 && idx <= AvailablePPE.Length)
            {
                var ppe = AvailablePPE[idx - 1];
                _bus.Publish(new PPEStateChangedEventArgs(ppe, true));
                chosen.Add(ppe);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [WARN] '{token}' is not a valid index (1–{AvailablePPE.Length})");
                Console.ResetColor();
            }
        }

        if (chosen.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  → Equipped: {string.Join(", ", chosen)}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  → No valid selection — nothing equipped.");
            Console.ResetColor();
        }

        await Task.Yield();
    }

    private async Task<bool> PromptActionAsync(ISafetyTask task)
    {
        string expectedId = task.ResolveExpectedActionId();

        Console.WriteLine($"  Perform action [{expectedId}]");
        Console.WriteLine("  Press Enter to perform, or type a different action ID to simulate wrong action:");
        Console.Write("  > ");

        string input = Console.ReadLine() ?? string.Empty;
        string actionId = string.IsNullOrWhiteSpace(input) ? expectedId : input.Trim();

        bool isCorrect = string.Equals(actionId, expectedId, StringComparison.OrdinalIgnoreCase);

        Console.ForegroundColor = isCorrect ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  → Publishing action: {actionId}{(isCorrect ? "" : "  ✗ (wrong)")}");
        Console.ResetColor();

        _bus.Publish(new ActionAttemptedEvent(actionId));

        await Task.Delay(150);

        return isCorrect;
    }

    private static void PrintSectionHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Write($"  {text}  ");
        Console.ResetColor();
        Console.WriteLine();
    }
}
