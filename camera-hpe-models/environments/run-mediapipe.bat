@echo off
call ..\.mediapipe_env\Scripts\activate.bat
python ..\pose_stream_server\mediapipe\mediapipe_stream_server.py
pause
