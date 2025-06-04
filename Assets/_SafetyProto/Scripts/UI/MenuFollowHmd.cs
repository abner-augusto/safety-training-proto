using UnityEngine;

public class MenuFollowHmd : MonoBehaviour
{
    [Header("HMD Source")]
    [SerializeField]
    private Transform hmdTransform; // Assign your HMD/camera here (OVRCameraRig CenterEyeAnchor, or Camera.main.transform)

    [Header("Positioning")]
    public float followDistance = 2f; // Distance in front of user
    public Vector3 menuOffset = Vector3.zero; // Optional offset for fine-tuning

    [Header("Smoothing")]
    public float positionLerpSpeed = 5f;
    public float rotationLerpSpeed = 7f;

    void Start()
    {
        // Auto-assign Camera if not set
        if (hmdTransform == null)
        {
            // For Meta XR, Camera.main is usually correct
            if (Camera.main != null)
                hmdTransform = Camera.main.transform;
            else
                Debug.LogError("MenuFollowHmd: Please assign the HMD Transform (Camera) in the Inspector!");
        }
    }

    void LateUpdate()
    {
        if (hmdTransform == null) return;

        // Target position: in front of HMD at a fixed distance
        Vector3 targetPosition = hmdTransform.position + hmdTransform.forward * followDistance + hmdTransform.TransformVector(menuOffset);

        // Smooth move to position
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionLerpSpeed);

        // Target rotation: face toward the HMD, but keep upright
        Vector3 lookDirection = (transform.position - hmdTransform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);

        // Smooth rotate to face camera
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationLerpSpeed);
    }
}