using UnityEngine;

/// <summary>
/// Controls the Digital Swoop interaction:
///
///   SPACEBAR            → Tab swoops from the computer position onto the wall beside the sensor.
///   P2 enters 3.3 m     → Tab smoothly follows P2 (X-axis only).
///   P2 enters 1.2 m     → Tab pans to the midpoint between sensor and P2.
///   P2 exits  2.2 m     → Tab returns smoothly to its home position beside the sensor.
///
/// The tab's Y and Z are fixed at all times — only X moves.
///
/// SETUP:
///   • person1Transform  → An empty GameObject placed at the camera/sensor position (fixed).
///   • person2Anchor     → A PersonAnchor tracking Person 2 (Brekel body ID 0 or 1).
/// </summary>
public class DigitalTabController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------

    [Header("Positions")]
    [Tooltip("Empty GameObject placed at the CONNECT camera / sensor position (fixed, no tracking script needed)")]
    public Transform person1Transform;

    [Tooltip("PersonAnchor component tracking Person 2")]
    public PersonAnchor person2Anchor;

    [Header("Tab Position")]
    [Tooltip("Fixed Y height of the tab on the wall — never changes")]
    public float tabFixedY = 1.5f;

    [Tooltip("Fixed Z depth of the tab (the wall plane Z value in world space)")]
    public float tabFixedZ = 0f;

    [Tooltip("X offset from the sensor for the home position (positive = to sensor's right)")]
    public float homeXOffset = 0.5f;

    [Header("Movement")]
    [Tooltip("Lerp speed for all smooth X movements. Higher = snappier; ~3 is a gentle glide")]
    public float moveSpeed = 3f;

    [Header("Swoop Animation")]
    [Tooltip("Transform representing the computer/laptop screen position (swoop source)")]
    public Transform swoopStartPoint;

    [Tooltip("Duration of the swoop-in animation in seconds")]
    public float swoopDuration = 1.2f;

    [Tooltip("Easing curve for the swoop (x = normalized time 0-1, y = progress 0-1)")]
    public AnimationCurve swoopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Proximity Thresholds (metres)")]
    [Tooltip("P2 enters this radius from the sensor → tab begins following P2")]
    public float wideProximity = 3.3f;

    [Tooltip("P2 enters this radius from the sensor → tab moves to midpoint")]
    public float intimateProximity = 1.2f;

    [Tooltip("P2 exits this radius from the sensor → tab returns home (hysteresis buffer)")]
    public float exitProximity = 2.2f;

    // -------------------------------------------------------------------------
    //  State machine
    // -------------------------------------------------------------------------
    private enum TabState
    {
        Hidden,         // before spacebar — tab is invisible
        SwoopingIn,     // animating from computer position to wall
        AtWall,         // resting beside the sensor, waiting for P2
        FollowingP2,    // tracking P2's X
        AtMidpoint,     // locked to midpoint of sensor and P2
        ReturningHome   // lerping back to sensor side
    }

    private TabState _state = TabState.Hidden;

    private float    _swoopTimer;
    private Vector3  _swoopFromPos;
    private Renderer _renderer;

    // -------------------------------------------------------------------------
    //  Unity lifecycle
    // -------------------------------------------------------------------------
    void Start()
    {
        _renderer = GetComponent<Renderer>();
        // Hide visually but keep the GameObject active so Update() keeps running
        SetVisible(false);
    }

    void Update()
    {
        switch (_state)
        {
            case TabState.Hidden:        HandleHidden();        break;
            case TabState.SwoopingIn:    HandleSwoopingIn();    break;
            case TabState.AtWall:        HandleAtWall();        break;
            case TabState.FollowingP2:   HandleFollowingP2();   break;
            case TabState.AtMidpoint:    HandleAtMidpoint();    break;
            case TabState.ReturningHome: HandleReturningHome(); break;
        }
    }

    // -------------------------------------------------------------------------
    //  State handlers
    // -------------------------------------------------------------------------

    private void HandleHidden()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            BeginSwoop();
    }

    private void HandleSwoopingIn()
    {
        _swoopTimer += Time.deltaTime;
        float t      = Mathf.Clamp01(_swoopTimer / swoopDuration);
        float curved = swoopCurve.Evaluate(t);

        Vector3 destination = new Vector3(GetHomeX(), tabFixedY, tabFixedZ);
        transform.position  = Vector3.LerpUnclamped(_swoopFromPos, destination, curved);

        if (t >= 1f)
        {
            transform.position = destination;
            _state = TabState.AtWall;
        }
    }

    private void HandleAtWall()
    {
        MoveToX(GetHomeX());

        if (person2Anchor == null || !person2Anchor.IsTracked) return;

        if (GetFloorDistance() < wideProximity)
            _state = TabState.FollowingP2;
    }

    private void HandleFollowingP2()
    {
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = TabState.ReturningHome;
            return;
        }

        float dist = GetFloorDistance();

        if (dist < intimateProximity)
        {
            _state = TabState.AtMidpoint;
            return;
        }

        if (dist > exitProximity)
        {
            _state = TabState.ReturningHome;
            return;
        }

        MoveToX(person2Anchor.transform.position.x);
    }

    private void HandleAtMidpoint()
    {
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = TabState.ReturningHome;
            return;
        }

        float dist = GetFloorDistance();

        if (dist > exitProximity)
        {
            _state = TabState.ReturningHome;
            return;
        }

        if (dist > intimateProximity)
        {
            _state = TabState.FollowingP2;
            return;
        }

        float midX = (GetSensorX() + person2Anchor.transform.position.x) * 0.5f;
        MoveToX(midX);
    }

    private void HandleReturningHome()
    {
        float homeX = GetHomeX();
        MoveToX(homeX);

        if (Mathf.Abs(transform.position.x - homeX) < 0.02f)
            _state = TabState.AtWall;
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private void BeginSwoop()
    {
        SetVisible(true);
        _swoopTimer   = 0f;
        _swoopFromPos = swoopStartPoint != null
            ? swoopStartPoint.position
            : new Vector3(GetHomeX() - 5f, tabFixedY, tabFixedZ - 3f);
        transform.position = _swoopFromPos;
        _state = TabState.SwoopingIn;
    }

    /// Move tab to target X; Y and Z are always locked
    private void MoveToX(float targetX)
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Lerp(pos.x, targetX, moveSpeed * Time.deltaTime);
        pos.y = tabFixedY;
        pos.z = tabFixedZ;
        transform.position = pos;
    }

    /// Home X = sensor X + homeXOffset
    private float GetHomeX()
    {
        if (person1Transform != null)
            return person1Transform.position.x + homeXOffset;
        return transform.position.x;
    }

    /// Show or hide the tab's visual without deactivating the GameObject
    private void SetVisible(bool visible)
    {
        if (_renderer != null) _renderer.enabled = visible;
    }

    /// Raw sensor X (for midpoint calculation)
    private float GetSensorX()
    {
        return person1Transform != null ? person1Transform.position.x : 0f;
    }

    /// Horizontal (floor-plane XZ) distance from sensor to P2
    private float GetFloorDistance()
    {
        if (person1Transform == null || person2Anchor == null) return float.MaxValue;

        Vector3 sensor = person1Transform.position;
        Vector3 p2     = person2Anchor.transform.position;

        return Vector2.Distance(new Vector2(sensor.x, sensor.z), new Vector2(p2.x, p2.z));
    }

    // -------------------------------------------------------------------------
    //  Scene-view gizmos (proximity rings around the sensor)
    // -------------------------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (person1Transform == null) return;
        Vector3 sensor = person1Transform.position;

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.4f);
        DrawCircleGizmo(sensor, wideProximity);

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.4f);
        DrawCircleGizmo(sensor, exitProximity);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        DrawCircleGizmo(sensor, intimateProximity);
    }

    private static void DrawCircleGizmo(Vector3 center, float radius)
    {
        const int segments = 64;
        float     step     = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a1   = i       * step * Mathf.Deg2Rad;
            float a2   = (i + 1) * step * Mathf.Deg2Rad;
            Vector3 from = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Vector3 to   = center + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * radius;
            Gizmos.DrawLine(from, to);
        }
    }
#endif
}
