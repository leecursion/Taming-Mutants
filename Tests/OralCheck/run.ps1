$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$jsonLibrary = Get-ChildItem -Path (Join-Path $projectRoot 'Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll') | Select-Object -First 1
if (-not $jsonLibrary) { throw 'Unity에서 프로젝트를 한 번 열어 Newtonsoft 패키지를 준비해 주세요.' }
& dotnet run --project (Join-Path $PSScriptRoot 'OralCheck.Tests.csproj') "-p:NewtonsoftPath=$($jsonLibrary.FullName)"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
