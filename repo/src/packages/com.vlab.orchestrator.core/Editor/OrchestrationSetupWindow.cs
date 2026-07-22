using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using VLabOrchestrator.Runtime;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VLabOrchestrator.Editor
{
    public sealed class OrchestrationSetupWindow : EditorWindow
    {
        private const string ConfigPath =
            "Assets/Resources/VLabOrchestrator/OrchestrationProjectConfig.asset";
        private const string InstrumentPrefabPath =
            "Assets/VLabOrchestrator/Prefabs/GenericSharedInstrument.prefab";
        private const string ParticipantPrefabPath =
            "Assets/VLabOrchestrator/Prefabs/GenericResearcherAvatar.prefab";

        private static readonly BackendDescriptor[] Backends =
        {
            new(
                OrchestrationBackendType.PUN2,
                "Photon PUN 2",
                "com.vlab.orchestrator.backend.pun2",
                "Photon.Pun.PhotonView",
                "2.55",
                string.Empty,
                "https://assetstore.unity.com/packages/tools/network/pun-2-free-119922",
                "https://doc.photonengine.com/pun/current/getting-started/initial-setup",
                "https://dashboard.photonengine.com",
                @"Photon Engine\ScriptingNetwork\PUN 2 - FREE.unitypackage",
                "A8E00E2D7111C9238A4E6C63824033826A4E4C56E0F266C08E49DA19699DC42C"),
            new(
                OrchestrationBackendType.Fusion,
                "Photon Fusion",
                "com.vlab.orchestrator.backend.fusion",
                "Fusion.NetworkObject",
                "2.0.9 (build 1566)",
                string.Empty,
                "https://doc.photonengine.com/fusion/v2/getting-started/sdk-download",
                "https://doc.photonengine.com/fusion/v2/getting-started/sdk-download",
                "https://dashboard.photonengine.com",
                @"Photon Engine\ScriptingNetwork\Photon Fusion.unitypackage",
                "7C7DEA3845E3886D0A7C3A2BDCEEB0817E5B2F8DAF6961DFF72ECBB671353435"),
            new(
                OrchestrationBackendType.UnityNetcode,
                "Unity Netcode for GameObjects",
                "com.vlab.orchestrator.backend.netcode",
                "Unity.Netcode.NetworkObject",
                "2.11.0",
                "com.unity.netcode.gameobjects@2.11.0",
                string.Empty,
                "https://docs-multiplayer.unity3d.com/netcode/current/installation/",
                string.Empty,
                string.Empty,
                string.Empty),
            new(
                OrchestrationBackendType.FishNet,
                "FishNet",
                "com.vlab.orchestrator.backend.fishnet",
                "FishNet.Object.NetworkObject",
                "4.6.22",
                string.Empty,
                "https://assetstore.unity.com/packages/tools/network/fishnet-networking-evolved-207815",
                "https://fish-networking.gitbook.io/docs/tutorials/getting-started/installing-fish-networking",
                string.Empty,
                @"FirstGearGames\ScriptingNetwork\FishNet Networking Evolved.unitypackage",
                "E897878B66A5EC1AA6B2EA019610B357A011CB459E6BDCE34F6C7D0D7FCE4E7D"),
            new(
                OrchestrationBackendType.Mirror,
                "Mirror",
                "com.vlab.orchestrator.backend.mirror",
                "Mirror.NetworkIdentity",
                "96.0.1",
                string.Empty,
                "https://assetstore.unity.com/packages/tools/network/mirror-129321",
                "https://mirror-networking.gitbook.io/docs/manual/general/migration-guide",
                string.Empty,
                @"Mirror Networking\ScriptingNetwork\Mirror.unitypackage",
                "020F733ACD3821CD6F958C992EC1B3E2750A687FBD847CDC21C17E285746286C")
        };

        private static readonly string[] BackendTabLabels =
        {
            "PUN 2",
            "Fusion",
            "Netcode",
            "FishNet",
            "Mirror"
        };

        private OrchestrationProjectConfig config;
        private Vector2 scroll;
        private string validationMessage = "Configuration has not been validated.";
        private MessageType validationType = MessageType.Info;
        private AddRequest addRequest;
        private string pendingInstallLabel = string.Empty;

        private GUIStyle headerStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle cardStyle;
        private GUIStyle sdkInstalledCardStyle;
        private GUIStyle sdkMissingCardStyle;

        [MenuItem("VLab Orchestrator/Setup", false, 0)]
        public static void Open()
        {
            var window = GetWindow<OrchestrationSetupWindow>();
            window.titleContent = new GUIContent("VLab Orchestrator");
            window.minSize = new Vector2(720f, 430f);
            window.Show();
        }

        [MenuItem("VLab Orchestrator/Create or Select Project Configuration")]
        public static void CreateOrSelectProjectConfiguration()
        {
            OrchestrationProjectConfig projectConfig = LoadOrCreateConfiguration();
            Selection.activeObject = projectConfig;
            if (projectConfig != null)
            {
                EditorGUIUtility.PingObject(projectConfig);
                Debug.Log($"[VLab Orchestrator] Project configuration ready at {ConfigPath}");
            }
        }

        [InitializeOnLoadMethod]
        private static void EnsureConfigurationExistsAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return;

                LoadOrCreateConfiguration();
            };
        }

        private void OnEnable()
        {
            config = LoadOrCreateConfiguration();
            // Package installation can reload scripts before the request callback runs.
            // Dropping the stale handle does not cancel Unity's Package Manager operation.
            addRequest = null;
            pendingInstallLabel = string.Empty;
            EditorApplication.update -= MonitorPackageInstall;
            EditorApplication.delayCall += RefreshSetupStatus;
        }

        private void OnDisable()
        {
            EditorApplication.update -= MonitorPackageInstall;
        }

        private void OnGUI()
        {
            if (addRequest != null && addRequest.IsCompleted)
                MonitorPackageInstall();

            EnsureStyles();
            DrawHeader();

            if (config == null)
            {
                EditorGUILayout.HelpBox(
                    "The project configuration could not be created.",
                    MessageType.Error);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawValidationSection();
            DrawBackendSection();
            DrawConfigurationHandoff();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            Rect header = EditorGUILayout.GetControlRect(false, 92f);
            EditorGUI.DrawRect(header, Hex("#12383C"));
            EditorGUI.DrawRect(
                new Rect(header.x, header.yMax - 5f, header.width, 5f),
                Hex("#E5B64F"));

            GUI.Label(
                new Rect(header.x + 20f, header.y + 15f, header.width - 40f, 34f),
                "VLAB ORCHESTRATOR",
                headerStyle);
            GUI.Label(
                new Rect(header.x + 22f, header.y + 52f, header.width - 44f, 24f),
                "RESEARCH SOFTWARE WORKSPACE  /  SDK-INDEPENDENT CORE",
                subtitleStyle);
            GUILayout.Space(8f);
        }

        private void DrawBackendSection()
        {
            BeginSection("Networking Backend");
            EditorGUILayout.HelpBox(
                "All five audited backends can coexist in this project. Choose a tab and use its single setup action. Green means the adapter, exact tested SDK, compilation, and any required local credentials are ready.",
                MessageType.Info);
            GUILayout.Space(6f);

            int selectedIndex = Array.FindIndex(
                Backends,
                item => item.Type == config.backendType);
            if (selectedIndex < 0)
                selectedIndex = 0;

            int nextIndex = GUILayout.Toolbar(selectedIndex, BackendTabLabels, GUILayout.Height(30f));
            if (nextIndex != selectedIndex)
            {
                SelectBackend(Backends[nextIndex]);
                selectedIndex = nextIndex;
            }

            BackendDescriptor backend = Backends[selectedIndex];
            bool adapterPackageInstalled = IsPackageInstalled(backend.PackageId);
            bool sdkAvailable = FindLoadedType(backend.SdkTypeName) != null;
            bool compilationReady = !EditorUtility.scriptCompilationFailed;
            bool versionSupported = TryGetInstalledSdkVersion(backend, out string installedVersion) &&
                                    IsSupportedSdkVersion(backend, installedVersion);
            bool credentialsReady = HasRequiredLocalCredentials(backend);
            bool backendReady = adapterPackageInstalled && sdkAvailable && compilationReady &&
                                versionSupported && credentialsReady;
            GUIStyle backendCardStyle = backendReady
                ? sdkInstalledCardStyle
                : sdkMissingCardStyle;

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(backendCardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(backend.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(
                        !compilationReady
                            ? "SCRIPT ERRORS"
                            : !adapterPackageInstalled
                            ? "ADAPTER MISSING"
                            : sdkAvailable && !versionSupported
                            ? "VERSION MISMATCH"
                            : !sdkAvailable
                            ? "SDK MISSING"
                            : !credentialsReady ? "APP ID MISSING" : "READY",
                        EditorStyles.boldLabel);
                }

                EditorGUILayout.LabelField(
                    !compilationReady
                        ? "Unity must compile without errors before this backend is ready."
                        : !adapterPackageInstalled
                        ? "The VLab Orchestrator adapter package must be added before this backend is ready."
                        : sdkAvailable && !versionSupported
                        ? $"Detected {installedVersion}. This reproducible workspace requires {backend.TestedVersion}."
                        : sdkAvailable && !credentialsReady
                        ? "Create your own Photon application and enter its App ID locally. Credentials are intentionally never distributed or committed."
                        : sdkAvailable
                        ? $"Audited SDK {installedVersion} detected. This backend is ready."
                        : $"This integration was tested with SDK {backend.TestedVersion}.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "Installed from the official provider; no vendor SDK is redistributed by VLab Orchestrator.",
                    EditorStyles.wordWrappedMiniLabel);

                GUILayout.Space(8f);
                GUI.enabled = addRequest == null;
                string primaryLabel = ResolvePrimaryActionLabel(
                    backend,
                    adapterPackageInstalled,
                    sdkAvailable && versionSupported,
                    credentialsReady);
                if (GUILayout.Button(primaryLabel, GUILayout.Height(36f)))
                {
                    HandlePrimaryBackendAction(
                        backend,
                        adapterPackageInstalled,
                        sdkAvailable && versionSupported,
                        credentialsReady);
                }
                GUI.enabled = true;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Official setup guide", EditorStyles.linkLabel))
                        OpenUrl(backend.SdkSetupUrl);

                    if (!string.IsNullOrWhiteSpace(backend.CredentialsUrl) &&
                        GUILayout.Button("Photon AppId", EditorStyles.linkLabel))
                    {
                        OpenUrl(backend.CredentialsUrl);
                    }
                }
            }

            if (addRequest != null)
            {
                EditorGUILayout.HelpBox(
                    $"Installing {pendingInstallLabel}. Unity may reload scripts when the package is ready.",
                    MessageType.Info);
            }

            EndSection();
        }

        private static string ResolvePrimaryActionLabel(
            BackendDescriptor backend,
            bool adapterPackageInstalled,
            bool sdkReady,
            bool credentialsReady)
        {
            if (!adapterPackageInstalled)
                return $"Add {backend.DisplayName} Adapter";
            if (!sdkReady)
                return $"Install {backend.DisplayName}";
            if (!credentialsReady)
                return "Configure Photon AppId";
            return $"Use {backend.DisplayName}";
        }

        private void HandlePrimaryBackendAction(
            BackendDescriptor backend,
            bool adapterPackageInstalled,
            bool sdkReady,
            bool credentialsReady)
        {
            SelectBackend(backend);

            if (!adapterPackageInstalled)
            {
                InstallLocalAdapterPackage(backend);
                return;
            }

            if (!sdkReady)
            {
                InstallSdkPackage(backend);
                return;
            }

            if (!credentialsReady)
            {
                FocusPhotonSettings(backend);
                return;
            }

            ValidateProject();
        }

        private void SelectBackend(BackendDescriptor backend)
        {
            if (config.backendType == backend.Type)
                return;

            Undo.RecordObject(config, "Select orchestration backend");
            config.backendType = backend.Type;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            SoftwareXRuntimeAuthoring.SyncRuntimeConfiguration(config);
            RefreshSetupStatus();
        }

        private void DrawConfigurationHandoff()
        {
            BeginSection("Continue Configuration");
            EditorGUILayout.HelpBox(
                "Prefabs, allocation policy, benchmark scenarios, and developer options are maintained in the project configuration asset.",
                MessageType.Info);
            if (GUILayout.Button("Open Project Configuration", GUILayout.Height(34f)))
                CreateOrSelectProjectConfiguration();
            EndSection();
        }

        private void DrawValidationSection()
        {
            BeginSection("Setup Status");
            EditorGUILayout.HelpBox(validationMessage, validationType);
            if (GUILayout.Button("Refresh Setup Status", GUILayout.Height(30f)))
                ValidateProject();
            EndSection();
        }

        private void RefreshSetupStatus()
        {
            if (this == null || config == null)
                return;

            ValidateProject();
            Repaint();
        }

        private void ValidateProject()
        {
            BackendDescriptor selected = Backends.First(item => item.Type == config.backendType);
            bool adapterPackageInstalled = IsPackageInstalled(selected.PackageId);
            bool sdkAvailable = FindLoadedType(selected.SdkTypeName) != null;
            bool compilationReady = !EditorUtility.scriptCompilationFailed;
            bool versionSupported = TryGetInstalledSdkVersion(selected, out string installedVersion) &&
                                    IsSupportedSdkVersion(selected, installedVersion);

            if (!compilationReady)
            {
                validationMessage =
                    "Unity has script compilation errors. Backend setup is not complete; inspect the first Console error before running the project.";
                validationType = MessageType.Error;
            }
            else if (!adapterPackageInstalled)
            {
                validationMessage =
                    $"{selected.DisplayName} adapter package is missing. Use the backend tab action to add it.";
                validationType = MessageType.Error;
            }
            else if (!sdkAvailable)
            {
                validationMessage =
                    $"{selected.DisplayName} is selected, but its SDK is not installed. Use the red backend card below.";
                validationType = MessageType.Warning;
            }
            else if (!versionSupported)
            {
                validationMessage =
                    $"{selected.DisplayName} {installedVersion} is installed, but the audited matrix requires {selected.TestedVersion}. Use the backend action to import the verified package.";
                validationType = MessageType.Error;
            }
            else if (!HasRequiredLocalCredentials(selected))
            {
                validationMessage =
                    $"{selected.DisplayName} requires the evaluator's own Photon App ID. Use Configure Photon AppId; this private credential is intentionally excluded from Git.";
                validationType = MessageType.Warning;
            }
            else
            {
                validationMessage =
                    $"{selected.DisplayName} {installedVersion} is selected, installed, and compilation-ready.";
                validationType = MessageType.Info;
            }
        }

        private static bool TryGetInstalledSdkVersion(
            BackendDescriptor backend,
            out string installedVersion)
        {
            installedVersion = "unknown version";

            switch (backend.Type)
            {
                case OrchestrationBackendType.PUN2:
                    return TryReadVersionMarker(
                        "Assets/Photon/PhotonUnityNetworking/Code/PhotonNetwork.cs",
                        "PunVersion = \"",
                        "\"",
                        out installedVersion);

                case OrchestrationBackendType.Fusion:
                    if (!TryReadFile("Assets/Photon/Fusion/release_history.txt", out string fusionHistory))
                        return false;

                    bool is209 = fusionHistory.Contains("# 2.0.9", StringComparison.Ordinal);
                    bool isBuild1566 = fusionHistory.Contains("Build 1566", StringComparison.Ordinal);
                    installedVersion = is209 && isBuild1566
                        ? "2.0.9 (build 1566)"
                        : ReadFirstHeading(fusionHistory);
                    return true;

                case OrchestrationBackendType.UnityNetcode:
                    PackageManagerPackageInfo netcode = PackageManagerPackageInfo
                        .GetAllRegisteredPackages()
                        .FirstOrDefault(package =>
                            string.Equals(
                                package.name,
                                "com.unity.netcode.gameobjects",
                                StringComparison.Ordinal));
                    if (netcode == null)
                        return false;

                    installedVersion = netcode.version;
                    return true;

                case OrchestrationBackendType.FishNet:
                    return TryReadVersionMarker(
                        "Assets/FishNet/package.json",
                        "\"version\": \"",
                        "\"",
                        out installedVersion);

                case OrchestrationBackendType.Mirror:
                    if (!TryReadFile("Assets/Mirror/version.txt", out string mirrorVersion))
                        return false;

                    installedVersion = mirrorVersion.Trim();
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsSupportedSdkVersion(
            BackendDescriptor backend,
            string installedVersion)
        {
            return backend.Type switch
            {
                OrchestrationBackendType.PUN2 => installedVersion == "2.55",
                OrchestrationBackendType.Fusion => installedVersion == "2.0.9 (build 1566)",
                OrchestrationBackendType.UnityNetcode => installedVersion == "2.11.0",
                OrchestrationBackendType.FishNet => installedVersion == "4.6.22",
                OrchestrationBackendType.Mirror => installedVersion == "96.0.1",
                _ => false
            };
        }

        private static bool HasRequiredLocalCredentials(BackendDescriptor backend)
        {
            return backend.Type switch
            {
                OrchestrationBackendType.PUN2 => HasNonEmptyYamlSetting(
                    "Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset",
                    "AppIdRealtime"),
                OrchestrationBackendType.Fusion => HasNonEmptyYamlSetting(
                    "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset",
                    "AppIdFusion"),
                _ => true
            };
        }

        private static bool HasNonEmptyYamlSetting(string assetPath, string settingName)
        {
            if (!File.Exists(assetPath))
                return false;

            string prefix = settingName + ":";
            foreach (string line in File.ReadLines(assetPath))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string value = trimmed.Substring(prefix.Length).Trim().Trim('"');
                return value.Length >= 16 &&
                       !value.Contains("YOUR", StringComparison.OrdinalIgnoreCase) &&
                       !value.Contains("APPID", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void FocusPhotonSettings(BackendDescriptor backend)
        {
            string settingsPath = backend.Type == OrchestrationBackendType.Fusion
                ? "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset"
                : "Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset";
            UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(settingsPath);
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }

            if (!string.IsNullOrWhiteSpace(backend.CredentialsUrl))
                OpenUrl(backend.CredentialsUrl);

            validationMessage =
                $"Create a {backend.DisplayName} application in the Photon dashboard, then paste its App ID into the selected local settings asset. The value remains ignored by Git.";
            validationType = MessageType.Warning;
        }

        private static bool TryReadVersionMarker(
            string filePath,
            string prefix,
            string suffix,
            out string version)
        {
            version = "unknown version";
            if (!TryReadFile(filePath, out string contents))
                return false;

            int start = contents.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                return true;

            start += prefix.Length;
            int end = contents.IndexOf(suffix, start, StringComparison.Ordinal);
            if (end > start)
                version = contents.Substring(start, end - start).Trim();
            return true;
        }

        private static bool TryReadFile(string filePath, out string contents)
        {
            contents = string.Empty;
            try
            {
                if (!File.Exists(filePath))
                    return false;

                contents = File.ReadAllText(filePath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ReadFirstHeading(string contents)
        {
            using var reader = new StringReader(contents);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                    return line.Substring(2).Trim();
            }

            return "unknown version";
        }

        private static OrchestrationProjectConfig LoadOrCreateConfiguration()
        {
            OrchestrationProjectConfig existing =
                AssetDatabase.LoadAssetAtPath<OrchestrationProjectConfig>(ConfigPath);
            if (existing != null)
                return existing;

            EnsureAssetFolder("Assets", "Resources");
            EnsureAssetFolder("Assets/Resources", "VLabOrchestrator");

            var created = CreateInstance<OrchestrationProjectConfig>();
            created.participantVisualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ParticipantPrefabPath);
            created.primarySharedInstrumentPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(InstrumentPrefabPath);
            AssetDatabase.CreateAsset(created, ConfigPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        private static void EnsureAssetFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool IsPackageInstalled(string packageId)
        {
            return PackageManagerPackageInfo.GetAllRegisteredPackages()
                .Any(package => string.Equals(package.name, packageId, StringComparison.Ordinal));
        }

        private void InstallLocalAdapterPackage(BackendDescriptor backend)
        {
            string path = ResolveBackendPackagePath(backend.PackageId);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                validationMessage = $"Local adapter package source was not found for {backend.DisplayName}.";
                validationType = MessageType.Error;
                return;
            }

            StartPackageInstall(
                $"file:{path.Replace('\\', '/')}",
                $"{backend.DisplayName} adapter");
        }

        private void InstallSdkPackage(BackendDescriptor backend)
        {
            if (string.IsNullOrWhiteSpace(backend.SdkInstallSource))
            {
                if (TryFindAuditedUnityPackage(backend, out string packagePath))
                {
                    validationMessage =
                        $"Importing the audited {backend.DisplayName} {backend.TestedVersion} package from Unity's local package cache...";
                    validationType = MessageType.Info;
                    Repaint();
                    AssetDatabase.ImportPackage(packagePath, false);
                    EditorApplication.delayCall += RefreshSetupStatus;
                    return;
                }

                OpenUrl(backend.SdkInstallUrl);
                validationMessage = backend.Type == OrchestrationBackendType.Fusion
                    ? "The audited Fusion package is not in Unity's local cache. Download Fusion 2.0.9 build 1566 from Photon once, then click Install again. Photon sign-in and license acceptance remain on Photon's site."
                    : $"The audited {backend.DisplayName} package is not in Unity's local cache. Download/import version {backend.TestedVersion} from the official provider once, then click Install again.";
                validationType = MessageType.Info;
                Repaint();
                return;
            }

            StartPackageInstall(backend.SdkInstallSource, $"{backend.DisplayName} SDK");
        }

        private static bool TryFindAuditedUnityPackage(
            BackendDescriptor backend,
            out string packagePath)
        {
            packagePath = string.Empty;
            if (string.IsNullOrWhiteSpace(backend.AuditedPackageSha256))
                return false;

            foreach (string candidate in GetUnityPackageCandidates(backend))
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    if (!string.Equals(
                            ComputeSha256(candidate),
                            backend.AuditedPackageSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    packagePath = candidate;
                    return true;
                }
                catch (IOException)
                {
                    // Unity may still be finishing the provider download.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore inaccessible candidates and continue with the official route.
                }
            }

            return false;
        }

        private static IEnumerable<string> GetUnityPackageCandidates(BackendDescriptor backend)
        {
            string assetStoreRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Unity",
                "Asset Store-5.x");
            if (!string.IsNullOrWhiteSpace(backend.AssetStoreCacheRelativePath))
            {
                yield return Path.Combine(assetStoreRoot, backend.AssetStoreCacheRelativePath);
            }

            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            if (!Directory.Exists(downloads))
                yield break;

            foreach (string candidate in Directory.GetFiles(downloads, "*.unitypackage"))
                yield return candidate;
        }

        private static string ComputeSha256(string filePath)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private void StartPackageInstall(string packageSource, string displayLabel)
        {
            if (addRequest != null)
                return;

            pendingInstallLabel = displayLabel;
            validationMessage = $"Installing {displayLabel}...";
            validationType = MessageType.Info;
            try
            {
                addRequest = Client.Add(packageSource);
                EditorApplication.update -= MonitorPackageInstall;
                EditorApplication.update += MonitorPackageInstall;
            }
            catch (Exception exception)
            {
                addRequest = null;
                pendingInstallLabel = string.Empty;
                validationMessage = $"Could not start installation: {exception.Message}";
                validationType = MessageType.Error;
            }
        }

        private void MonitorPackageInstall()
        {
            if (addRequest == null || !addRequest.IsCompleted)
                return;

            EditorApplication.update -= MonitorPackageInstall;
            if (addRequest.Status == StatusCode.Success)
            {
                validationMessage = $"Installed {pendingInstallLabel}. Unity will refresh backend readiness after compilation.";
                validationType = MessageType.Info;
            }
            else
            {
                validationMessage = addRequest.Error?.message ?? "Package installation failed.";
                validationType = MessageType.Error;
            }

            addRequest = null;
            pendingInstallLabel = string.Empty;
            EditorApplication.delayCall += RefreshSetupStatus;
            Repaint();
        }

        private static void OpenUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
                Application.OpenURL(url);
        }

        private static string ResolveBackendPackagePath(string packageId)
        {
            string coreRoot = ResolveCorePackageRoot();
            string parent = string.IsNullOrEmpty(coreRoot)
                ? string.Empty
                : Directory.GetParent(coreRoot)?.FullName;
            return string.IsNullOrEmpty(parent) ? string.Empty : Path.Combine(parent, packageId);
        }

        private static string ResolveCorePackageRoot()
        {
            PackageManagerPackageInfo info =
                PackageManagerPackageInfo.FindForAssembly(typeof(OrchestrationSetupWindow).Assembly);
            return info?.resolvedPath;
        }

        private void BeginSection(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, sectionStyle);
            EditorGUILayout.BeginVertical(cardStyle);
        }

        private static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private void EnsureStyles()
        {
            headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                normal = { textColor = Hex("#F5F2E8") }
            };
            subtitleStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = Hex("#8EC5C0") }
            };
            sectionStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = Hex("#E5B64F") }
            };
            cardStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(8, 8, 4, 8)
            };
            sdkInstalledCardStyle ??= CreateBackendCardStyle(
                EditorGUIUtility.isProSkin ? Hex("#173B2A") : Hex("#DDF3E5"));
            sdkMissingCardStyle ??= CreateBackendCardStyle(
                EditorGUIUtility.isProSkin ? Hex("#4A2328") : Hex("#F8DADD"));
        }

        private static GUIStyle CreateBackendCardStyle(Color color)
        {
            var texture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();

            return new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = texture },
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(8, 8, 5, 9),
                border = new RectOffset(1, 1, 1, 1)
            };
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.white;
        }

        private sealed class BackendDescriptor
        {
            public BackendDescriptor(
                OrchestrationBackendType type,
                string displayName,
                string packageId,
                string sdkTypeName,
                string testedVersion,
                string sdkInstallSource,
                string sdkInstallUrl,
                string sdkSetupUrl,
                string credentialsUrl,
                string assetStoreCacheRelativePath,
                string auditedPackageSha256)
            {
                Type = type;
                DisplayName = displayName;
                PackageId = packageId;
                SdkTypeName = sdkTypeName;
                TestedVersion = testedVersion;
                SdkInstallSource = sdkInstallSource;
                SdkInstallUrl = sdkInstallUrl;
                SdkSetupUrl = sdkSetupUrl;
                CredentialsUrl = credentialsUrl;
                AssetStoreCacheRelativePath = assetStoreCacheRelativePath;
                AuditedPackageSha256 = auditedPackageSha256;
            }

            public OrchestrationBackendType Type { get; }
            public string DisplayName { get; }
            public string PackageId { get; }
            public string SdkTypeName { get; }
            public string TestedVersion { get; }
            public string SdkInstallSource { get; }
            public string SdkInstallUrl { get; }
            public string SdkSetupUrl { get; }
            public string CredentialsUrl { get; }
            public string AssetStoreCacheRelativePath { get; }
            public string AuditedPackageSha256 { get; }
        }
    }
}
