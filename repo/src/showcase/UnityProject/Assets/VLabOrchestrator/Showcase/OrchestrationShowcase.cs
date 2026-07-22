using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Thesis.Orchestration.Core;
using Thesis.Orchestration.Core.Models;
using UnityEngine;

namespace VLabOrchestrator.Showcase
{
    public sealed class OrchestrationShowcase : MonoBehaviour
    {
        private const string InstrumentId = "generic_instrument_01";
        private const string ResearcherA = "Researcher_A";
        private const string ResearcherB = "Researcher_B";

        private readonly List<string> eventHistory = new();
        private readonly Dictionary<string, double> requestStartedAt = new();

        [Header("Authored Scene")]
        [SerializeField] private ShowcaseSharedInstrumentDefinition instrumentDefinition;
        [SerializeField] private ShowcaseParticipantDefinition researcherA;
        [SerializeField] private ShowcaseParticipantDefinition researcherB;
        [SerializeField] private Transform turret;
        [SerializeField] private Renderer accessIndicator;

        [Header("Authored Materials")]
        [SerializeField] private Material neutralMaterial;
        [SerializeField] private Material userAMaterial;
        [SerializeField] private Material userBMaterial;
        [SerializeField] private Material accentMaterial;

        private QueueManagerCore queue;
        private Coroutine scenarioCoroutine;

        private int requestCount;
        private int grantCount;
        private int releaseCount;
        private int transferCount;
        private int deniedCount;
        private float totalRotation;
        private double totalGrantLatencyMs;
        private string scenarioStatus = "READY";
        private string lastExportPath = string.Empty;

        private GUIStyle panelStyle;
        private GUIStyle cardStyle;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle bodyStyle;
        private GUIStyle mutedStyle;
        private GUIStyle badgeStyle;
        private GUIStyle buttonStyle;
        private GUIStyle secondaryButtonStyle;
        private GUIStyle eventStyle;
        private Texture2D panelTexture;
        private Texture2D cardTexture;
        private Texture2D accentTexture;
        private Texture2D secondaryTexture;
        private Texture2D badgeTexture;

        private bool ScenarioRunning => scenarioCoroutine != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureShowcaseExists()
        {
            if (FindAnyObjectByType<OrchestrationShowcase>() != null)
                return;

            var host = new GameObject("VLab Orchestrator Showcase");
            host.AddComponent<OrchestrationShowcase>();
        }

        private void Start()
        {
            InitializeCore();
            if (!TryUseAuthoredScene())
                CreateFallbackEnvironment();
            AddEvent("Showcase ready. Request access or run the automated scenario.");

            string capturePath = GetCommandLineValue("-captureShowcase");
            if (!string.IsNullOrWhiteSpace(capturePath))
                StartCoroutine(CaptureAndQuit(capturePath));
        }

        private void Update()
        {
            if (ScenarioRunning)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
                RequestAccess(ResearcherA);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                RequestAccess(ResearcherB);
            if (Input.GetKeyDown(KeyCode.Space) && !string.IsNullOrEmpty(CurrentHolder))
                ReleaseAccess(CurrentHolder);

            float direction = 0f;
            if (Input.GetKey(KeyCode.Q))
                direction -= 1f;
            if (Input.GetKey(KeyCode.E))
                direction += 1f;

            if (Mathf.Abs(direction) > 0.01f)
                RotateTurret(direction * 45f * Time.deltaTime);
        }

        private void InitializeCore()
        {
            queue = new QueueManagerCore();
            queue.RegisterInstrument(InstrumentId);
            queue.RegisterUser(ResearcherA);
            queue.RegisterUser(ResearcherB);

            requestStartedAt.Clear();
            requestCount = 0;
            grantCount = 0;
            releaseCount = 0;
            transferCount = 0;
            deniedCount = 0;
            totalRotation = 0f;
            totalGrantLatencyMs = 0d;
            scenarioStatus = "READY";
            lastExportPath = string.Empty;
            eventHistory.Clear();
        }

        public void ConfigureAuthoredScene(
            ShowcaseSharedInstrumentDefinition sharedInstrument,
            ShowcaseParticipantDefinition participantA,
            ShowcaseParticipantDefinition participantB,
            Material neutral,
            Material userA,
            Material userB,
            Material accent)
        {
            instrumentDefinition = sharedInstrument;
            researcherA = participantA;
            researcherB = participantB;
            neutralMaterial = neutral;
            userAMaterial = userA;
            userBMaterial = userB;
            accentMaterial = accent;

            if (instrumentDefinition != null)
            {
                turret = instrumentDefinition.ControlledPart;
                accessIndicator = instrumentDefinition.AccessIndicator;
            }
        }

        private bool TryUseAuthoredScene()
        {
            if (instrumentDefinition == null)
                instrumentDefinition = FindAnyObjectByType<ShowcaseSharedInstrumentDefinition>();

            if (instrumentDefinition != null)
            {
                turret = instrumentDefinition.ControlledPart;
                accessIndicator = instrumentDefinition.AccessIndicator;
            }

            if (researcherA == null || researcherB == null)
            {
                ShowcaseParticipantDefinition[] participants =
                    FindObjectsByType<ShowcaseParticipantDefinition>(FindObjectsSortMode.None);
                researcherA ??= participants.FirstOrDefault(item => item.ParticipantId == ResearcherA);
                researcherB ??= participants.FirstOrDefault(item => item.ParticipantId == ResearcherB);
            }

            bool valid = turret != null &&
                         accessIndicator != null &&
                         neutralMaterial != null &&
                         userAMaterial != null &&
                         userBMaterial != null;

            if (valid)
                UpdateAccessVisual();

            return valid;
        }

        private void CreateFallbackEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#42606A");
            RenderSettings.ambientEquatorColor = Hex("#18333C");
            RenderSettings.ambientGroundColor = Hex("#071319");
            RenderSettings.ambientIntensity = 0.8f;

            var cameraObject = new GameObject("Showcase Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("#07141B");
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(8.8f, 6.4f, -10.8f);
            cameraObject.transform.LookAt(new Vector3(1.5f, 1.4f, 0.8f));

            var keyLightObject = new GameObject("Key Light");
            var keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Hex("#FFF2D2");
            keyLight.intensity = 1.25f;
            keyLightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var rimLightObject = new GameObject("Rim Light");
            var rimLight = rimLightObject.AddComponent<Light>();
            rimLight.type = LightType.Point;
            rimLight.color = Hex("#23B9B1");
            rimLight.intensity = 7f;
            rimLight.range = 12f;
            rimLightObject.transform.position = new Vector3(4f, 4.8f, 2.5f);

            neutralMaterial = CreateMaterial("Instrument Graphite", Hex("#253A42"), 0.25f, 0.65f);
            userAMaterial = CreateMaterial("Researcher A", Hex("#28C2B8"), 0.25f, 0.65f);
            userBMaterial = CreateMaterial("Researcher B", Hex("#FF8C52"), 0.2f, 0.55f);
            accentMaterial = CreateMaterial("Instrument Accent", Hex("#E8C36A"), 0.4f, 0.7f);
            var darkMaterial = CreateMaterial("Floor", Hex("#0D222A"), 0.05f, 0.45f);
            var paleMaterial = CreateMaterial("Instrument Ceramic", Hex("#CAD9D5"), 0.1f, 0.45f);

            CreatePrimitive("Floor", PrimitiveType.Cube, null, new Vector3(1.5f, -0.2f, 0.8f),
                new Vector3(12f, 0.25f, 8f), darkMaterial);
            CreatePrimitive("Rear Panel", PrimitiveType.Cube, null, new Vector3(1.5f, 2.6f, 4.35f),
                new Vector3(12f, 5.5f, 0.2f), darkMaterial);

            var instrumentRoot = new GameObject("Generic Shared Instrument").transform;
            instrumentRoot.position = new Vector3(1.7f, 0f, 0.8f);

            CreatePrimitive("Base", PrimitiveType.Cube, instrumentRoot, new Vector3(0f, 0.2f, 0f),
                new Vector3(3.3f, 0.4f, 2.3f), neutralMaterial);
            CreatePrimitive("Foot", PrimitiveType.Cylinder, instrumentRoot, new Vector3(0f, 0.45f, 0f),
                new Vector3(1.15f, 0.18f, 1.15f), paleMaterial);
            CreatePrimitive("Column", PrimitiveType.Cylinder, instrumentRoot, new Vector3(0f, 1.3f, 0.5f),
                new Vector3(0.48f, 1.4f, 0.48f), neutralMaterial);
            CreatePrimitive("Bridge", PrimitiveType.Cube, instrumentRoot, new Vector3(0f, 2.35f, 0.05f),
                new Vector3(2.15f, 0.28f, 0.5f), neutralMaterial);
            CreatePrimitive("Work Platform", PrimitiveType.Cube, instrumentRoot, new Vector3(0f, 1.15f, -0.25f),
                new Vector3(2.3f, 0.16f, 1.65f), paleMaterial);

            turret = CreatePrimitive("Controlled Turret", PrimitiveType.Cylinder, instrumentRoot,
                new Vector3(0f, 2.05f, -0.25f), new Vector3(0.9f, 0.2f, 0.9f), accentMaterial).transform;

            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var arm = CreatePrimitive($"Turret Arm {i + 1}", PrimitiveType.Cube, turret,
                    Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, -0.45f, 0.72f),
                    new Vector3(0.16f, 0.75f, 0.16f), i % 2 == 0 ? userAMaterial : userBMaterial);
                arm.transform.localRotation = Quaternion.Euler(18f, angle, 0f);
            }

            accessIndicator = CreatePrimitive("Access Indicator", PrimitiveType.Sphere, instrumentRoot,
                new Vector3(0f, 2.85f, 0.5f), Vector3.one * 0.28f, neutralMaterial)
                .GetComponent<Renderer>();

            CreateResearcherStation("Researcher A Station", new Vector3(-2.3f, 0f, 0.8f), userAMaterial);
            CreateResearcherStation("Researcher B Station", new Vector3(5.7f, 0f, 0.8f), userBMaterial);
            UpdateAccessVisual();
        }

        private void CreateResearcherStation(string name, Vector3 position, Material material)
        {
            var root = new GameObject(name).transform;
            root.position = position;
            CreatePrimitive("Body", PrimitiveType.Capsule, root, new Vector3(0f, 0.85f, 0f),
                new Vector3(0.55f, 0.8f, 0.55f), material);
            CreatePrimitive("Head", PrimitiveType.Sphere, root, new Vector3(0f, 1.95f, 0f),
                Vector3.one * 0.65f, material);
            CreatePrimitive("Station Ring", PrimitiveType.Cylinder, root, new Vector3(0f, 0.08f, 0f),
                new Vector3(0.85f, 0.08f, 0.85f), material);
        }

        private GameObject CreatePrimitive(
            string objectName,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var created = GameObject.CreatePrimitive(primitiveType);
            created.name = objectName;
            created.transform.SetParent(parent, false);
            created.transform.localPosition = localPosition;
            created.transform.localScale = localScale;
            created.GetComponent<Renderer>().sharedMaterial = material;

            var collider = created.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            return created;
        }

        private Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Shader shader = Resources.Load<Shader>("ShowcaseSurface");
            if (shader == null)
                shader = Shader.Find("VLab Orchestrator/Showcase Surface");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("The bundled showcase shader could not be loaded.");

            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private string CurrentHolder =>
            queue?.GetInstrumentState(InstrumentId)?.CurrentHolderUserId ?? string.Empty;

        private void RequestAccess(string userId)
        {
            if (ScenarioRunning && scenarioStatus != "RUNNING")
                return;

            requestCount++;
            requestStartedAt[userId] = Time.realtimeSinceStartupAsDouble;
            QueueDecision decision = queue.RequestAccess(userId, InstrumentId);
            HandleDecision(decision, userId, "request");
            UpdateAccessVisual();
        }

        private void ReleaseAccess(string userId)
        {
            QueueDecision decision = queue.ReleaseAccess(userId, InstrumentId);
            if (decision.Type != QueueDecisionType.Denied && decision.Type != QueueDecisionType.Invalid)
                releaseCount++;

            HandleDecision(decision, userId, "release");
            UpdateAccessVisual();
        }

        private void HandleDecision(QueueDecision decision, string actor, string operation)
        {
            if (decision == null)
                return;

            switch (decision.Type)
            {
                case QueueDecisionType.Granted:
                    RegisterGrant(decision.UserId);
                    AddEvent($"{DisplayName(decision.UserId)} granted access.");
                    break;
                case QueueDecisionType.Queued:
                    AddEvent($"{DisplayName(decision.UserId)} queued at position {decision.QueuePosition}.");
                    break;
                case QueueDecisionType.ReleasedAndGrantedNext:
                    transferCount++;
                    RegisterGrant(decision.UserId);
                    AddEvent($"{DisplayName(decision.SecondaryUserId)} released; access transferred to {DisplayName(decision.UserId)}.");
                    break;
                case QueueDecisionType.ReleasedWithoutNext:
                    AddEvent($"{DisplayName(actor)} released; instrument is available.");
                    break;
                case QueueDecisionType.Denied:
                case QueueDecisionType.Invalid:
                    deniedCount++;
                    AddEvent($"{DisplayName(actor)} {operation} denied: {decision.Reason}");
                    break;
                default:
                    AddEvent($"{operation}: {decision.Type}");
                    break;
            }
        }

        private void RegisterGrant(string userId)
        {
            grantCount++;
            if (!requestStartedAt.TryGetValue(userId, out double startedAt))
                return;

            totalGrantLatencyMs += (Time.realtimeSinceStartupAsDouble - startedAt) * 1000d;
            requestStartedAt.Remove(userId);
        }

        private void RotateTurret(float degrees)
        {
            if (turret == null || string.IsNullOrEmpty(CurrentHolder))
                return;

            turret.Rotate(Vector3.up, degrees, Space.Self);
            totalRotation += Mathf.Abs(degrees);
            queue.RegisterInteraction(CurrentHolder, InstrumentId);
        }

        private void UpdateAccessVisual()
        {
            if (accessIndicator == null)
                return;

            accessIndicator.sharedMaterial = CurrentHolder switch
            {
                ResearcherA => userAMaterial,
                ResearcherB => userBMaterial,
                _ => neutralMaterial
            };
        }

        private void StartAutomatedScenario()
        {
            if (ScenarioRunning)
                return;

            InitializeCore();
            UpdateAccessVisual();
            scenarioCoroutine = StartCoroutine(RunAutomatedScenario());
        }

        private IEnumerator RunAutomatedScenario()
        {
            scenarioStatus = "WARMUP";
            AddEvent("Automated three-cycle scenario started.");
            yield return new WaitForSeconds(1f);

            scenarioStatus = "RUNNING";
            RequestAccess(ResearcherA);
            yield return new WaitForSeconds(0.5f);

            float[] rotations = { 30f, -45f, 60f };
            for (int cycle = 0; cycle < rotations.Length; cycle++)
            {
                string holder = CurrentHolder;
                string next = holder == ResearcherA ? ResearcherB : ResearcherA;
                RequestAccess(next);
                AddEvent($"Cycle {cycle + 1}: {DisplayName(holder)} manipulating controlled turret.");

                float elapsed = 0f;
                const float duration = 1.5f;
                while (elapsed < duration)
                {
                    float step = rotations[cycle] * Time.deltaTime / duration;
                    RotateTurret(step);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                yield return new WaitForSeconds(0.4f);
                ReleaseAccess(holder);
                yield return new WaitForSeconds(0.65f);
            }

            if (!string.IsNullOrEmpty(CurrentHolder))
                ReleaseAccess(CurrentHolder);

            InstrumentStateModel state = queue.GetInstrumentState(InstrumentId);
            bool passed = transferCount == 3 &&
                          string.IsNullOrEmpty(state.CurrentHolderUserId) &&
                          state.WaitingQueue.Count == 0;

            scenarioStatus = passed ? "COMPLETE - SAFE TO EXPORT" : "FAILED - REVIEW EVENTS";
            AddEvent(passed
                ? "Scenario complete: three transfers, clean final release."
                : "Scenario outcome did not satisfy the conformance checks.");
            scenarioCoroutine = null;
        }

        private void ExportCsv()
        {
            string directory = Path.Combine(Application.persistentDataPath, "VLabOrchestratorShowcase");
            Directory.CreateDirectory(directory);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, $"showcase_run_{stamp}.csv");
            var csv = new StringBuilder();
            csv.AppendLine("metric,value");
            csv.AppendLine($"timestamp_utc,{Csv(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))}");
            csv.AppendLine($"scenario_status,{Csv(scenarioStatus)}");
            csv.AppendLine($"policy,{Csv(queue.GetCurrentPolicyName())}");
            csv.AppendLine($"requests,{requestCount}");
            csv.AppendLine($"grants,{grantCount}");
            csv.AppendLine($"releases,{releaseCount}");
            csv.AppendLine($"authority_transfers,{transferCount}");
            csv.AppendLine($"denied_operations,{deniedCount}");
            csv.AppendLine($"total_controlled_rotation_degrees,{totalRotation.ToString("F3", CultureInfo.InvariantCulture)}");
            csv.AppendLine($"mean_grant_latency_ms,{MeanGrantLatencyMs.ToString("F3", CultureInfo.InvariantCulture)}");
            csv.AppendLine($"final_holder,{Csv(CurrentHolder)}");
            csv.AppendLine($"final_queue_count,{queue.GetInstrumentState(InstrumentId).WaitingQueue.Count}");
            csv.AppendLine();
            csv.AppendLine("event_index,event");
            for (int i = 0; i < eventHistory.Count; i++)
                csv.AppendLine($"{i + 1},{Csv(eventHistory[i])}");

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            lastExportPath = path;
            AddEvent($"CSV exported: {Path.GetFileName(path)}");
        }

        private IEnumerator CaptureAndQuit(string capturePath)
        {
            string fullPath = Path.GetFullPath(capturePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(fullPath);

            float timeoutAt = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(fullPath) && Time.realtimeSinceStartup < timeoutAt)
                yield return null;
            yield return new WaitForSeconds(0.75f);

            Debug.Log($"[VLab Orchestrator] Showcase capture written to {fullPath}");
            Application.Quit();
        }

        private double MeanGrantLatencyMs => grantCount > 0 ? totalGrantLatencyMs / grantCount : 0d;

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }

        private void AddEvent(string message)
        {
            eventHistory.Add($"{DateTime.Now:HH:mm:ss}  {message}");
            if (eventHistory.Count > 14)
                eventHistory.RemoveAt(0);
        }

        private static string DisplayName(string userId) =>
            userId switch
            {
                ResearcherA => "Researcher A",
                ResearcherB => "Researcher B",
                _ => string.IsNullOrWhiteSpace(userId) ? "No user" : userId
            };

        private static string GetCommandLineValue(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], key, StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1];
            }

            return string.Empty;
        }

        private void OnGUI()
        {
            EnsureGuiStyles();

            const float referenceWidth = 1440f;
            const float referenceHeight = 900f;
            float scale = Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight);
            scale = Mathf.Max(scale, 0.55f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float width = Screen.width / scale;
            GUI.Box(new Rect(0f, 0f, width, 82f), GUIContent.none, panelStyle);
            GUI.Label(new Rect(28f, 14f, 700f, 36f), "VLAB ORCHESTRATOR", titleStyle);
            GUI.Label(new Rect(30f, 49f, 700f, 22f),
                "BACKEND-NEUTRAL PUBLICATION SHOWCASE  /  FCFS ACCESS CORE", subtitleStyle);

            GUI.Box(new Rect(width - 310f, 20f, 280f, 42f), GUIContent.none, badgeStyle);
            GUI.Label(new Rect(width - 296f, 30f, 252f, 22f), scenarioStatus, sectionStyle);

            DrawControlPanel(new Rect(24f, 104f, 430f, referenceHeight - 128f));
            DrawMetricsPanel(new Rect(width - 352f, 104f, 328f, 270f));
            DrawHintPanel(new Rect(width - 352f, referenceHeight - 178f, 328f, 154f));
        }

        private void DrawControlPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle);
            float x = rect.x + 22f;
            float y = rect.y + 18f;
            float contentWidth = rect.width - 44f;

            GUI.Label(new Rect(x, y, contentWidth, 26f), "SHARED INSTRUMENT SESSION", sectionStyle);
            y += 36f;
            GUI.Label(new Rect(x, y, contentWidth, 40f),
                $"Holder  {DisplayName(CurrentHolder)}\nQueue   {QueueText()}", bodyStyle);
            y += 56f;

            y = DrawUserCard(x, y, contentWidth, ResearcherA, "01", Hex("#28C2B8"));
            y += 10f;
            y = DrawUserCard(x, y, contentWidth, ResearcherB, "02", Hex("#FF8C52"));
            y += 20f;

            GUI.Label(new Rect(x, y, contentWidth, 24f), "CONTROLLED CHILD OBJECT", sectionStyle);
            y += 32f;
            GUI.enabled = !ScenarioRunning && !string.IsNullOrEmpty(CurrentHolder);
            if (GUI.Button(new Rect(x, y, 116f, 40f), "ROTATE -15", secondaryButtonStyle))
                RotateTurret(-15f);
            if (GUI.Button(new Rect(x + 126f, y, 116f, 40f), "ROTATE +15", secondaryButtonStyle))
                RotateTurret(15f);
            GUI.enabled = true;
            GUI.Label(new Rect(x + 252f, y + 10f, 130f, 24f), "Q / E keys", mutedStyle);
            y += 56f;

            GUI.enabled = !ScenarioRunning;
            if (GUI.Button(new Rect(x, y, contentWidth, 46f),
                    ScenarioRunning ? "SCENARIO RUNNING" : "RUN 3-CYCLE SCENARIO", buttonStyle))
                StartAutomatedScenario();
            GUI.enabled = true;
            y += 58f;

            if (GUI.Button(new Rect(x, y, contentWidth, 40f), "EXPORT DEMO CSV", secondaryButtonStyle))
                ExportCsv();
            y += 54f;

            GUI.Label(new Rect(x, y, contentWidth, 24f), "EVENT TRACE", sectionStyle);
            y += 28f;
            string events = eventHistory.Count == 0 ? "No events recorded." : string.Join("\n", eventHistory);
            GUI.Label(new Rect(x, y, contentWidth, rect.yMax - y - 18f), events, eventStyle);
        }

        private float DrawUserCard(float x, float y, float width, string userId, string index, Color color)
        {
            GUI.Box(new Rect(x, y, width, 92f), GUIContent.none, cardStyle);
            GUI.Label(new Rect(x + 14f, y + 12f, 42f, 24f), index, sectionStyle);

            var nameStyle = new GUIStyle(bodyStyle) { normal = { textColor = color }, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(x + 58f, y + 10f, 180f, 24f), DisplayName(userId), nameStyle);

            UserStateModel state = queue.GetUserState(userId);
            string status = CurrentHolder == userId
                ? "ACTIVE HOLDER"
                : state.IsWaiting ? $"WAITING #{queue.GetQueuePosition(userId, InstrumentId)}" : "AVAILABLE";
            GUI.Label(new Rect(x + 58f, y + 38f, 180f, 22f), status, mutedStyle);

            GUI.enabled = !ScenarioRunning;
            if (GUI.Button(new Rect(x + width - 140f, y + 10f, 124f, 31f), "REQUEST", secondaryButtonStyle))
                RequestAccess(userId);
            GUI.enabled = !ScenarioRunning && CurrentHolder == userId;
            if (GUI.Button(new Rect(x + width - 140f, y + 50f, 124f, 31f), "RELEASE", secondaryButtonStyle))
                ReleaseAccess(userId);
            GUI.enabled = true;
            return y + 92f;
        }

        private void DrawMetricsPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle);
            float x = rect.x + 20f;
            float y = rect.y + 18f;
            GUI.Label(new Rect(x, y, rect.width - 40f, 26f), "LIVE EVIDENCE", sectionStyle);
            y += 39f;

            DrawMetric(x, y, "REQUESTS", requestCount.ToString(CultureInfo.InvariantCulture));
            DrawMetric(x + 145f, y, "GRANTS", grantCount.ToString(CultureInfo.InvariantCulture));
            y += 62f;
            DrawMetric(x, y, "TRANSFERS", transferCount.ToString(CultureInfo.InvariantCulture));
            DrawMetric(x + 145f, y, "ROTATION", $"{totalRotation:F1} deg");
            y += 62f;
            DrawMetric(x, y, "MEAN GRANT", $"{MeanGrantLatencyMs:F1} ms");
            DrawMetric(x + 145f, y, "POLICY", "FCFS");
            y += 64f;

            GUI.Label(new Rect(x, y, rect.width - 40f, 40f),
                "The local showcase validates orchestration behavior, not transport performance.", mutedStyle);
        }

        private void DrawMetric(float x, float y, string label, string value)
        {
            GUI.Label(new Rect(x, y, 130f, 20f), label, mutedStyle);
            GUI.Label(new Rect(x, y + 20f, 135f, 30f), value, sectionStyle);
        }

        private void DrawHintPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle);
            float x = rect.x + 20f;
            float y = rect.y + 16f;
            GUI.Label(new Rect(x, y, rect.width - 40f, 24f), "QUICK CONTROLS", sectionStyle);
            GUI.Label(new Rect(x, y + 34f, rect.width - 40f, 70f),
                "1 / 2   Request access\nQ / E   Rotate while holding\nSpace   Release current holder", bodyStyle);

            if (!string.IsNullOrEmpty(lastExportPath))
                GUI.Label(new Rect(x, y + 103f, rect.width - 40f, 34f),
                    $"Last export: {Path.GetFileName(lastExportPath)}", mutedStyle);
        }

        private string QueueText()
        {
            InstrumentStateModel state = queue?.GetInstrumentState(InstrumentId);
            return state == null || state.WaitingQueue.Count == 0
                ? "Empty"
                : string.Join(" -> ", state.WaitingQueue.Select(DisplayName));
        }

        private void EnsureGuiStyles()
        {
            if (panelStyle != null)
                return;

            panelTexture = MakeTexture(Hex("#102831E8"));
            cardTexture = MakeTexture(Hex("#17343DEB"));
            accentTexture = MakeTexture(Hex("#DDAF4FFF"));
            secondaryTexture = MakeTexture(Hex("#24444EFF"));
            badgeTexture = MakeTexture(Hex("#123A3CFF"));

            panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture } };
            cardStyle = new GUIStyle(GUI.skin.box) { normal = { background = cardTexture } };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 27,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Hex("#F4F1E7") }
            };
            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Hex("#7EBDB8") }
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Hex("#F4F1E7") }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Hex("#D4E0DD") }
            };
            mutedStyle = new GUIStyle(bodyStyle)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = Hex("#89A7A6") }
            };
            badgeStyle = new GUIStyle(GUI.skin.box) { normal = { background = badgeTexture } };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { background = accentTexture, textColor = Hex("#102027") },
                hover = { background = accentTexture, textColor = Hex("#102027") },
                active = { background = secondaryTexture, textColor = Color.white }
            };
            secondaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { background = secondaryTexture, textColor = Hex("#E4ECE9") },
                hover = { background = badgeTexture, textColor = Color.white },
                active = { background = accentTexture, textColor = Hex("#102027") }
            };
            eventStyle = new GUIStyle(mutedStyle)
            {
                fontSize = 10,
                clipping = TextClipping.Clip
            };
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.white;
        }
    }
}
