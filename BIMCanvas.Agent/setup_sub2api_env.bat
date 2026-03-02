@echo off
echo.
echo =======================================================
echo          Configuring AI Environment
echo =======================================================
echo.

echo [1/2] Setting AGENT_SDK_BASE_URL...
setx AGENT_SDK_BASE_URL "https://css.youngala.com"
echo.

echo [2/2] Setting AGENT_SDK_API_KEY..
setx AGENT_SDK_API_KEY "sk-25004efb0e70feec9d3667b7229284ab4c6601f2c80c2c17d74ca248e09d7cf4"
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
