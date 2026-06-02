@echo off
echo Configurando Unity SmartMerge para este repositorio...

set UNITY_VERSION=6000.4.1f1
set UNITY_YAML_MERGE=C:\Program Files\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Data\Tools\UnityYAMLMerge.exe

if not exist "%UNITY_YAML_MERGE%" (
    echo ERROR: No se encontro UnityYAMLMerge en:
    echo   %UNITY_YAML_MERGE%
    echo.
    echo Verifica que Unity %UNITY_VERSION% este instalado via Unity Hub.
    pause
    exit /b 1
)

git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "\"%UNITY_YAML_MERGE%\" merge -p --force --fallback none %%O %%B %%A %%A"

echo Listo! Unity SmartMerge configurado correctamente.
echo Driver: %UNITY_YAML_MERGE%
pause
