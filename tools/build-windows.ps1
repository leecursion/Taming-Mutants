<#
.SYNOPSIS
    경진대회 제출용 Windows 빌드를 만들고 zip으로 묶는다.

.DESCRIPTION
    빌드 씬은 ProjectSettings/EditorBuildSettings.asset에 등록된 것을 그대로 쓴다
    (현재 Assets/Scenes/Lab_Desktop.unity).

    Unity 에디터가 이 프로젝트를 열어둔 상태면 실패한다 — 배치 빌드는 프로젝트 폴더를
    단독으로 잠가야 하기 때문이다. Unity를 닫고 실행하세요.

.EXAMPLE
    .\tools\build-windows.ps1
    .\tools\build-windows.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe",
    [string]$Version = "0.1.0",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$projectPath = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $projectPath "Build" }

$productName = "TamingMutants"
$stageName   = "${productName}_v${Version}"
$stageDir    = Join-Path $OutputRoot $stageName
$exePath     = Join-Path $stageDir "$productName.exe"
$zipPath     = Join-Path $OutputRoot "$stageName.zip"
$logPath     = Join-Path $OutputRoot "build.log"

if (-not (Test-Path $UnityPath)) {
    throw "Unity를 찾을 수 없습니다: $UnityPath`n-UnityPath 로 경로를 지정하세요."
}

# 에디터가 프로젝트를 열고 있으면 배치 빌드가 잠금 충돌로 실패한다. 미리 잡아준다.
$running = Get-Process Unity -ErrorAction SilentlyContinue
if ($running) {
    Write-Warning "Unity 에디터가 실행 중입니다. 이 프로젝트를 열고 있다면 빌드가 실패합니다."
    $answer = Read-Host "그래도 계속할까요? (y/N)"
    if ($answer -ne "y") { return }
}

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Write-Host "빌드 중... (수 분 걸립니다)" -ForegroundColor Cyan
Write-Host "  프로젝트 : $projectPath"
Write-Host "  출력     : $exePath"
Write-Host "  로그     : $logPath"

$unityArgs = @(
    "-quit", "-batchmode", "-nographics",
    "-projectPath", $projectPath,
    "-buildWindows64Player", $exePath,
    "-logFile", $logPath
)

$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow

if ($proc.ExitCode -ne 0) {
    Write-Host ""
    Write-Host "빌드 실패 (exit $($proc.ExitCode)). 로그 마지막 40줄:" -ForegroundColor Red
    if (Test-Path $logPath) { Get-Content $logPath -Tail 40 }
    throw "빌드에 실패했습니다."
}

if (-not (Test-Path $exePath)) {
    throw "빌드는 끝났지만 exe가 없습니다: $exePath"
}

# 제출물 안내문을 빌드 폴더에 함께 넣는다.
$readmeSource = Join-Path $PSScriptRoot "SUBMISSION_README.txt"
if (Test-Path $readmeSource) {
    Copy-Item $readmeSource (Join-Path $stageDir "README.txt")
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "압축 중..." -ForegroundColor Cyan
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "완료" -ForegroundColor Green
Write-Host "  실행 파일 : $exePath"
Write-Host "  제출용 zip: $zipPath ($sizeMb MB)"
Write-Host ""
Write-Host "제출 전 확인:" -ForegroundColor Yellow
Write-Host "  1. exe를 직접 실행해 AI 대화와 음성이 동작하는지 확인"
Write-Host "  2. Unity에서 [Tools/Taming Mutants/배포용 프록시 배선] > [빌드 전 점검]이 통과했는지 확인"
Write-Host "  3. zip을 Google Drive에 올리고 '링크가 있는 모든 사용자'로 공유 설정"
