@echo off

call MC7D2D LawnMowing.dll ^
  /reference:"%PATH_7D2D_MANAGED%\Assembly-CSharp.dll" ^
  Harmony\*.cs Library\*.cs && ^
echo Successfully compiled LawnMowing.dll

pause