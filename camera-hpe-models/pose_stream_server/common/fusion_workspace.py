from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Mapping, Optional

logger = logging.getLogger(__name__)

@dataclass
class PoseSnapshot:
    timestamp: float
    landmarks: Mapping[str, Mapping[str, float]]


class FusionWorkspace:
    """Central point for multiple pose sources.

    This module holds the shared state for upper-body data (e.g. from
    Unity/Meta Quest) and lower-body keypoints (MediaPipe, SynthPose,
    etc.).
    """

    def __init__(self) -> None:
        self.latest_upper_body: Optional[Mapping[str, object]] = None
        self.latest_lower_body: Optional[PoseSnapshot] = None

    # Unity / OSC callbacks
    def handle_quest_packet(self, packet: Mapping[str, object], addr) -> None:
        self.latest_upper_body = packet
        timestamp = packet.get("timestamp")
        joint_count = len(packet.get("joints", []) or [])
        logger.info(
            "Unity OSC packet from %s @ %.3f with %d joints", addr, timestamp, joint_count
        )
        self._log_workspace_state()

    # Pose estimation model callbacks
    def update_lower_body(self, snapshot: PoseSnapshot) -> None:
        self.latest_lower_body = snapshot

        # Keep a light throttled logger so we can monitor incoming data without
        # flooding the console when multiple sources are active.
        if hasattr(self, "_mp_frame_idx"):
            self._mp_frame_idx += 1
        else:
            self._mp_frame_idx = 1

        if self._mp_frame_idx % 60 == 0:
            if snapshot.landmarks:
                logger.info(
                    "Pose snapshot from camera @ %.3f with %d keypoints (logging every 60 frames):",
                    snapshot.timestamp,
                    len(snapshot.landmarks),
                )
                for idx, (name, lm) in enumerate(snapshot.landmarks.items(), start=1):
                    x = lm.get("x", 0.0)
                    y = lm.get("y", 0.0)
                    z = lm.get("z", 0.0)
                    visibility = lm.get("visibility", 0.0)
                    logger.info(
                        "Keypoint %d - %s: (%.3f, %.3f, %.3f). Confidence score: %.4f",
                        idx,
                        name,
                        x,
                        y,
                        z,
                        visibility,
                    )
            else:
                logger.info(
                    "Pose snapshot from camera @ %.3f (no landmarks visible)",
                    snapshot.timestamp,
                )

        self._log_workspace_state()

    def _log_workspace_state(self) -> None:
        """Log when both streams are live to highlight the fusion."""

        if not self.latest_upper_body or not self.latest_lower_body:
            return

        try:
            quest_ts = float(self.latest_upper_body.get("timestamp", 0.0))
        except Exception:
            quest_ts = 0.0
        pose_estimation_by_camera_ts = self.latest_lower_body.timestamp
        delta = pose_estimation_by_camera_ts - quest_ts

        logger.info(
            "Fusion workspace ready (Quest ts=%.3f, Camera source ts=%.3f, Δ=%.3fs) — this is the hook for blending the two bodies.",
            quest_ts,
            pose_estimation_by_camera_ts,
            delta,
        )
