@echo off
call ..\.synthpose_env\Scripts\activate.bat
python ..\pose_stream_server\synthpose\synthpose_mmpose_server.py
pause
