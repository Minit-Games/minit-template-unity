using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MinitTemplate.Editor
{
    /// <summary>
    /// A diagnostic WebGL build: identical to <b>Minit → Build for Minit</b>
    /// except that exceptions are ON.
    ///
    /// The shipping build sets WebGLExceptionSupport.None for size, and with it
    /// a C# exception cannot be handled — IL2CPP calls abort() and the console
    /// shows only "RuntimeError: unreachable / Halting program", with no message
    /// and no stack. That makes an ordinary NullReferenceException look like an
    /// engine failure. Build this instead when a game boots to a blank screen.
    /// </summary>
    public static class MinitDebugBuild
    {
        [MenuItem("Minit Template/Build (debug, exceptions on)")]
        public static void Run()
        {
            const string outputDir = "Build/MinitWebGLDebug";
            Directory.CreateDirectory(outputDir);

            PlayerSettings.WebGL.template = "PROJECT:Minit";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.runInBackground = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = Path.Combine(outputDir, "index.html"),
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.Development,
            });
            Debug.Log("[MinitTemplate] Debug build: " + report.summary.result);
        }
    }
}
