using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.CliHarness;

public sealed class HarnessPPEManager : IPPEComplianceChecker, IDisposable
{
    private readonly IEventBus _bus;
    private readonly Dictionary<PPEType, bool> _worn = new();
    private readonly Action<PPEStateChangedEventArgs> _onPpeStateChanged;
    private bool _subscribed;

    public HarnessPPEManager(IEventBus bus)
    {
        _bus = bus;
        _onPpeStateChanged = OnPpeStateChanged;
    }

    public void Subscribe()
    {
        if (_subscribed) return;
        _bus.Subscribe(_onPpeStateChanged);
        _subscribed = true;
    }

    public void Unsubscribe()
    {
        if (!_subscribed) return;
        _bus.Unsubscribe(_onPpeStateChanged);
        _subscribed = false;
    }

    private void OnPpeStateChanged(PPEStateChangedEventArgs args)
    {
        _worn[args.PpeType] = args.IsWearing;
    }

    public bool IsCompliant(IReadOnlyCollection<PPEType> requiredPpe)
    {
        if (requiredPpe == null || requiredPpe.Count == 0) return true;
        foreach (var ppe in requiredPpe)
        {
            if (!_worn.TryGetValue(ppe, out var isWearing) || !isWearing) return false;
        }
        return true;
    }

    public void Dispose() => Unsubscribe();
}
