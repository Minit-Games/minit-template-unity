using System.Runtime.InteropServices;

/// <summary>
/// Asks the page whether its audio context is actually running.
///
/// Unity's WebGL audio produces nothing useful while the page's AudioContext is
/// suspended, so a music loop started too early is lost rather than delayed.
/// The Minit WebGL template keeps a registry of contexts at
/// window.__minitAudioContexts for exactly this question — see
/// Assets/WebGLTemplates/Minit/index.html, which also carries the recovery for
/// the host's mute gain.
/// </summary>
public static class MinitAudioBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int MinitAudioContextRunning();

    public static int ContextRunning() => MinitAudioContextRunning();
#else
    public static int ContextRunning() => 1;
#endif
}
