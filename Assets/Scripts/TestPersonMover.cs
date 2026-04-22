using UnityEngine;

/// <summary>
/// OFFLINE TESTING ONLY — remove or disable before live demo.
///
/// Moves this GameObject on the XZ floor plane using keyboard input,
/// simulating a person walking around. Y is always locked.
///
/// Controls:
///   W / Up Arrow    → move forward  (–Z)
///   S / Down Arrow  → move backward (+Z)
///   A / Left Arrow  → move left     (–X)
///   D / Right Arrow → move right    (+X)
///
/// Attach to InvisibleAnchor_P2 alongside PersonAnchor (with manualOverride = true).
/// A live distance readout is shown in the Inspector while playing so you can
/// verify the proximity thresholds without needing a camera feed.
/// </summary>
public class TestPersonMover : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Movement speed in metres per second")]
    public float speed = 1.5f;

    [Header("Sensor Reference (read-only debug)")]
    [Tooltip("Point the sensor / Person 1 transform here to see live distance in the Inspector")]
    public Transform sensorTransform;

    [Header("— Live Readout (Inspector only) —")]
    [SerializeField, HideInInspector]
    private float _currentDistanceFromSensor;

    [SerializeField, HideInInspector]
    private string _proximityZone = "—";

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A/D or Left/Right
        float v = Input.GetAxisRaw("Vertical");   // W/S or Up/Down

        Vector3 move = new Vector3(h, 0f, -v).normalized * speed * Time.deltaTime;
        transform.position += move;

        // Lock Y so the anchor stays on the floor plane
        Vector3 pos = transform.position;
        pos.y = 0f;
        transform.position = pos;

        UpdateDebugReadout();
    }

    private void UpdateDebugReadout()
    {
        if (sensorTransform == null) return;

        Vector3 s = sensorTransform.position;
        Vector3 p = transform.position;
        _currentDistanceFromSensor = Vector2.Distance(
            new Vector2(s.x, s.z),
            new Vector2(p.x, p.z));

        // Mirror the thresholds from DigitalTabController defaults
        // so you can read the zone directly in the Inspector
        if (_currentDistanceFromSensor < 1.2f)
            _proximityZone = "INTIMATE  (< 1.2 m) → tab at midpoint";
        else if (_currentDistanceFromSensor < 2.2f)
            _proximityZone = "CLOSE     (1.2 – 2.2 m) → tab follows P2";
        else if (_currentDistanceFromSensor < 3.3f)
            _proximityZone = "WIDE      (2.2 – 3.3 m) → tab follows P2";
        else
            _proximityZone = "OUTSIDE   (> 3.3 m) → tab at home";
    }

    // Show proximity rings in Scene view around THIS object while testing
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);

        if (sensorTransform == null) return;

        // Draw a line from sensor to P2 for visual distance feedback
        Gizmos.color = Color.white;
        Gizmos.DrawLine(sensorTransform.position, transform.position);
    }
#endif
}
