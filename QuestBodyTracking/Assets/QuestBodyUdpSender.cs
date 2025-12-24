using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class QuestBodyUdpSender : MonoBehaviour
{
    [Header("Networking")]
    [Tooltip("PC IP address running the Python UDP receiver (same LAN/Wi-Fi).")]
    public string remoteIp = "10.20.21.11";

    [Tooltip("UDP port on the PC to send to.")]
    public int remotePort = 5005;

    [Tooltip("Send rate in Hz (e.g., 30).")]
    public int sendHz = 30;

    [Header("Body Tracking")]
    [Tooltip("Reference to OVRBody component (on your rig).")]
    public OVRBody ovrBody;

    // Internal
    private UdpClient _udp;
    private IPEndPoint _remoteEndPoint;
    private float _sendInterval;
    private float _nextSendTime;
    private uint _sequence;

    // Packet constants
    private const uint MAGIC = 0x42544A53; // 'BTJS' arbitrary marker
    private const uint VERSION = 1;

    void Awake()
    {
        if (ovrBody == null)
        {
            ovrBody = FindObjectOfType<OVRBody>();
        }

        _sendInterval = (sendHz <= 0) ? 0.0333f : (1.0f / sendHz);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        _udp = new UdpClient();
        _udp.Client.SendTimeout = 5; // ms; keep small
        _sequence = 0;
        _nextSendTime = Time.unscaledTime;
    }

    void OnDestroy()
    {
        _udp?.Close();
        _udp = null;
    }

    void Update()
    {
        if (ovrBody == null)
            return;

        // Throttle to sendHz
        if (Time.unscaledTime < _nextSendTime)
            return;

        _nextSendTime = Time.unscaledTime + _sendInterval;

        // Ensure body tracking is active and has data
        // OVRBody provides pose in tracking space; data availability depends on device/support.
        if (!TryIsBodyTracked(ovrBody))
            return;

        // Read joints.
        // OVRBody exposes a BodyState; exact API can differ by SDK version.
        if (!TryGetBodyJoints(out List<JointSample> joints))
            return;

        byte[] packet = BuildPacket(joints);
        try
        {
            _udp.Send(packet, packet.Length, _remoteEndPoint);
        }
        catch (Exception)
        {
            // TODO: Send exceptions (Wi-Fi hiccups etc.).
        }
    }

    // A small container for one joint sample
    private struct JointSample
    {
        public Vector3 pos;
        public Quaternion rot;
    }

    private bool TryGetBodyJoints(out List<JointSample> joints)
    {
        joints = null;

        // Use TryGetComponent to avoid allocations
        if (!ovrBody.TryGetComponent<OVRSkeleton>(out OVRSkeleton skel))
            skel = ovrBody.GetComponentInChildren<OVRSkeleton>(true);

        if (skel == null)
            return false;

        // Bones list may not be ready immediately.
        var bones = skel.Bones;
        if (bones == null || bones.Count == 0)
            return false;

        joints = new List<JointSample>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
        {
            Transform t = bones[i].Transform;
            joints.Add(new JointSample
            {
                pos = t.position,    
                rot = t.rotation,
            });
        }

        return true;
    }

    private byte[] BuildPacket(List<JointSample> joints)
    {
        // timestamp in Unix microseconds
        long tsUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;

        // Compute packet size:
        // header: magic(4) + version(4) + seq(4) + ts(8) + jointCount(2) + padding(2) = 24 bytes
        // each joint: pos(12) + rot(16) + conf(4) = 32 bytes
        int jointCount = joints.Count;
        int headerSize = 24;
        int jointSize = 32;
        int totalSize = headerSize + (jointCount * jointSize);

        byte[] buffer = new byte[totalSize];
        int offset = 0;

        void WriteUInt32(uint v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buffer, offset, 4);
            offset += 4;
        }
        void WriteInt64(long v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buffer, offset, 8);
            offset += 8;
        }
        void WriteUInt16(ushort v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buffer, offset, 2);
            offset += 2;
        }
        void WriteFloat(float v)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buffer, offset, 4);
            offset += 4;
        }

        // Header
        WriteUInt32(MAGIC);
        WriteUInt32(VERSION);
        WriteUInt32(_sequence++);
        WriteInt64(tsUs);
        WriteUInt16((ushort)jointCount);
        WriteUInt16(0); // padding for alignment

        // Body
        for (int i = 0; i < jointCount; i++)
        {
            var j = joints[i];
            WriteFloat(j.pos.x); WriteFloat(j.pos.y); WriteFloat(j.pos.z);
            WriteFloat(j.rot.x); WriteFloat(j.rot.y); WriteFloat(j.rot.z); WriteFloat(j.rot.w);
        }

        return buffer;
    }

    /// <summary>
    /// Checks if the OVRBody is currently tracked by verifying skeleton validity and confidence.
    /// </summary>
    private static bool TryIsBodyTracked(OVRBody body)
    {
        if (body == null)
            return false;

        OVRSkeleton skeleton;
        if (!body.TryGetComponent<OVRSkeleton>(out skeleton))
            skeleton = body.GetComponentInChildren<OVRSkeleton>(true);

        if (skeleton == null)
            return false;

        // Use IsDataValid and IsDataHighConfidence as tracking indicators
        return skeleton.IsDataValid && skeleton.IsDataHighConfidence;
    }
}
