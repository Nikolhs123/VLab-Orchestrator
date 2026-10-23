param(
    [Parameter(Mandatory = $true)][string]$SdkProject,
    [string]$UnityEditorData = "C:/Program Files/Unity/Hub/Editor/6000.3.11f1/Editor/Data",
    [string]$SourceRoot = "",
    [string]$ArtifactsPath = (Join-Path ([IO.Path]::GetTempPath()) "vlab-sdk-compile-check"),
    [switch]$IncludeEditor
)

# Compile-only API check of canonical framework runtime + five adapters against the
# SDK assemblies already compiled in $SdkProject (Library/ScriptAssemblies, Assets/Photon).
$project = Join-Path $PSScriptRoot "SdkCompileCheck.csproj"
$buildArgs = @(
    "build", $project, "--artifacts-path", $ArtifactsPath, "-nologo", "-v:q",
    "-p:UnityEditorData=$UnityEditorData",
    "-p:SdkScriptAssemblies=$SdkProject/Library/ScriptAssemblies",
    "-p:PhotonAssets=$SdkProject/Assets/Photon"
)
if ($SourceRoot) { $buildArgs += "-p:SourceRoot=$SourceRoot" }
if ($IncludeEditor) { $buildArgs += "-p:IncludeEditor=true" }
& dotnet @buildArgs
exit $LASTEXITCODE
