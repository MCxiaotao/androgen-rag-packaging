param(
    [string]$PythonExe = 'D:\miniconda\envs\admet_clean\python.exe',
    [string]$SmartCypJar = 'D:\external_tools\smartcyp\target\smartcyp.jar',
    [string]$FpgnnRepo = 'D:\external_models\FP-GNN_CYP',
    [string]$ChempropExe = 'D:\miniconda\envs\pk_env\Scripts\chemprop.exe',
    [string]$SygmaPython = 'D:\miniconda\envs\sygma_env\python.exe'
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $PythonExe)) {
    throw "Python not found: $PythonExe"
}

Write-Host "[check] Python: $PythonExe"

@"
mods = [
    'streamlit',
    'streamlit_ketcher',
    'pandas',
    'numpy',
    'rdkit',
    'PIL',
    'requests',
    'openai',
    'reportlab',
    'torch',
]
for name in mods:
    try:
        __import__(name)
        print(f'{name}: OK')
    except Exception as e:
        print(f'{name}: FAIL -> {type(e).__name__}: {e}')
"@ | & $PythonExe -

Write-Host "[check] SMARTCyp jar: $(Test-Path -LiteralPath $SmartCypJar) -> $SmartCypJar"
Write-Host "[check] FP-GNN repo: $(Test-Path -LiteralPath $FpgnnRepo) -> $FpgnnRepo"
Write-Host "[check] chemprop.exe: $(Test-Path -LiteralPath $ChempropExe) -> $ChempropExe"
Write-Host "[check] SyGMa python: $(Test-Path -LiteralPath $SygmaPython) -> $SygmaPython"

if (Test-Path -LiteralPath $SygmaPython) {
    @"
try:
    import sygma
    print('sygma: OK')
except Exception as e:
    print(f'sygma: FAIL -> {type(e).__name__}: {e}')
"@ | & $SygmaPython -
}
