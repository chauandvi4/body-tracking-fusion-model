using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

[AddComponentMenu("Visualization/Body Tracking Logger (Visualization Only)")]
public class BodyTrackingLogger : MonoBehaviour
{
    [Header("References")]
    public OVRSkeleton skeleton;   

    [Header("Logging")]
    public bool logToConsole = true;
    public int logEveryNFrames = 30;

    [Header("UDP (OSC-friendly text) - Visualization Only")]
    public bool sendUdp = true;
    public string remoteIp = "192.168.1.100"; // PC IP on same Wi-Fi
    public int remotePort = 9000;

    private UdpClient _udp;
    private int _frame;
    private Transform _hips;

    void Awake()
    {
        if (skeleton == null)
            skeleton = GetComponent<OVRSkeleton>();

        if (sendUdp)
            _udp = new UdpClient();
    }

    void OnDestroy()
    {
        _udp?.Close();
        _udp = null;
    }

    void Update()
    {
        _frame++;

        if (skeleton == null)
            return;

        // Wait until bones are available (first few frames are often empty)
        if (_hips == null)
        {
            TryFindHipsBone();
            return;
        }

        // Bone transforms are updated by the tracking system each frame.
        Vector3 p = _hips.position;      // Unity world position (meters)
        Quaternion q = _hips.rotation;   // Unity world rotation

        if (logToConsole && (_frame % logEveryNFrames == 0))
        {
            Debug.Log($"[QuestBody][VISUALIZATION] Hips pos(m)={p:F3} rot(euler)={q.eulerAngles:F1} source={PipelineSwitches.GetVisualizationSourceLabel()}");
        }

        if (sendUdp && _udp != null)
        {
            // /quest/hips x y z qx qy qz qw t
            double t = Time.timeAsDouble;
            string msg = $"/quest/hips {p.x:F6} {p.y:F6} {p.z:F6} {q.x:F6} {q.y:F6} {q.z:F6} {q.w:F6} {t:F6} visualization_only";
            byte[] data = Encoding.UTF8.GetBytes(msg);
            _udp.Send(data, data.Length, remoteIp, remotePort);
        }
    }

    private void TryFindHipsBone()
    {
        // OVRSkeleton stores a list of bones with ids.
        // We look for a bone whose id name contains "Hips" (robust across minor enum naming differences).
        var bones = skeleton.Bones;
        if (bones == null || bones.Count == 0)
            return;

        foreach (var b in bones)
        {
            if (b == null || b.Transform == null)
                continue;

            string idName = b.Id.ToString();
            if (idName.IndexOf("Hips", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _hips = b.Transform;
                Debug.Log($"[QuestBody] Found hips bone id={idName}");
                return;
            }
        }

        // If not found, print available bone ids once to help debugging.
        if (_frame % 120 == 0)
        {
            string all = string.Join(", ", bones.Where(x => x != null).Select(x => x.Id.ToString()));
            Debug.LogWarning($"[QuestBody] Hips not found yet. Bones available: {all}");
        }
    }
}
