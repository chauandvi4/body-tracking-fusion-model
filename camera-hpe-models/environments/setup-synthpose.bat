@echo off
echo === Creating SynthPose GPU Environment ===
python -m venv ..\.synthpose_env

echo === Activating Environment ===
call ..\.synthpose_env\Scripts\activate.bat

echo === Upgrading pip / setuptools / wheel ===
python.exe -m pip install --upgrade pip setuptools wheel

echo === Installing CUDA-enabled PyTorch 2.1.0 (cu118) ===
pip install ^
  torch==2.1.0+cu118 ^
  torchvision==0.16.0+cu118 ^
  torchaudio==2.1.0+cu118 ^
  --index-url https://download.pytorch.org/whl/cu118

echo === Installing full MMCV with CUDA ops (cu118 + torch 2.1) ===
pip install mmcv==2.1.0 -f https://download.openmmlab.com/mmcv/dist/cu118/torch2.1.0/index.html

echo === Installing MMEngine + MMDetection ===
pip install mmengine==0.10.2
pip install mmdet==3.2.0

echo === Installing MMPose WITHOUT auto deps (avoid chumpy / version fights) ===
pip install mmpose==1.3.0 --no-deps

echo === Installing remaining SynthPose requirements ===
pip install -r requirements-synthpose.txt

echo === Done. You can now run run-synthpose.bat ===
pause
