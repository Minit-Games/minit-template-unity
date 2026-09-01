using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MinitTemplate.Editor
{
    /// <summary>
    /// Creates the game scene and applies the project settings the Minit
    /// platform needs. Run once via <b>Minit Template → Set Up Project</b>, or
    /// headlessly with
    /// <c>-executeMethod MinitTemplate.Editor.MinitTemplateSetup.Run</c>.
    ///
    /// The scene it produces is a normal asset — edit it in the Editor
    /// afterwards. This exists so a fresh clone can be set up reproducibly, and
    /// so the whole project can be rebuilt from source with no manual steps.
    /// </summary>
    public static class MinitTemplateSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Minit Template/Set Up Project")]
        public static void Run()
        {
            // The game builds its own view in code onto a Screen Space Overlay
            // canvas, so the scene needs only the one object.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // An overlay canvas renders without a camera, but a camera keeps the
            // Game view sane and gives the frame a background colour.
            //
            // The AudioListener is NOT optional and is easy to lose when the
            // camera is created in code rather than from Unity's default
            // GameObject: with no listener in the scene Unity produces no audio
            // at all, in every environment, while AudioSource.isPlaying still
            // reports true. There is no error -- just silence.
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.56f, 0.83f, 0.96f);
            camGo.tag = "MainCamera";

            new GameObject("Game", typeof(BouncyBall));

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Minit Games";
            PlayerSettings.productName = "Bouncy Ball";
            // Portrait only: the platform requires it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            // MinitBuild sets this too; setting it here means the Editor's own
            // Build Settings dialog is already correct.
            PlayerSettings.WebGL.template = "PROJECT:Minit";

            // The Unity splash delays first play and shows Unity branding inside
            // the Minit feed. Disabling it requires a Pro/Plus seat; on Personal
            // this assignment is ignored, which the log below reports honestly
            // rather than pretending it worked.
            PlayerSettings.SplashScreen.show = false;
            Debug.Log("[MinitTemplate] Unity splash after request: " +
                      (PlayerSettings.SplashScreen.show ? "STILL ON (Personal seat - cannot be disabled)" : "off"));

            // Image.Type.Sliced reads the SPRITE's border, not the Image's --
            // without this the whole sprite is stretched and the rounded corners
            // distort. Must match the corner region drawn by tools/gen-art.mjs.
            const string buttonPath = "Assets/Resources/Art/button.png";
            if (AssetImporter.GetAtPath(buttonPath) is TextureImporter button)
            {
                button.textureType = TextureImporterType.Sprite;
                // Single, not Multiple: the importer's spriteBorder is only
                // applied to the auto-generated sprite in Single mode. In
                // Multiple mode the border is silently ignored and the sprite
                // stretches -- the value is still written into the .meta, so
                // the setting LOOKS right while doing nothing.
                button.spriteImportMode = SpriteImportMode.Single;
                button.spriteBorder = new Vector4(24, 24, 24, 24);
                button.SaveAndReimport();
                Debug.Log("[MinitTemplate] button 9-slice border set to 24");
            }

            // Audio must be PRELOADED and decompressed on load. The importer
            // default here is preloadAudioData: 0, which leaves every clip in
            // state=Unloaded -- AudioSource.Play() then produces silence with no
            // error, which is indistinguishable from the platform muting the
            // game. On WebGL there is no streaming to fall back on either.
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }))
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(clipPath) is not AudioImporter clip) continue;
                AudioImporterSampleSettings settings = clip.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                // Per-platform since Unity 2022; the old AudioImporter.preloadAudioData
                // is obsolete.
                settings.preloadAudioData = true;
                clip.defaultSampleSettings = settings;
                clip.loadInBackground = false;
                clip.forceToMono = true;
                clip.SaveAndReimport();
            }
            Debug.Log("[MinitTemplate] audio clips set to preload + decompress on load");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MinitTemplate] Project set up: scene at " + ScenePath);
        }
    }
}
