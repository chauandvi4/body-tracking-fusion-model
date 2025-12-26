# pose-estimation-by-camera-and-vr-headset

## Getting started

This is a hybrid framework for human body pose estimation using **MediaPipe**/**SynthPose MMPose** with external cameras, synchronized with **Meta Quest** tracking data for VR applications.

## Installation and run camera modules
 Python 3.10 is recommended

1. **Setup Mediapipe env:**

```bash
cd camera-hpe-models\environments
.\setup-mediapipe.bat
.\run-mediapipe.bat
```

2. **Setup Synthpose env:**

``` bash
cd camera-hpe-models\environments
.\setup-synthpose.bat
.\run-synthpose.bat
```

## Streaming Quest body data into the Python UDP receiver

1. In Unity, add the **QuestBodyUdpSender** component (found under `Assets/QuestBodyUdpSender.cs`) to  `OVRCameraRig` or another GameObject in the scene.
   - Set **Remote Ip** to the IPv4 address of the PC running the Python process (same Wi‑Fi/LAN).
   - Optionally point **Hmd Transform** to the headset camera; otherwise the main camera is used.
2. Deploy/run the Unity scene on the headset so the component can read the `OVRBody`/`OVRSkeleton` poses.
3. On the PC, start the UDP listener to view the incoming packets:

```bash
cd camera-hpe-models\pose_stream_server
python -m pose_stream_server.udp_pose_receiver --host 0.0.0.0 --port 9000 --verbose
```
