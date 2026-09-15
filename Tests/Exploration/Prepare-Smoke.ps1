# Build an isolated smoke-test project. Does not open or modify the user's scene.
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$project = Join-Path $workspace 'Temp/ExplorationChecks/Unity'
New-Item -ItemType Directory -Force "$project/Assets/Editor", "$project/Packages", "$project/ProjectSettings" | Out-Null
$files = @('Protein/MoleculeCatalog.cs','Protein/ExplorationMoleculeRenderer.cs','Protein/BackboneChain.cs',
 'Protein/RibbonMeshBuilder.cs','Protein/SecondaryStructureAssigner.cs','Protein/ProteinLoader.cs',
 'Protein/AtomInfo.cs','Protein/PLDDTColorizer.cs','Quest/RuntimeMaterials.cs','Util/StreamingAssetsUrl.cs',
 'Intro/MoleculeExplorationController.cs','AI/AIAssistantStateTester.cs','Camera/CameraTransitionDirector.cs','Camera/LevelStage.cs',
 'UI/HoloFont.cs','UI/HoloSpriteFactory.cs','UI/ScreenSafePanel.cs',
 'Intro/ExplorationDockingSession.cs','Intro/ExplorationDockingController.cs','Protein/ExplorationLigandRenderer.cs',
 'Intro/ExplorationDockingController.GuidedUI.cs','Protein/ExplorationDockingEffects.cs','Quest/MutationExperimentEffects.cs','AI/AIAssistantFollower.cs')
foreach ($file in $files) {
 Copy-Item -LiteralPath "$workspace/Assets/Scripts/$file" -Destination "$project/Assets/$(Split-Path $file -Leaf)"
}
Copy-Item -LiteralPath "$PSScriptRoot/UnitySmoke.cs" -Destination "$project/Assets/Editor/ExplorationSmoke.cs"
Copy-Item -LiteralPath "$PSScriptRoot/StateTesterSmoke.cs" -Destination "$project/Assets/Editor/StateTesterSmoke.cs"
Copy-Item -LiteralPath "$PSScriptRoot/DockingSmoke.cs" -Destination "$project/Assets/Editor/DockingSmoke.cs"
Copy-Item -LiteralPath "$PSScriptRoot/SceneDependencies.cs" -Destination "$project/Assets/SceneDependencies.cs"
New-Item -ItemType Directory -Force "$project/Assets/StreamingAssets/exploration" | Out-Null
Get-ChildItem -LiteralPath "$workspace/Assets/StreamingAssets/exploration" -Filter '*.pdb' | Copy-Item -Destination "$project/Assets/StreamingAssets/exploration"
Copy-Item -LiteralPath "$workspace/ProjectSettings/ProjectVersion.txt" -Destination "$project/ProjectSettings/ProjectVersion.txt"
$ugui = Get-ChildItem -LiteralPath "$workspace/Library/PackageCache" -Directory -Filter 'com.unity.ugui@*' | Select-Object -First 1
if (-not $ugui) { throw 'Open the main Unity project once to populate the package cache.' }
# 구조를 마우스로 돌리는 데 새 Input System을 쓴다. 없으면 컨트롤러가 아예 컴파일되지 않는다.
$input = Get-ChildItem -LiteralPath "$workspace/Library/PackageCache" -Directory -Filter 'com.unity.inputsystem@*' | Select-Object -First 1
if (-not $input) { throw 'Open the main Unity project once to populate the package cache.' }
$manifest = @{ dependencies = @{
 'com.unity.ugui' = ('file:' + $ugui.FullName.Replace('\','/'))
 'com.unity.inputsystem' = ('file:' + $input.FullName.Replace('\','/'))
 'com.unity.modules.ui' = '1.0.0'; 'com.unity.modules.physics' = '1.0.0'
 'com.unity.modules.unitywebrequest' = '1.0.0'; 'com.unity.modules.imageconversion' = '1.0.0'
 'com.unity.modules.jsonserialize' = '1.0.0'
 'com.unity.modules.particlesystem' = '1.0.0'
}} | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText("$project/Packages/manifest.json", $manifest, [System.Text.UTF8Encoding]::new($false))
Write-Output "Prepared $project. Run Unity -batchmode -projectPath <this path> -executeMethod ExplorationSmoke.Run -logFile <log path> (without -quit)."
