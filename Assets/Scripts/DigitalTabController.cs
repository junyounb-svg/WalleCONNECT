using UnityEngine;

/// <summary>
/// Controls the Digital Swoop interaction:
///
///   SPACEBAR                          → Tab pops up beside the sensor (home).
///   P2.x drops below entry (1.7)      → Tab follows P2 smoothly.
///   P2.x drops below intimate (0.5)   → Tab moves to midpoint between home and P2.
///   P2.x rises above exit (0) AFTER
///     having reached x ≤ exit         → Tab returns home.
///
/// Movement uses SmoothDamp for an organic bell-curve velocity feel
/// (slow start → fast mid → slow arrival).
/// </summary>
public class DigitalTabController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------

    [Header("Positions")]
    [Tooltip("Empty GameObject at the CONNECT camera / sensor position (fixed)")]
    public Transform person1Transform;

    [Tooltip("PersonAnchor component on InvisibleAnchor_P2")]
    public PersonAnchor person2Anchor;

    [Header("Tab Position")]
    [Tooltip("Fixed Y height of the tab on the wall — never changes")]
    public float tabFixedY = 1.5f;

    [Tooltip("Fixed Z depth of the tab (wall plane Z value in world space)")]
    public float tabFixedZ = 0f;

    [Tooltip("X offset from the sensor for the home position")]
    public float homeXOffset = 0.5f;

    [Header("Movement Feel")]
    [Tooltip("Approx time to reach target (seconds). Lower = snappier. Try 0.15 for 0.3-0.4 s reach")]
    public float smoothTime = 0.15f;

    [Tooltip("Maximum movement speed (units/sec) — caps the SmoothDamp velocity")]
    public float maxSpeed = 20f;

    [Header("Swoop Animation")]
    [Tooltip("How far below the resting position the tab starts its rise (metres)")]
    public float verticalRiseDistance = 0.4f;

    [Tooltip("Duration of the swoop-in animation in seconds")]
    public float swoopDuration = 0.4f;

    [Tooltip("Easing curve for the swoop")]
    public AnimationCurve swoopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Proximity Thresholds (World X Position)")]
    [Tooltip("P2's X drops below this → tab starts following")]
    public float entryXThreshold = 1.7f;

    [Tooltip("P2's X drops below this → tab moves to midpoint between home and P2")]
    public float intimateXThreshold = 0.5f;

    [Tooltip("Tab returns home only after P2 has reached this X and then retreats back above it")]
    public float exitXThreshold = 0f;

    // -------------------------------------------------------------------------
    //  State machine
    // -------------------------------------------------------------------------
    private enum TabState
    {
        Hidden,
        SwoopingIn,
        AtWall,
        FollowingP2,
        AtMidpoint,
        ReturningHome
    }

    private TabState _state       = TabState.Hidden;
    private float    _swoopTimer;
    private float    _swoopStartY;
    private Vector3  _originalScale;
    private Renderer _renderer;

    // SmoothDamp state
    private float _xVelocity = 0f;

    // Exit gate: tab can only exit AFTER P2 has physically reached x <= exitXThreshold
    private bool  _p2ReachedExitZone = false;
    private float _p2PrevX           = float.MaxValue;

    // -------------------------------------------------------------------------
    //  Unity lifecycle
    // -------------------------------------------------------------------------
    void Start()
    {
        _renderer      = GetComponent<Renderer>();
        _originalScale = transform.localScale;
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

        Vector3 pos = transform.position;
        pos.x = GetHomeX();
        pos.y = Mathf.Lerp(_swoopStartY, tabFixedY, curved);
        pos.z = tabFixedZ;
        transform.position   = pos;
        transform.localScale = Vector3.Lerp(Vector3.one * 0.01f, _originalScale, curved);

        if (t >= 1f)
        {
            transform.position   = new Vector3(GetHomeX(), tabFixedY, tabFixedZ);
            transform.localScale = _originalScale;
            _state = TabState.AtWall;
        }
    }

    private void HandleAtWall()
    {
        MoveToX(GetHomeX());

        if (person2Anchor == null || !person2Anchor.IsTracked) return;

        float p2x        = person2Anchor.transform.position.x;
        bool  movingInward = p2x < _p2PrevX;
        _p2PrevX = p2x;

        if (movingInward && p2x < entryXThreshold)
        {
            _p2ReachedExitZone = false; // reset exit gate for new approach
            _state = TabState.FollowingP2;
        }
    }

    private void HandleFollowingP2()
    {
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = TabState.ReturningHome;
            return;
        }

        float p2x      = person2Anchor.transform.position.x;
        bool  movingOut = p2x > _p2PrevX;
        _p2PrevX = p2x;

        // Gate: record when P2 has actually reached the exit zone
        if (p2x <= exitXThreshold)
            _p2ReachedExitZone = true;

        // Exit only fires after P2 reached the exit zone and is now retreating past it
        if (_p2ReachedExitZone && movingOut && p2x > exitXThreshold)
        {
            _state = TabState.ReturningHome;
            return;
        }

        // Enter intimate zone (pure position — direction not needed here)
        if (p2x < intimateXThreshold)
        {
            _state = TabState.AtMidpoint;
            return;
        }

        MoveToX(p2x);
    }

    private void HandleAtMidpoint()
    {
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = TabState.ReturningHome;
            return;
        }

        float p2x      = person2Anchor.transform.position.x;
        bool  movingOut = p2x > _p2PrevX;
        _p2PrevX = p2x;

        // Gate: record when P2 has reached the exit zone
        if (p2x <= exitXThreshold)
            _p2ReachedExitZone = true;

        // Exit when P2 has been at exit zone and retreats past it
        if (_p2ReachedExitZone && movingOut && p2x > exitXThreshold)
        {
            _state = TabState.ReturningHome;
            return;
        }

        // Return to following if P2 backs out of intimate zone
        if (p2x > intimateXThreshold)
        {
            _state = TabState.FollowingP2;
            return;
        }

        float midX = (GetHomeX() + p2x) * 0.5f;
        MoveToX(midX);
    }

    private void HandleReturningHome()
    {
        float homeX = GetHomeX();
        MoveToX(homeX);

        if (Mathf.Abs(transform.position.x - homeX) < 0.02f)
        {
            _xVelocity = 0f;
            _state     = TabState.AtWall;
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private void BeginSwoop()
    {
        SetVisible(true);
        _swoopTimer  = 0f;
        _swoopStartY = tabFixedY - verticalRiseDistance;

        transform.position   = new Vector3(GetHomeX(), _swoopStartY, tabFixedZ);
        transform.localScale = Vector3.one * 0.01f;
        _xVelocity           = 0f;

        _state = TabState.SwoopingIn;
    }

    /// Moves the tab to a target X using SmoothDamp — organic bell-curve velocity
    private void MoveToX(float targetX)
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.SmoothDamp(pos.x, targetX, ref _xVelocity, smoothTime, maxSpeed);
        pos.y = tabFixedY;
        pos.z = tabFixedZ;
        transform.position = pos;
    }

    private float GetHomeX()
    {
        if (person1Transform != null)
            return person1Transform.position.x + homeXOffset;
        return transform.position.x;
    }

    private void SetVisible(bool visible)
    {
        if (_renderer != null) _renderer.enabled = visible;
    }

    // -------------------------------------------------------------------------
    //  Scene-view gizmos — labelled vertical X threshold lines
    // -------------------------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        float h = 3f;
        float z = tabFixedZ;

        Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(entryXThreshold, 0f, z), new Vector3(entryXThreshold, h, z));
        UnityEditor.Handles.Label(new Vector3(entryXThreshold, h, z), $"Entry  {entryXThreshold}");

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(intimateXThreshold, 0f, z), new Vector3(intimateXThreshold, h, z));
        UnityEditor.Handles.Label(new Vector3(intimateXThreshold, h, z), $"Intimate  {intimateXThreshold}");

        Gizmos.color = new Color(1f, 0.85f, 0f, 0.8f);
        Gizmos.DrawLine(new Vector3(exitXThreshold, 0f, z), new Vector3(exitXThreshold, h, z));
        UnityEditor.Handles.Label(new Vector3(exitXThreshold, h, z), $"Exit  {exitXThreshold}");

        if (person1Transform != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(new Vector3(GetHomeX(), tabFixedY, tabFixedZ), 0.08f);
        }
    }
#endif
}
