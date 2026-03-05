@echo off
echo.
echo =======================================================
echo          Configuring AI Environment
echo =======================================================
echo.

echo [1/2] Setting AGENT_SDK_BASE_URL...
setx AGENT_SDK_BASE_URL "http://127.0.0.1:8045"
echo.

echo [2/2] Setting AGENT_SDK_API_KEY..
setx AGENT_SDK_API_KEY "sk-46d40c8f901f46cd9c46e468e84534bb"
echo.

echo =======================================================
echo All environment variables have been set successfully!
echo.
echo IMPORTANT: You must open a NEW terminal or restart your
echo            application (like VS Code) for these
echo            changes to take effect.
echo =======================================================
echo.
pause
