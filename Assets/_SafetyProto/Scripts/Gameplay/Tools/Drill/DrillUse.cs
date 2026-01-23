using Oculus.Interaction.HandGrab;
using UnityEngine;

public class DrillUse : MonoBehaviour, IHandGrabUseDelegate
{
    [Header("Input")]
    [SerializeField]
    private Transform _trigger;
    [SerializeField]
    private AnimationCurve _triggerRotationCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField]
    private SnapAxis _axis = SnapAxis.X;
    [SerializeField]
    [Range(0f, 1f)]
    private float _releaseThreshold = 0.3f;
    [SerializeField]
    [Range(0f, 1f)]
    private float _fireThreshold = 0.9f;
    [SerializeField]
    private float _triggerSpeed = 3f;
    [SerializeField]
    private AnimationCurve _strengthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Motor")]
    [SerializeField]
    private AudioSource _motorAudio;
    [SerializeField]
    private AudioSource _clutchAudio;
    [SerializeField]
    private float _clutchCooldown = 0.2f;
    [SerializeField]
    private float _rpm = 1200f;

    [Header("Fastener")]
    [SerializeField]
    private Transform _bitTip;
    [SerializeField]
    private Transform _forward;
    [SerializeField]
    private LayerMask _fastenerMask = 0;
    [SerializeField]
    private float _sphereCastRadius = 0.01f;
    [SerializeField]
    private float _sphereCastDistance = 0.05f;

    private bool _isSpinning;
    private float _dampedUseStrength;
    private float _lastUseTime;
    private float _lastClutchTime;
    private Quaternion _triggerInitialLocalRot;

    private static readonly RaycastHit[] _sphereCastHits = new RaycastHit[4];

    private void Awake()
    {
        if (_trigger != null)
        {
            _triggerInitialLocalRot = _trigger.localRotation;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_axis == 0)
        {
            return;
        }

        int axisCount = 0;
        if ((_axis & SnapAxis.X) != 0)
        {
            axisCount++;
        }
        if ((_axis & SnapAxis.Y) != 0)
        {
            axisCount++;
        }
        if ((_axis & SnapAxis.Z) != 0)
        {
            axisCount++;
        }

        if (axisCount > 1)
        {
            Debug.LogWarning("DrillUse: Use a single SnapAxis for trigger rotation to avoid compounded axes.", this);
        }
    }
#endif

    public void BeginUse()
    {
        _dampedUseStrength = 0f;
        _lastUseTime = Time.realtimeSinceStartup;
        _lastClutchTime = 0f;
        StopMotor();
    }

    public void EndUse()
    {
        StopMotor();
    }

    public float ComputeUseStrength(float strength)
    {
        float now = Time.realtimeSinceStartup;
        float delta = now - _lastUseTime;
        delta = Mathf.Max(0f, delta); // Clamp negative delta to avoid time rewind issues.
        _lastUseTime = now;

        if (strength > _dampedUseStrength)
        {
            _dampedUseStrength = Mathf.Lerp(_dampedUseStrength, strength, _triggerSpeed * delta);
        }
        else
        {
            _dampedUseStrength = strength;
        }

        float progress = _strengthCurve.Evaluate(_dampedUseStrength);
        UpdateTriggerRotation(progress);
        UpdateMotorState(progress);

        if (_isSpinning && delta > 0f)
        {
            DriveFastener(delta);
        }

        return progress;
    }

    private void UpdateTriggerRotation(float progress)
    {
        if (_trigger == null)
        {
            return;
        }

        float value = _triggerRotationCurve.Evaluate(progress);
        Quaternion axisRotation = Quaternion.identity;
        if ((_axis & SnapAxis.X) != 0)
        {
            axisRotation = axisRotation * Quaternion.AngleAxis(value, Vector3.right);
        }
        if ((_axis & SnapAxis.Y) != 0)
        {
            axisRotation = axisRotation * Quaternion.AngleAxis(value, Vector3.up);
        }
        if ((_axis & SnapAxis.Z) != 0)
        {
            axisRotation = axisRotation * Quaternion.AngleAxis(value, Vector3.forward);
        }

        _trigger.localRotation = _triggerInitialLocalRot * axisRotation; // Apply rotation on top of initial local rotation.
    }

    private void UpdateMotorState(float progress)
    {
        if (progress >= _fireThreshold && !_isSpinning)
        {
            StartMotor();
        }
        else if (progress <= _releaseThreshold && _isSpinning)
        {
            StopMotor();
        }
    }

    private void StartMotor()
    {
        _isSpinning = true;
        if (_motorAudio != null && !_motorAudio.isPlaying)
        {
            _motorAudio.Play();
        }
    }

    private void StopMotor()
    {
        _isSpinning = false;
        if (_motorAudio != null)
        {
            _motorAudio.Stop();
        }
    }

    private void DriveFastener(float deltaTime)
    {
        if (_bitTip == null || _forward == null)
        {
            return;
        }

        Vector3 origin = _bitTip.position;
        Vector3 direction = _forward.forward;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            _sphereCastRadius,
            direction,
            _sphereCastHits,
            _sphereCastDistance,
            _fastenerMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            return;
        }

        float closestDistance = float.PositiveInfinity;
        Collider closestCollider = null;
        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = _sphereCastHits[i].collider;
            if (candidate == null)
            {
                continue;
            }

            float distance = _sphereCastHits[i].distance;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCollider = candidate;
            }
        }

        FastenerSocket socket = closestCollider != null
            ? closestCollider.GetComponentInParent<FastenerSocket>()
            : null;

        if (socket == null)
        {
            return;
        }

        bool clutching;
        socket.ApplyDrive(_rpm, origin, direction, deltaTime, out clutching);

        if (clutching)
        {
            TryPlayClutch();
        }
    }

    private void TryPlayClutch()
    {
        if (_clutchAudio == null)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - _lastClutchTime < _clutchCooldown)
        {
            return;
        }

        _lastClutchTime = now;
        if (_clutchAudio.clip != null)
        {
            _clutchAudio.PlayOneShot(_clutchAudio.clip);
        }
        else
        {
            _clutchAudio.Play();
        }
    }
}

[System.Flags]
public enum SnapAxis
{
    X = 1 << 0,
    Y = 1 << 1,
    Z = 1 << 2
}
