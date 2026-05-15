using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YtDlp.Editor
{
    /// <summary>
    /// Stages the Python stdlib zip for the active build target into
    /// StreamingAssets/dlp/stdlib/ before each player build.
    ///
    /// Skips if the zip already exists. Aborts the build if Python cannot
    /// be found or the script fails. Set DLP_PYTHON_HOME to override the
    /// Python prefix (otherwise uv python find 3.12 is used).
    ///
    /// Android and iOS stdlib zips must be staged by CI — this preprocessor
    /// only handles the three desktop targets automatically.
    /// </summary>
    public sealed class DlpBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var platformId = ToPlatformId(report.summary.platform);
            if (platformId == null)
            {
                Debug.LogWarning(
                    $"[YtDlp] {report.summary.platform} stdlib must be staged by CI — " +
                    "auto-staging only supports Windows/macOS/Linux desktop builds.");
                return;
            }

            var pkg = PackageInfo.FindForAssembly(typeof(DlpBuildPreprocessor).Assembly);
            if (pkg == null)
                throw new BuildFailedException("[YtDlp] Cannot find YtDlp package path.");

            var outZip = Path.Combine(
                pkg.resolvedPath, "StreamingAssets", "dlp", "stdlib", platformId + ".zip");

            if (File.Exists(outZip))
            {
                Debug.Log($"[YtDlp] stdlib/{platformId}.zip already present — skipping.");
                return;
            }

            var python = FindPython();
            if (python == null)
                throw new BuildFailedException(
                    "[YtDlp] Python 3.x not found. " +
                    "Set DLP_PYTHON_HOME to your Python prefix, or run: uv python install 3.12");

            Debug.Log($"[YtDlp] Staging stdlib/{platformId}.zip using {python} …");
            RunStageScript(python, platformId, outZip);

            if (!File.Exists(outZip))
                throw new BuildFailedException(
                    $"[YtDlp] Stdlib staging failed — {outZip} was not created.");

            AssetDatabase.Refresh();
            Debug.Log($"[YtDlp] Staged → {outZip}");
        }

        // ── Also expose as a menu item for manual / on-demand staging ─────────

        [MenuItem("Tools/YtDlp/Stage stdlib for current platform")]
        public static void StageManual()
        {
            var platformId = ToPlatformId(EditorUserBuildSettings.activeBuildTarget);
            if (platformId == null)
            {
                Debug.LogWarning("[YtDlp] No auto-staging for the current build target.");
                return;
            }

            var pkg = PackageInfo.FindForAssembly(typeof(DlpBuildPreprocessor).Assembly);
            if (pkg == null) { Debug.LogError("[YtDlp] Cannot find package path."); return; }

            var outZip = Path.Combine(
                pkg.resolvedPath, "StreamingAssets", "dlp", "stdlib", platformId + ".zip");

            var python = FindPython();
            if (python == null) { Debug.LogError("[YtDlp] Python not found."); return; }

            RunStageScript(python, platformId, outZip);

            if (File.Exists(outZip))
            {
                AssetDatabase.Refresh();
                Debug.Log($"[YtDlp] Staged → {outZip}");
            }
            else
            {
                Debug.LogError($"[YtDlp] Staging failed — {outZip} not created.");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ToPlatformId(BuildTarget t) => t switch
        {
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "windows-x86_64",
            BuildTarget.StandaloneOSX     => "macos-universal",
            BuildTarget.StandaloneLinux64 => "linux-x86_64",
            _                             => null,
        };

        private static string FindPython()
        {
            // 1. DLP_PYTHON_HOME is sys.prefix; derive the executable from it
            var home = System.Environment.GetEnvironmentVariable("DLP_PYTHON_HOME");
            if (!string.IsNullOrEmpty(home))
            {
                foreach (var rel in new[] { "python.exe", "bin/python3", "bin/python" })
                {
                    var p = Path.Combine(home, rel);
                    if (File.Exists(p)) return p;
                }
            }

            // 2. uv python find 3.12
            var uv = Exec("uv", "python find 3.12");
            if (!string.IsNullOrEmpty(uv) && File.Exists(uv)) return uv;

            return null;
        }

        private static void RunStageScript(string python, string platformId, string outZip)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outZip)!);

            // Write the staging logic to a temp file — avoids quoting issues.
            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".py");
            try
            {
                File.WriteAllText(tmp, BuildScript(platformId, outZip));
                var result = Exec(python, $"\"{tmp}\"");
                if (!string.IsNullOrEmpty(result))
                    Debug.Log($"[YtDlp] {result}");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static string BuildScript(string platformId, string outZip)
        {
            // Escape backslashes for the Python string literal
            var escapedOut = outZip.Replace("\\", "\\\\");
            return
$@"import zipfile, os, sys

prefix = sys.prefix
out    = r'{escapedOut}'
os.makedirs(os.path.dirname(out), exist_ok=True)

exclude = {{'__pycache__', 'test', 'ensurepip'}}

if sys.platform == 'win32':
    bases = ['Lib', 'DLLs']
else:
    lib  = os.path.join(prefix, 'lib')
    dirs = sorted(d for d in os.listdir(lib) if d.startswith('python3.'))
    bases = [os.path.join('lib', dirs[-1])] if dirs else []

total = 0
with zipfile.ZipFile(out, 'w', zipfile.ZIP_STORED) as z:
    for base in bases:
        bd = os.path.join(prefix, base)
        if not os.path.isdir(bd):
            continue
        for root, dirs, files in os.walk(bd):
            dirs[:] = [d for d in dirs if d not in exclude]
            for f in files:
                full = os.path.join(root, f)
                arc  = os.path.relpath(full, prefix).replace(os.sep, '/')
                z.write(full, arc)
                total += 1

mb = os.path.getsize(out) / 1_048_576
print(f'Staged {{total}} files from {{prefix}} ({{mb:.1f}} MB) -> {{out}}')
";
        }

        private static string Exec(string exe, string args)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = exe,
                    Arguments              = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                });
                var stdout = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(60_000);
                return p.ExitCode == 0 ? stdout : null;
            }
            catch { return null; }
        }
    }
}
