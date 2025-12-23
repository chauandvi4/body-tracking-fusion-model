"""Synthpose captures loop that shares a workspace with Unity OSC packets.
"""
import os
import cv2
import numpy as np
import torch
import logging
import time
import argparse
import asyncio
import contextlib
import sys
from pathlib import Path
from mmpose.apis import init_model, inference_topdown
from mmpose.visualization import PoseLocalVisualizer
from ultralytics import YOLO
import urllib.request
from huggingface_hub import snapshot_download

current_file = Path(__file__).resolve()
project_root = current_file.parents[2]
sys.path.insert(0, str(project_root))
# Shared fusion workspace so this process can publish pose snapshots
from pose_stream_server.common.fusion_workspace import FusionWorkspace, PoseSnapshot
# OSC receiver helper
from pose_stream_server.osc_pose_receiver.osc_pose_receiver import run_server as start_osc_server

logger = logging.getLogger(__name__)
logging.basicConfig(level=logging.INFO)

# Create or reuse a workspace for fusing pose sources
workspace = FusionWorkspace()

# Choose device
def choose_device() -> str:
    if torch.cuda.is_available():
        return "cuda"
    if hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
        return "mps"
    return "cpu"

# YOLOv8 local model download
YOLO_DIR = "./models_local/yolov8n"
os.makedirs(YOLO_DIR, exist_ok=True)

yolo_local_path = os.path.join(YOLO_DIR, "yolov8n.pt")

def setup_models():
    """Download / initialize YOLO and SynthPose models and return them.

    Returns: (yolo_model, yolo_device, synth_model, visualizer)
    """
    if not os.path.exists(yolo_local_path):
        logger.info("Downloading YOLOv8n model to: %s", yolo_local_path)
        url = "https://github.com/ultralytics/assets/releases/download/v0.0.0/yolov8n.pt"
        urllib.request.urlretrieve(url, yolo_local_path)
    else:
        logger.info("YOLOv8n model found locally.")

    yolo_device = choose_device()
    yolo_model = YOLO(yolo_local_path)
    if yolo_device != "cpu":
        yolo_model.to(yolo_device)
    logger.info("Using YOLO device: %s", yolo_device)

    # SynthPose local model download and load
    MODEL_DIR = "./models_local/synthpose_hrnet"
    os.makedirs(MODEL_DIR, exist_ok=True)

    repo_dir = snapshot_download(
        repo_id="stanfordmimi/synthpose-hrnet-48-mmpose",
        local_dir=MODEL_DIR,
    )

    config_path = os.path.join(repo_dir, "td-hm_hrnet-w48_dark-8xb32-210e_synthpose_inference.py")
    checkpoint_path = os.path.join(repo_dir, "hrnet-w48_dark.pth")

    device = choose_device()
    synth_model = init_model(config_path, checkpoint_path, device=device)
    synth_model.eval()
    logger.info("SynthPose HRNet48 initialized on %s", device)

    visualizer = PoseLocalVisualizer()
    visualizer.set_dataset_meta(synth_model.dataset_meta)

    return yolo_model, yolo_device, synth_model, visualizer


# Webcam Loop
def process_frame(frame, yolo_model, yolo_device, synth_model, visualizer, workspace, window_name, conf_thresh=0.6):
    vis_frame = frame.copy()

    # YOLO person detection
    yolo_results = yolo_model(frame, device=yolo_device, verbose=False)[0]

    person_bboxes = []
    for box in yolo_results.boxes:
        cls = int(box.cls[0])
        conf = float(box.conf[0])
        if cls == 0 and conf >= conf_thresh:  # person + high confidence
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            person_bboxes.append([x1, y1, x2, y2])

    if len(person_bboxes) == 0:
        logger.debug("No person detected in frame")
        cv2.imshow(window_name, vis_frame)
        return True

    # Draw all YOLO boxes
    for (x1, y1, x2, y2) in person_bboxes:
        cv2.rectangle(vis_frame, (int(x1), int(y1)), (int(x2), int(y2)), (0, 255, 255), 2)

    # Run SynthPose for all persons
    pose_samples = inference_topdown(
        synth_model,
        frame,
        bboxes=person_bboxes,
        bbox_format='xyxy'
    )

    if len(pose_samples) == 0:
        logger.debug("Pose not detected even though YOLO found persons.")
        cv2.imshow(window_name, vis_frame)
        return True

    # inference_topdown returns a list of PoseDataSample (one per image),
    # since we're giving a single frame, it's usually length 1.
    pose_batch = pose_samples[0]

    pred_instances = getattr(pose_batch, "pred_instances", None)
    if pred_instances is not None and hasattr(pred_instances, "keypoints"):
        keypoints = pred_instances.keypoints  # (num_instances, num_keypoints, 2)
        keypoint_scores = getattr(pred_instances, "keypoint_scores", None)  # (num_instances, num_keypoints) if present

        if isinstance(keypoints, torch.Tensor):
            keypoints_np = keypoints.detach().cpu().numpy()
        else:
            keypoints_np = np.asarray(keypoints)

        if keypoint_scores is not None:
            if isinstance(keypoint_scores, torch.Tensor):
                keypoint_scores_np = keypoint_scores.detach().cpu().numpy()
            else:
                keypoint_scores_np = np.asarray(keypoint_scores)
        else:
            keypoint_scores_np = None

        if keypoints_np.ndim == 3 and keypoints_np.shape[0] > 0:
            # Build a simple landmarks map for the fusion workspace
            first_kpts = keypoints_np[0]  # (num_kpts, 2)
            if keypoint_scores_np is not None and keypoint_scores_np.shape[0] > 0:
                first_scores = keypoint_scores_np[0]
            else:
                first_scores = None

            num_kpts = first_kpts.shape[0]
            landmarks = {}
            for i in range(num_kpts):
                kname = f"synthpose_kpt_{i}"
                kp_x = float(first_kpts[i, 0])
                kp_y = float(first_kpts[i, 1])
                score = float(first_scores[i]) if (first_scores is not None and i < first_scores.shape[0]) else 0.0
                landmarks[kname] = {"x": kp_x, "y": kp_y, "z": 0.0, "visibility": score}

            snapshot = PoseSnapshot(timestamp=time.time(), landmarks=landmarks)
            try:
                workspace.update_lower_body(snapshot)
            except Exception:
                logger.exception("Failed to publish SynthPose snapshot to fusion workspace")

    # Draw skeletons for all instances using MMPose visualizer
    visualizer.add_datasample(
        name="vis",
        image=vis_frame,
        data_sample=pose_batch,
        draw_gt=False,
        draw_pred=True,
        show=False,
        out_file=None
    )

    vis_frame = visualizer.get_image()
    cv2.imshow(window_name, vis_frame)
    return True

def run_capture_loop(camera_index: int, yolo_model, yolo_device, synth_model, visualizer):
    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        raise RuntimeError("Could not open webcam.")

    window_name = "SynthPose HRNet48 - Live Pose Estimation"
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                break

            cont = process_frame(frame, yolo_model, yolo_device, synth_model, visualizer, workspace, window_name)
            if not cont:
                break

            if cv2.waitKey(5) & 0xFF == 27:
                logger.info("ESC pressed, stopping Synthpose loop")
                break

            if cv2.getWindowProperty(window_name, cv2.WND_PROP_VISIBLE) < 1:
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="SynthPose MMPose capture server")
    parser.add_argument("--camera-index", type=int, default=0, help="OpenCV camera index (default: 0)")
    parser.add_argument("--osc-host", default="0.0.0.0", help="Interface for Unity OSC packets (default: 0.0.0.0)")
    parser.add_argument("--osc-port", type=int, default=9000, help="UDP port for Unity OSC packets (default: 9000)")
    parser.add_argument("--log-level", default="INFO", help="Logging level (default: INFO)")
    return parser.parse_args()


async def async_main(args: argparse.Namespace) -> None:
    # Initialize models in a thread to avoid blocking the event loop
    yolo_model, yolo_device, synth_model, visualizer = await asyncio.to_thread(setup_models)

    osc_task = asyncio.create_task(
        start_osc_server(args.osc_host, args.osc_port, workspace.handle_quest_packet)
    )

    try:
        # Run the blocking capture/inference loop in a thread so OSC can run concurrently
        await asyncio.to_thread(run_capture_loop, args.camera_index, yolo_model, yolo_device, synth_model, visualizer)
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
        logger.info("Shutting down SynthPose + OSC fusion workspace")


if __name__ == "__main__":
    main()