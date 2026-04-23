using UnityEngine;

public class DigitalTabController : MonoBehaviour
{
    // ── References ───────────────────────────────────────────────────────────
    [Header("References")]
    public PersonAnchor person2Anchor;
    public Transform    swoopOrigin;   // optional – sets swoop start Y

    // ── Tab Home Position ────────────────────────────────────────────────────
    [Header("Tab Home Position")]
    public float homeX = 0.03f;
    public float tabY  = 1.5f;
    public float wallZ = 2.32f;

    // ── Swoop Animation ──────────────────────────────────────────────────────
    [Header("Swoop Animation")]
    public float swoopDuration = 0.4f;

    // ── Proximity Thresholds (raw Z coordinates of each zone boundary) ───────
    [Header("Proximity — raw Z position of each boundary")]
    [Tooltip("Tab starts following when anchor.Z reaches this value")]
    public float entryDist    = 1.7f;

    [Tooltip("(reserved for intimate zone — not active yet)")]
    public float intimateDist = 0.5f;

    [Tooltip("(reserved for exit zone — not active yet)")]
    public float exitDist     = 2.2f;

    // ── Following Offset ─────────────────────────────────────────────────────
    [Header("Following Offset")]
    [Tooltip("Z offset applied only while the tab is following/midpointing the anchor. " +
             "Positive = push tab further from sensor. Negative = pull tab closer.")]
    public float followZOffset = 0f;

    // ── Following Feel ───────────────────────────────────────────────────────
    [Header("Following Feel")]
    [Tooltip("How quickly the tab snaps to the anchor's X (seconds). 0.1 ≈ arrives in ~0.3 s)")]
    public float followSmoothTime = 0.10f;

    [Tooltip("Maximum tab speed while following (units/sec)")]
    public float followMaxSpeed   = 20f;

    // ── Private ──────────────────────────────────────────────────────────────
    private enum State { Hidden, SwoopingIn, AtWall, Following, Midpoint, Returning }

    private State    _state    = State.Hidden;
    private Renderer _rend;
    private Vector3  _origScale;
    private float    _xVel;
    private float    _swoopT;
    private Vector3  _swoopFrom;

    // Exit crossing counter — first cross ignored, second cross → permanent home
    private int   _exitCrossings  = 0;
    private bool  _permanentHome  = false;
    private float _prevAnchorZ    = float.MinValue;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Start()
    {
        _rend         = GetComponent<Renderer>();
        _origScale    = transform.localScale;
        _rend.enabled = false;
        _state        = State.Hidden;
    }

    void Update()
    {
        switch (_state)
        {
            case State.Hidden:    DoHidden();    break;
            case State.SwoopingIn:DoSwoop();     break;
            case State.AtWall:    DoAtWall();    break;
            case State.Following: DoFollowing(); break;
            case State.Midpoint:  DoMidpoint();  break;
            case State.Returning: DoReturning(); break;
        }
    }

    // ── State handlers ────────────────────────────────────────────────────────

    void DoHidden()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            StartSwoop();
    }

    void DoSwoop()
    {
        _swoopT += Time.deltaTime / swoopDuration;
        float t = Mathf.Clamp01(_swoopT);
        float e = Mathf.SmoothStep(0f, 1f, t);

        Vector3 dest = new Vector3(homeX, tabY, wallZ);
        transform.position   = Vector3.Lerp(_swoopFrom, dest, e);
        transform.localScale = Vector3.Lerp(Vector3.one * 0.01f, _origScale, e);

        if (t >= 1f)
        {
            transform.position   = dest;
            transform.localScale = _origScale;
            _state = State.AtWall;
        }
    }

    void DoAtWall()
    {
        LockPosition();

        // Permanently home after second exit crossing — ignore all proximity
        if (_permanentHome) return;

        if (person2Anchor == null || !person2Anchor.IsTracked) return;

        if (person2Anchor.transform.position.z >= entryDist)
            _state = State.Following;
    }

    void DoFollowing()
    {
        // If tracking lost, snap back to AtWall
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = State.AtWall;
            return;
        }

        // Enter intimate zone → go to midpoint
        if (person2Anchor.transform.position.z >= intimateDist)
        {
            _state = State.Midpoint;
            return;
        }

        // Smoothly move to match anchor's Z (plus inspector offset); X and Y are locked
        float targetZ = person2Anchor.transform.position.z + followZOffset;
        Vector3 pos   = transform.position;
        pos.x = homeX;
        pos.y = tabY;
        pos.z = Mathf.SmoothDamp(pos.z, targetZ, ref _xVel, followSmoothTime, followMaxSpeed);
        transform.position = pos;
    }

    void DoMidpoint()
    {
        if (person2Anchor == null || !person2Anchor.IsTracked)
        {
            _state = State.AtWall;
            return;
        }

        float anchorZ = person2Anchor.transform.position.z;

        // Detect each time the anchor crosses the exit threshold (either direction)
        if (_prevAnchorZ != float.MinValue)
        {
            bool crossed = (_prevAnchorZ < exitDist) != (anchorZ < exitDist);
            if (crossed)
            {
                _exitCrossings++;
                if (_exitCrossings >= 2)
                {
                    // Second crossing → permanent home, never follow again
                    _permanentHome = true;
                    _prevAnchorZ   = anchorZ;
                    _state = State.Returning;
                    return;
                }
            }
        }
        _prevAnchorZ = anchorZ;

        // Midpoint in Z between anchor and swoop origin (plus inspector offset)
        float originZ = swoopOrigin != null ? swoopOrigin.transform.position.z : wallZ;
        float targetZ = (anchorZ + originZ) * 0.5f + followZOffset;

        Vector3 pos = transform.position;
        pos.x = homeX;
        pos.y = tabY;
        pos.z = Mathf.SmoothDamp(pos.z, targetZ, ref _xVel, followSmoothTime, followMaxSpeed);
        transform.position = pos;
    }

    void DoReturning()
    {
        // Smoothly bring tab back to home position (wallZ)
        Vector3 pos = transform.position;
        pos.x = homeX;
        pos.y = tabY;
        pos.z = Mathf.SmoothDamp(pos.z, wallZ, ref _xVel, followSmoothTime, followMaxSpeed);
        transform.position = pos;

        // Once close enough, settle at AtWall
        if (Mathf.Abs(pos.z - wallZ) < 0.02f)
        {
            transform.position = new Vector3(homeX, tabY, wallZ);
            _xVel  = 0f;
            _state = State.AtWall;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void StartSwoop()
    {
        _rend.enabled  = true;
        _swoopT        = 0f;
        _xVel          = 0f;
        _exitCrossings = 0;
        _permanentHome = false;
        _prevAnchorZ   = float.MinValue;
        float startY   = swoopOrigin != null ? swoopOrigin.position.y : tabY - 0.5f;
        _swoopFrom     = new Vector3(homeX, startY, wallZ);
        transform.position   = _swoopFrom;
        transform.localScale = Vector3.one * 0.01f;
        _state = State.SwoopingIn;
    }

    /// Lock tab to home position.
    void LockPosition()
    {
        transform.position = new Vector3(homeX, tabY, wallZ);
    }

    // ── Diagnostics (remove after confirming working) ─────────────────────────
    private float _diagTimer;
    void LateUpdate()
    {
        // Log every 0.5 s so console isn't flooded
        _diagTimer += Time.deltaTime;
        if (_diagTimer < 0.5f) return;
        _diagTimer = 0f;

        string anchorInfo;
        if (person2Anchor == null)
            anchorInfo = "ANCHOR IS NULL — drag InvisibleAnchor_P2 into Person2Anchor field!";
        else
            anchorInfo = $"tracked={person2Anchor.IsTracked} | anchorX={person2Anchor.transform.position.x:F3} | anchorZ={person2Anchor.transform.position.z:F3}";

        Debug.Log($"[Tab] state={_state} | tabZ={transform.position.z:F3} | {anchorInfo} | entryDist={entryDist}");
    }

    // ── Scene-view gizmos ─────────────────────────────────────────────────────
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        float cx = homeX;
        float hw = 4f;

        // Draw vertical marker lines at each raw Z boundary
        DrawZLine(cx, hw, entryDist,    new Color(0.2f, 0.9f, 0.2f), $"Entry  Z={entryDist}");
        DrawZLine(cx, hw, intimateDist, new Color(0.9f, 0.2f, 0.2f), $"Intimate  Z={intimateDist}");
        DrawZLine(cx, hw, exitDist,     new Color(1f,   0.85f, 0f),  $"Exit  Z={exitDist}");
        DrawZLine(cx, hw, wallZ,        Color.cyan,                  $"Wall  Z={wallZ}");

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(new Vector3(homeX, tabY, wallZ), 0.08f);
    }

    static void DrawZLine(float cx, float hw, float z, Color col, string label)
    {
        Gizmos.color = col;
        Gizmos.DrawLine(new Vector3(cx - hw, 0f, z), new Vector3(cx + hw, 0f, z));
        Gizmos.DrawLine(new Vector3(cx,      0f, z), new Vector3(cx,      2f, z));
        UnityEditor.Handles.Label(new Vector3(cx + hw, 0.1f, z), label);
    }
#endif
}
