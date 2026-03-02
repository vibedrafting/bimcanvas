@echo off
echo.
echo =======================================================
echo          Configuring AI Environment
echo =======================================================
echo.

echo [1/2] Setting AGENT_SDK_BASE_URL...
setx AGENT_SDK_BASE_URL "https://aitoll.net/api/gateway/cli/cc"
echo.

echo [2/2] Setting AGENT_SDK_API_KEY..
setx AGENT_SDK_API_KEY "pass_06a049bbe109012abf061d82504606a739c2e3c17baa7bd01e9a89acfafebac2"
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
