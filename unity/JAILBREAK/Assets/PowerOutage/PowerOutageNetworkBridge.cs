using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Drives <see cref="PowerOutage"/> from the authoritative backend world state.
/// Listens to <c>NetworkManager.OnWorldStateEvent</c>: when
/// <c>ventilationPowered</c> flips to false (correct server sabotaged) the
/// outage triggers; when it flips back to true, power is restored.
///
/// Hydrates from <c>NetworkManager.CachedWorldState</c> on Start so reconnects
/// and late scene loads still see the correct lighting state.
/// </summary>
[RequireComponent(typeof(PowerOutage))]
public class PowerOutageNetworkBridge : MonoBehaviour
{
    [Tooltip("If true, logs each network-driven power state change.")]
    public bool debugLogs = false;

    private PowerOutage power;
    private bool subscribed;
    private bool? lastApplied;

    private void Awake()
    {
        power = GetComponent<PowerOutage>();
    }

    private void OnEnable()
    {
        SubscribeIfNeeded();
    }

    private void Start()
    {
        SubscribeIfNeeded();
        var net = NetworkManager.Instance;
        if (net != null && net.CachedWorldState != null)
            ApplyWorldState(net.CachedWorldState);
    }

    private void Update()
    {
        // NetworkManager singleton may not exist yet when this scene first loads.
        if (!subscribed) SubscribeIfNeeded();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void SubscribeIfNeeded()
    {
        if (subscribed) return;
        var net = NetworkManager.Instance;
        if (net == null) return;

        net.OnWorldStateEvent += ApplyWorldState;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        var net = NetworkManager.Instance;
        if (net != null) net.OnWorldStateEvent -= ApplyWorldState;
        subscribed = false;
    }

    private void ApplyWorldState(WorldStatePayload payload)
    {
        if (payload == null || power == null) return;

        bool powered = payload.ventilationPowered;
        if (lastApplied.HasValue && lastApplied.Value == powered) return;

        if (powered)
        {
            power.RestorePower();
            if (debugLogs) Debug.Log("[PowerOutageNetworkBridge] Power restored from world state.", this);
        }
        else
        {
            power.TriggerPowerOutage();
            if (debugLogs) Debug.Log("[PowerOutageNetworkBridge] Power outage triggered from world state.", this);
        }
        lastApplied = powered;
    }
}
