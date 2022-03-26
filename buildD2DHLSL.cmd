rem This batch file compiles an HLSL (high-level shader language) file into a shader file
rem that can be loaded by Direct2D. It compiles the shader so that it supports "linking",
rem which is important for performance.

rem In your build event, you must first initialize the VS environment like so:
rem call "$(DevEnvDir)\..\Tools\VsDevCmd.bat"

rem %~1 = shader model version, e.g. 5_0, 4_0, 4_0_level_9_1
rem %~2 = input directory
rem %~3 = filename WITHOUT an extension
rem the input filename will be %~2\%~3.hlsl
rem the output filename will be %~2\%~3.cso

rem See also: https://docs.microsoft.com/en-us/windows/desktop/Direct2D/effect-shader-linking

rem Compile the export function
fxc /nologo /T "lib_%~1" /I "%WindowsSdkDir%\Include\%WindowsSDKVersion%\um" /O3 /WX /D D2D_FUNCTION /D D2D_ENTRY=main /Fl "%~2\%~3.fxlib" "%~2\%~3.hlsl"

rem Compile the full shader and embed the export function
rem Optionally, when wanting to look at the low level shader instructions, add: /Fh "%~2\%~3.h"
fxc /nologo /T "ps_%~1" /I "%WindowsSdkDir%\Include\%WindowsSDKVersion%\um" /O3 /WX /D D2D_FULL_SHADER /D D2D_ENTRY=main /setprivate "%~2\%~3.fxlib" /Fo "%~2\%~3.cso" "%~2\%~3.hlsl" 
