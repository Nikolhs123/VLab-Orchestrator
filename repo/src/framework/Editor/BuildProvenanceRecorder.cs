using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Thesis.Orchestration.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Thesis.Orchestration.Editor
{
    // Before every player build (after backend authoring), writes Resources/VLabOrchestrator/BuildProvenance.txt,
    // which ExecutionManifestWriter copies into each run export. Source-tree hashes identify the exact canonical
    // framework/adapter source and the generated runtime actually compiled into the player, independently of git.
    public sealed class BuildProvenanceRecorder : IPreprocessBuildWithReport
    {
        private const string ProvenanceAssetPath = "Assets/Resources/VLabOrchestrator/BuildProvenance.txt";
        private const string GeneratedRuntimeRoot = "Assets/GeneratedSoftwareXRuntime";

        public int callbackOrder => 100;

        public void OnPreprocessBuild(BuildReport report)
        {
            string sourceRoot = ResolvePublicationSourceRoot();
            OrchestrationProjectConfig config =
                Resources.Load<OrchestrationProjectConfig>("AIMultiplayerAssistant/OrchestrationProjectConfig");

            var sb = new StringBuilder();
            Line(sb, "built_utc", DateTime.UtcNow.ToString("o"));
            Line(sb, "unity_version", Application.unityVersion);
            // SDK versions as declared by the installed SDK files (assembly versions are not informative).
            Line(sb, "sdk_pun2_version", MatchInFile("Assets/Photon/PhotonUnityNetworking/Code/PhotonNetwork.cs", "PunVersion\\s*=\\s*\"([^\"]+)\""));
            Line(sb, "sdk_fusion_build", MatchInFile("Assets/Photon/Fusion/release_history.txt", "### Build (\\d+)"));
            Line(sb, "sdk_fishnet_version", MatchInFile("Assets/FishNet/Runtime/Managing/NetworkManager.cs", "FISHNET_VERSION\\s*=\\s*\"([^\"]+)\""));
            Line(sb, "sdk_mirror_version", MatchInFile("Assets/Mirror/version.txt", "^\\s*(\\S+)"));
            Line(sb, "sdk_netcode_version",
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.netcode.gameobjects")?.version);
            Line(sb, "build_target", report.summary.platform.ToString());
            Line(sb, "development_build", ((report.summary.options & BuildOptions.Development) != 0).ToString());
            Line(sb, "selected_backend", config != null ? config.backendType.ToString() : "unavailable");
            Line(sb, "protocol_version", config != null ? config.automatedScenarioProtocolVersion : "unavailable");
            Line(sb, "scenario_id", config != null ? config.ResolveAutomatedScenarioId() : "unavailable");
            Line(sb, "canonical_source_root", sourceRoot ?? "unavailable");
            Line(sb, "canonical_framework_sha256",
                sourceRoot != null ? HashSourceTree(Path.Combine(sourceRoot, "framework")) : "unavailable");
            Line(sb, "canonical_adapters_sha256",
                sourceRoot != null ? HashSourceTree(Path.Combine(sourceRoot, "backend-adapters")) : "unavailable");
            Line(sb, "generated_runtime_sha256", HashSourceTree(Path.GetFullPath(GeneratedRuntimeRoot)));
            string gitCommit = sourceRoot != null ? RunGit(sourceRoot, "rev-parse HEAD") : "unavailable";
            if (gitCommit == "unavailable" && sourceRoot != null)
                gitCommit = ReadGitHeadFromFiles(sourceRoot);
            Line(sb, "git_commit", gitCommit);
            string status = sourceRoot != null ? RunGit(sourceRoot, "status --porcelain --untracked-files=no") : "unavailable";
            Line(sb, "git_tracked_changes", status == "unavailable" ? status : (status.Length > 0).ToString());

            Directory.CreateDirectory(Path.GetDirectoryName(ProvenanceAssetPath));
            File.WriteAllText(ProvenanceAssetPath, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ProvenanceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Debug.Log($"[BuildProvenanceRecorder] Wrote {ProvenanceAssetPath}.");
        }

        // SHA-256 over all .cs files (sorted relative paths, LF-normalized contents), so line-ending
        // conversion by git does not change the identity of the source.
        public static string HashSourceTree(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "unavailable";

            using SHA256 sha = SHA256.Create();
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(path => new { path, relative = path.Substring(root.Length).Replace('\\', '/').TrimStart('/') })
                .OrderBy(file => file.relative, StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (var file in files)
            {
                string content = File.ReadAllText(file.path).Replace("\r\n", "\n");
                byte[] fileHash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                builder.Append(file.relative).Append(':').Append(ToHex(fileHash)).Append('\n');
            }

            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static string ResolvePublicationSourceRoot()
        {
            UnityEditor.PackageManager.PackageInfo corePackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.vlab.orchestrator.core");
            if (corePackage == null || string.IsNullOrEmpty(corePackage.resolvedPath))
                return null;

            string root = Path.GetFullPath(Path.Combine(corePackage.resolvedPath, "..", ".."));
            return Directory.Exists(Path.Combine(root, "framework")) ? root : null;
        }

        private static string MatchInFile(string path, string pattern)
        {
            try
            {
                if (!File.Exists(path))
                    return "not installed";
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                    File.ReadAllText(path), pattern, System.Text.RegularExpressions.RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value : "unavailable";
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }

        // Fallback when no git executable is on PATH (e.g. launched from a GUI client): read .git/HEAD directly.
        // Detached HEAD, loose refs and packed-refs are handled; worktrees (.git as a file) are not.
        private static string ReadGitHeadFromFiles(string startDirectory)
        {
            try
            {
                string directory = startDirectory;
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(Path.Combine(directory, ".git")))
                    directory = Path.GetDirectoryName(directory);
                if (string.IsNullOrEmpty(directory))
                    return "unavailable";

                string gitDirectory = Path.Combine(directory, ".git");
                string head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
                if (!head.StartsWith("ref: ", StringComparison.Ordinal))
                    return head;

                string reference = head.Substring(5).Trim();
                string loose = Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose))
                    return File.ReadAllText(loose).Trim();

                string packed = Path.Combine(gitDirectory, "packed-refs");
                if (File.Exists(packed))
                {
                    foreach (string line in File.ReadAllLines(packed))
                    {
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                            return line.Substring(0, line.IndexOf(' '));
                    }
                }

                return "unavailable";
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }

        private static string RunGit(string workingDirectory, string arguments)
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process == null)
                    return "unavailable";
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(10000);
                return process.ExitCode == 0 ? output : "unavailable";
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }

        private static void Line(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append(string.IsNullOrEmpty(value) ? "unavailable" : value.Replace('\n', ' ')).Append('\n');
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
