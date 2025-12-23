"""MediaPipe captures loop that shares a workspace with Unity OSC packets.
"""

import argparse
import asyncio
import contextlib
import logging
import time
from typing import Dict, Mapping, MutableMapping, Optional
import cv2
import mediapipe as mp
import sys
from pathlib import Path

current_file = Path(__file__).resolve()
project_root = current_file.parents[2]
sys.path.insert(0, str(project_root))
# Shared fusion workspace so this process can publish pose snapshots
from pose_stream_server.common.fusion_workspace import FusionWorkspace, PoseSnapshot
# Helper to starts and listens to OSC
from pose_stream_server.osc_pose_receiver.osc_pose_receiver import run_server as start_osc_server

logger = logging.getLogger(__name__)

mp_pose = mp.solutions.pose
pose = mp_pose.Pose(
    static_image_mode=False,
    model_complexity=1,
    enable_segmentation=False,
    min_detection_confidence=0.5,
    min_tracking_confidence=0.5,
)

# Previously LOWER_BODY_LANDMARKS; now use all pose landmarks.
ALL_LANDMARKS = list(mp_pose.PoseLandmark)

# MediaPipe helpers
def _landmark_dict(landmark) -> Dict[str, float]:
    return {
        "x": float(landmark.x),
        "y": float(landmark.y),
        "z": float(landmark.z),
        "visibility": float(getattr(landmark, "visibility", 0.0)),
    }


def extract_pose_data(results) -> Optional[PoseSnapshot]:
    if not (results.pose_landmarks and results.pose_world_landmarks):
        return None

    world_landmarks = results.pose_world_landmarks.landmark
    output: MutableMapping[str, Mapping[str, float]] = {}

    for landmark_enum in ALL_LANDMARKS:
        idx = landmark_enum.value
        output[landmark_enum.name.lower()] = _landmark_dict(world_landmarks[idx])

    return PoseSnapshot(timestamp=time.time(), landmarks=dict(output))


async def mediapipe_loop(camera_index: int, workspace: FusionWorkspace) -> None:
    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        logger.error("Cannot open camera index %s", camera_index)
        return

    try:
        while True:
            success, image = cap.read()
            if not success:
                logger.warning("Empty frame, retrying...")
                await asyncio.sleep(0.1)
                continue

            image.flags.writeable = False
            image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            results = pose.process(image)

            snapshot = extract_pose_data(results)
            if snapshot:
                workspace.update_lower_body(snapshot)

            image.flags.writeable = True
            image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
            mp.solutions.drawing_utils.draw_landmarks(
                image,
                results.pose_landmarks,
                mp_pose.POSE_CONNECTIONS,
                landmark_drawing_spec=mp.solutions.drawing_styles.get_default_pose_landmarks_style(),
            )
            cv2.imshow("MediaPipe Pose", cv2.flip(image, 1))
            if cv2.waitKey(5) & 0xFF == 27:
                logger.info("ESC pressed, stopping MediaPipe loop")
                break

            await asyncio.sleep(0)
    finally:
        cap.release()
        cv2.destroyAllWindows()


# CLI entrypoint
def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera-index", type=int, default=0, help="OpenCV camera index (default: 0)")
    parser.add_argument("--osc-host", default="0.0.0.0", help="Interface for Unity OSC packets (default: 0.0.0.0)")
    parser.add_argument("--osc-port", type=int, default=9000, help="UDP port for Unity OSC packets (default: 9000)")
    parser.add_argument("--log-level", default="INFO", help="Logging level (default: INFO)")
    return parser.parse_args()


async def async_main(args: argparse.Namespace) -> None:
    workspace = FusionWorkspace()
    osc_task = asyncio.create_task(
        start_osc_server(args.osc_host, args.osc_port, workspace.handle_quest_packet)
    )

    try:
        await mediapipe_loop(args.camera_index, workspace)
    finally:
        osc_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await osc_task


def main() -> None:
    args = parse_args()
    logging.basicConfig(level=getattr(logging, args.log_level.upper(), logging.INFO))

    try:
        asyncio.run(async_main(args))
    except KeyboardInterrupt:
        logger.info("Shutting down MediaPipe + OSC fusion workspace")


if __name__ == "__main__":
    main()
