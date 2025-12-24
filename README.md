# pose-estimation-by-camera-and-vr-headset

## Getting started

This is a hybrid framework for human body pose estimation using **MediaPipe**/**SynthPose MMPose** with external cameras, synchronized with **Meta Quest** tracking data for VR applications.

## Installation and run camera modules
 Python 3.10 is recommended

1. **Setup Mediapipe env:**

```
cd environments
.\setup-mediapipe.bat
.\run-mediapipe.bat
```

2. **Setup Synthpose env:**

```
cd environments
setup-synthpose.bat
run-synthpose.bat
```

## Streaming Quest body data into the Python UDP receiver

1. In Unity, add the **QuestBodyUdpSender** component (found under `Assets/QuestBodyUdpSender.cs`) to your `OVRCameraRig` or another GameObject in the scene.
   - Set **Remote Ip** to the IPv4 address of the PC running the Python process (same Wi‑Fi/LAN).
   - Leave **Remote Port** at `9000` unless you change the Python `--port` argument.
   - Optionally point **Hmd Transform** to the headset camera; otherwise the main camera is used.
2. Deploy/run the Unity scene on the headset so the component can read the `OVRBody`/`OVRSkeleton` poses.
3. On the PC, start the UDP listener to view the incoming packets:

```bash
cd pose_stream_server
python -m pose_stream_server.osc_pose_receiver --host 0.0.0.0 --port 9000 --verbose
```

The sender packs each frame as a UTF‑8 JSON payload in a single UDP datagram (timestamp + HMD pose + all body joints). The Python receiver reads this JSON directly and logs a sample of the joint positions.
