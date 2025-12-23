@echo off
echo === Creating Mediapipe Environment ===
python -m venv ..\.mediapipe_env

echo === Activating Environment ===
call ..\.mediapipe_env\Scripts\activate.bat

echo === Upgrading pip ===
python.exe -m pip install --upgrade pip setuptools wheel

echo === Installing Mediapipe Dependencies ===
pip install -r requirements-mediapipe.txt

echo === Installation Complete ===
python -c "import mediapipe as mp, cv2, numpy as np; print('Mediapipe:', mp.__version__, '| OpenCV:', cv2.__version__, '| NumPy:', np.__version__)"
pause
