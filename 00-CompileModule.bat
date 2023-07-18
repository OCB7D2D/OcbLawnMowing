@echo off

REM call MC7D2D CopyTransform.dll ^
REM   /reference:"%PATH_7D2D_MANAGED%\Assembly-CSharp.dll" ^
REM   Plugins\*.cs

call MC7D2D LawnMowing.dll ^
  /reference:"%PATH_7D2D_MANAGED%\Assembly-CSharp.dll" ^
  Harmony\*.cs Library\*.cs Plugins\*.cs && ^
echo Successfully compiled LawnMowing.dll

pause