using UnityEngine;
using System;
using System.Net.Sockets;
using System.Text;


// =============================================================================
//  SHARED ENUMS & DATA TYPES
//  Used by the Receiver and both mapper scripts.
// =============================================================================

public enum Brekel_joint_name_v3
{
    waist = 0,
    spine,
    chest,
    neck,
    head,
    head_tip,

    upperLeg_L,
    lowerLeg_L,
    foot_L,
    toes_L,

    upperLeg_R,
    lowerLeg_R,
    foot_R,
    toes_R,

    collar_L,
    upperArm_L,
    foreArm_L,
    hand_L,
    fingersTip_L,

    collar_R,
    upperArm_R,
    foreArm_R,
    hand_R,
    fingersTip_R,

    numJoints
}

public enum Brekel_blendshape_name
{
    browDown_L = 0,
    browDown_R,
    browInnerUp_L,
    browInnerUp_R,
    browOuterUp_L,
    browOuterUp_R,
    cheekPuff_L,
    cheekPuff_R,
    cheekSquint_L,
    cheekSquint_R,
    eyeBlink_L,
    eyeBlink_R,
    eyeLookDown_L,
    eyeLookDown_R,
    eyeLookIn_L,
    eyeLookIn_R,
    eyeLookOut_L,
    eyeLookOut_R,
    eyeLookUp_L,
    eyeLookUp_R,
    eyeSquint_L,
    eyeSquint_R,
    eyeWide_L,
    eyeWide_R,
    jawForward,
    jawLeft,
    jawOpen,
    jawRight,
    mouthClose,
    mouthDimple_L,
    mouthDimple_R,
    mouthFrown_L,
    mouthFrown_R,
    mouthFunnel,
    mouthLeft,
    mouthLowerDown_L,
    mouthLowerDown_R,
    mouthPress_L,
    mouthPress_R,
    mouthPucker,
    mouthRight,
    mouthRollLower,
    mouthRollUpper,
    mouthShrugLower,
    mouthShrugUpper,
    mouthSmile_L,
    mouthSmile_R,
    mouthStretch_L,
    mouthStretch_R,
    mouthUpperUp_L,
    mouthUpperUp_R,
    noseSneer_L,
    noseSneer_R,
    numBlendshapes
}

/// <summary>Human-readable joint name strings indexed by Brekel_joint_name_v3.</summary>
public static class BrekelJointNames
{
    public static readonly string[] Names = new string[]
    {
        "waist", "spine", "chest", "neck", "head", "head_tip",
        "upperLeg_L", "lowerLeg_L", "foot_L", "toes_L",
        "upperLeg_R", "lowerLeg_R", "foot_R", "toes_R",
        "collar_L", "upperArm_L", "foreArm_L", "hand_L", "fingersTip_L",
        "collar_R", "upperArm_R", "foreArm_R", "hand_R", "fingersTip_R",
    };
}

/// <summary>Per-joint data for one skeleton frame.</summary>
[Serializable]
public class BrekelJoint
{
    public float      confidence;
    public Vector3    position;
    public Quaternion rotation;
}

/// <summary>One full body frame received from Brekel Body v3.</summary>
[Serializable]
public class BrekelBodyFrame
{
    public const int NumJoints      = (int)Brekel_joint_name_v3.numJoints;   // 25
    public const int NumBlendshapes = (int)Brekel_blendshape_name.numBlendshapes; // 52

    public float          timestamp;
    public BrekelJoint[]  joints     = new BrekelJoint[NumJoints];
    public float[]        blendshapes = new float[NumBlendshapes];

    public BrekelBodyFrame()
    {
        for (int i = 0; i < NumJoints; i++)
            joints[i] = new BrekelJoint();
    }
}


// =============================================================================
//  Brekel_Body_v3_Receiver
//
//  Owns the TCP connection to Brekel Body v3.  Parses incoming packets into
//  a double-buffered array of BrekelBodyFrame objects that mapper scripts
//  read via GetBody().
//
//  Add this component once per scene.  Both Brekel_Body_v3_TCP and
//  Brekel_Body_v3_HumanoidMapper reference it as a public field.
// =============================================================================
public class Brekel_Body_v3_Receiver : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------
    [Header("Network Settings")]
    [Tooltip("Hostname or IP where Brekel Body v3 is running (localhost = same machine)")]
    public string host = "localhost";
    [Tooltip("TCP port Brekel Body v3 is streaming on (default 8844)")]
    public int    port = 8844;

    // -------------------------------------------------------------------------
    //  Public read-only state
    // -------------------------------------------------------------------------
    public bool IsConnected => _isConnected;

    // -------------------------------------------------------------------------
    //  Internal
    // -------------------------------------------------------------------------
    public  const int  MaxBodies      = 10;
    private const int  ReadBufferSize = 65535;

    private TcpClient _client;
    private byte[]    _readBuffer  = new byte[ReadBufferSize];
    private bool      _isConnected;

    // Double-buffer: network thread writes _back, main thread reads _front
    private BrekelBodyFrame[] _front;
    private BrekelBodyFrame[] _back;
    private readonly object   _swapLock    = new object();
    private bool              _newDataReady;



    // =========================================================================
    //  Unity lifecycle
    // =========================================================================
    void Start()
    {
        _front = AllocFrames();
        _back  = AllocFrames();
        Connect();
    }

    void OnDisable()
    {
        Disconnect();
    }

    void Update()
    {
        if (!_isConnected)
            return;

        if (_newDataReady)
        {
            lock (_swapLock)
            {
                BrekelBodyFrame[] tmp = _front;
                _front        = _back;
                _back         = tmp;
                _newDataReady = false;
            }
        }

    }


    // =========================================================================
    //  Public API for mappers
    // =========================================================================

    /// <summary>
    /// Returns the latest received body frame for the given body ID,
    /// or null if the ID is out of range.
    /// </summary>
    public BrekelBodyFrame GetBody(int bodyID)
    {
        if (bodyID < 0 || bodyID >= MaxBodies)
            return null;
        return _front[bodyID];
    }


    // =========================================================================
    //  Networking
    // =========================================================================
    private bool Connect()
    {
        try
        {
            _client = new TcpClient(host, port);
            _client.GetStream().BeginRead(_readBuffer, 0, ReadBufferSize, OnFrameReceived, null);
            _isConnected = true;
            Debug.Log($"[BrekelReceiver] Connected to Brekel Body v3 at {host}:{port}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BrekelReceiver] Could not connect to {host}:{port} — {ex.Message}");
            return false;
        }
    }

    private void Disconnect()
    {
        _isConnected = false;
        _client?.Close();
        Debug.Log("[BrekelReceiver] Disconnected from Brekel Body v3");
    }

    private void OnFrameReceived(IAsyncResult ar)
    {
        try
        {
            int bytesRead = _client.GetStream().EndRead(ar);
            if (bytesRead < 1)
            {
                Debug.Log("[BrekelReceiver] Stream closed by server.");
                _isConnected = false;
                return;
            }
            ParsePacket(bytesRead);
        }
        catch
        {
            // Drop broken packets silently
        }

        if (_isConnected)
            _client.GetStream().BeginRead(_readBuffer, 0, ReadBufferSize, OnFrameReceived, null);
    }

    private void ParsePacket(int bytesRead)
    {
        string header = Encoding.UTF8.GetString(_readBuffer, 0,            6);
        string footer = Encoding.UTF8.GetString(_readBuffer, bytesRead - 6, 6);
        if (header != "BRKL_S" || footer != "BRKL_E")
        {
            Debug.LogWarning("[BrekelReceiver] Invalid packet framing — discarding.");
            return;
        }

        int index              = 8; // skip 6-byte header + 2-byte encoded size
        int numJoints          = BitConverter.ToInt32(_readBuffer, index); index += 4;
        int numBodies          = BitConverter.ToInt32(_readBuffer, index); index += 4;
        /*int numFaceExpressions =*/ BitConverter.ToInt32(_readBuffer, index); index += 4;

        if (numJoints != BrekelBodyFrame.NumJoints)
        {
            Debug.LogWarning($"[BrekelReceiver] Expected {BrekelBodyFrame.NumJoints} joints, got {numJoints} — discarding.");
            return;
        }

        numBodies = Mathf.Min(numBodies, MaxBodies);

        for (int b = 0; b < numBodies; b++)
        {
            BrekelBodyFrame frame = _back[b];
            frame.timestamp = BitConverter.ToSingle(_readBuffer, index); index += 4;

            for (int j = 0; j < numJoints; j++)
            {
                frame.joints[j].confidence = BitConverter.ToSingle(_readBuffer, index); index += 4;

                float px = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float py = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float pz = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float rx = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float ry = BitConverter.ToSingle(_readBuffer, index); index += 4;
                float rz = BitConverter.ToSingle(_readBuffer, index); index += 4;

                frame.joints[j].position = ConvertPosition(new Vector3(px, py, pz));
                frame.joints[j].rotation = ConvertRotation(new Vector3(rx, ry, rz));
            }

            int exprCount = BitConverter.ToInt32(_readBuffer, index); index += 4;
            if (exprCount > 0)
            {
                index += 4; // skip quality float
                for (int e = 0; e < exprCount; e++)
                {
                    float w = BitConverter.ToSingle(_readBuffer, index); index += 4;
                    if (e < BrekelBodyFrame.NumBlendshapes)
                        frame.blendshapes[e] = w;
                }
            }
        }

        lock (_swapLock)
            _newDataReady = true;
    }


    // =========================================================================
    //  Coordinate system conversion
    //  Brekel Body v3: right-handed, Y-up (OpenGL).  Unity: left-handed, Y-up.
    //  Position: negate X.
    //  Rotation: negate Y and Z contributions (handedness flip).
    // =========================================================================
    private static Vector3 ConvertPosition(Vector3 p)
    {
        p.x *= -1f;
        return p;
    }

    private static Quaternion ConvertRotation(Vector3 euler)
    {
        Quaternion qx = Quaternion.AngleAxis(euler.x, Vector3.right);
        Quaternion qy = Quaternion.AngleAxis(euler.y, Vector3.down);
        Quaternion qz = Quaternion.AngleAxis(euler.z, Vector3.back);
        return qz * qy * qx;
    }


    // =========================================================================
    //  Helpers
    // =========================================================================
    private static BrekelBodyFrame[] AllocFrames()
    {
        var arr = new BrekelBodyFrame[MaxBodies];
        for (int i = 0; i < MaxBodies; i++)
            arr[i] = new BrekelBodyFrame();
        return arr;
    }
}
