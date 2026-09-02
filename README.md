# Unity Minit Template

> **Learn page:** [Unity on Minit](https://minit.studio/docs/unity) — the official guide this template implements.

A complete, working Minit game in Unity 6, kept deliberately small so the parts
around it are easy to read. Tap the ball to bounce it; each tap scores. A
30 second clock ends the run and reports the result.

Start here, replace the game, keep the plumbing. There are matching
`minit-template-defold` and `minit-template-godot` templates with the same game.

```bash
tools/package.sh                # regenerate assets, build, verify, zip for upload
tools/play.mjs <dir>            # run a build in a real browser and screenshot it
```

In the Editor the build is **Minit → Build for Minit**, which applies every
required Player setting itself. `tools/package.sh` calls the same entry point
headlessly and then adds `meta.json` and the notices to the archive, which that
menu item does not.

## Cloning

**The SDK is a private UPM package.** `Packages/manifest.json` points at

```
git@github.com:Minit-Games/minit-unity.git?path=/Packages/games.minit.unity
```

The docs give the HTTPS form; this uses **SSH** because the repo is private and
HTTPS has no non-interactive credential path. You need an SSH key with access,
or Unity cannot resolve the package and nothing will compile.

This repo also uses **Git LFS** for art, audio and fonts — run `git lfs install`
before cloning, or those files arrive as text pointers.

## What the game shows you

| Where | What it demonstrates |
|---|---|
| `Assets/Scripts/BouncyBall.cs` → `ReadConfig()` | Config values, with defaults and clamping. Booleans follow the backend's coercion: anything but `"true"` is false. |
| `BouncyBall.cs` → `Start()` | `Minit.LoadingDone()` at the end, once the view is built. |
| `BouncyBall.cs` → `Finish()` | `Minit.ReportResult()` **exactly once**, with flavour text, a persisted `userData` and a `delay` so the outro is seen. |
| `BouncyBall.cs` → `Start()` | `Minit.GetUserData()` read back as the player's previous best. |
| `Assets/WebGLTemplates/Minit/index.html` | The audio fixes. Read this before touching sound. |
| `BouncyBall.cs` → `Layout()` | Everything measured from `Screen.width/height`, every frame it changes. |
| `Assets/Editor/MinitTemplateSetup.cs` | Recreates the scene and applies the project settings, so the project can be rebuilt from source. |

Note the API is **ahead of the published docs**: `ReportResult` takes a
`userData` argument and `GetUserData()` exists (DROP-4077). The docs page still
shows the older signature.

## Read this before you touch audio

Sound is the one thing that behaves differently inside the Minit app than in a
browser, and it fails **silently in both directions**. The stock Minit WebGL
template has no audio handling at all; the copy in this project adds it, and
`tools/package.sh` fails the build if it goes missing.

**The host's volume message never arrives (the root cause).** The app tells a
game its volume with `window.postMessage(payload, window.location.origin)`. On
iOS the game is served from `minitlocal://`, whose origin is **opaque** — so
`location.origin` is the string `"null"` and `postMessage` **throws**. Every
volume message is discarded, and the only value the page sees is the seed baked
in at mount time, which is `0` for a drop mounted before it was scrolled into
view. The game is then healthy and permanently inaudible, and only a foreground
transition fixes it. The template repairs the channel rather than guessing: a
same-window post rejected for its target origin is retried with `'*'`, so the
host's real intent flows through **including a deliberate mute**. Tracked
app-side as DROP-8164.

**A suspended AudioContext** drops whatever is produced, so a loop started too
early is lost rather than delayed. The template resumes on any gesture, on
visibility and focus, and on a watchdog. Unity exposes no handle on its
`AudioContext`, so the template wraps the constructor into a registry at
`window.__minitAudioContexts`; `Assets/Plugins/WebGL/MinitAudioBridge.jslib`
reads that registry, which is how `BouncyBall` knows not to start its music into
a sleeping context.

**The host's mute gain** is re-applied when the host says it wants sound while
the gain is still zero — and never touched when the drop is deliberately muted.

**Nothing plays before the first tap**, which is also the gesture that resumes
the context.

## Layout: no design surface

The whole view is built in code onto a **Screen Space Overlay** canvas with the
scaler at Constant Pixel Size, so one canvas unit is one screen pixel and
`Layout()` reads straight from `Screen.width/height`. That is deliberate: the
Minit app's game slot is roughly **2:3**, far wider relative to its height than
a phone screen, because the game sits between the app's header and its toolbar.
Anything pinned to a fixed design width paints a fraction of that slot and
leaves a band down one side, and you cannot see it in a desktop browser at a
phone viewport.

## Build settings that are not optional

`MinitBuild` applies these; they are listed here because breaking one is silent:

- **Multithreading off** — a threaded build needs COOP/COEP headers the host
  does not send.
- **Brotli with decompression fallback off** — the host must serve `*.br` with
  `Content-Encoding: br`. `tools/serve.mjs` does this too, or a local test
  receives raw Brotli bytes and never boots.
- **IL2CPP, high managed stripping, exceptions none** — size.
- **`PROJECT:Minit` WebGL template** — this is what carries the audio fixes.

The Unity splash is disabled in `MinitTemplateSetup` (`SplashScreen.show =
false`). That needs a Pro/Plus seat; on Personal the assignment is ignored and
every game opens with several seconds of Unity branding inside the feed. The
setup script logs which happened rather than assuming.

## A note on size

The bundle is **~9 MB zipped**, nearly all of it Unity's WebGL runtime. That is
over Minit's 5 MB recommendation and under its 50 MB limit. For the same game:
Defold is 1.5 MB, Godot ~10 MB, Unity ~9 MB. The game's own content is under
0.5 MB in every case — the number is the engine.

## Assets

No binary art or audio is authored by hand. `tools/gen-art.mjs` rasterises every
sprite from signed distance fields and `tools/gen-audio.mjs` synthesises the
effects, both into `Assets/Resources/` where the game loads them by name.
Replace either wholesale when you bring your own.

The background music is the one third-party asset: a CC0 chiptune, converted by
`tools/gen-music.mjs` from 2.0 MB of 44.1 kHz stereo to 0.35 MB of 16 kHz mono.
Unity loops it natively via `AudioSource.loop`. That script also checks whether
the loop point is genuinely seamless before "fixing" it; this track's is, so it
is left exactly as the author wrote it. See `THIRD-PARTY-NOTICES.txt`.

## Two asset-import settings that silence or distort the game

Both are import metadata, not code — the game looks correct in the Editor and
fails in the build, with nothing logged either way.

**The scene needs an `AudioListener`.** Unity's default camera GameObject
carries one, but a camera created in code (`new GameObject("Main Camera",
typeof(Camera))`) does not. With no listener anywhere in the scene Unity
produces **no audio at all**, in every environment, while
`AudioSource.isPlaying` still reports `true` and `AudioListener.volume` still
reads 1. There is no error — just silence. `MinitTemplateSetup` adds it
explicitly.

That last detail is worth internalising: `isPlaying == true` is not evidence
that anything is audible. To actually check, splice an `AnalyserNode` into the
page's AudioContext and measure — see Testing below.

**Audio must be preloaded.** The importer default here is
`preloadAudioData: 0`, which leaves every clip in `state=Unloaded`.
`AudioSource.Play()` then produces **silence with no error**, which is
indistinguishable from the platform muting the game — and on WebGL there is no
streaming to fall back on. `MinitTemplateSetup` sets every clip in
`Resources/Audio` to `DecompressOnLoad` + preload.

**A 9-sliced sprite needs Single mode AND a border that fits.** The game no
longer ships a 9-sliced element — the end-game button it was written for is
gone, replaced by the round clock — but the importer settings in
`MinitTemplateSetup.cs` still configure one, and this is what to know the
moment a fork adds a panel or a pill. `Image.Type.Sliced` reads the *sprite's*
border, and two things silently defeat it:

- The importer's `spriteBorder` is only applied in **Single** sprite mode. In
  Multiple mode the value is still written into the `.meta`, so the setting
  looks right while doing nothing and the sprite just stretches.
- The border must be under **half the element's shortest rendered side**, or
  Unity squashes the borders to fit — which looks exactly like the plain
  stretching the 9-slice was meant to avoid. At ~1.6 units tall that meant a
  border of 24 on a 96 px source.

## When the game boots to a blank screen

The shipping build sets `WebGLExceptionSupport.None` for size. With that, a C#
exception **cannot be handled**: IL2CPP calls `abort()` and the browser console
shows only

```
Invoking error handler due to abort("")
Uncaught exception from main loop:
RuntimeError: unreachable
Halting program.
```

No message, no stack, no hint that it was your code. An ordinary
`NullReferenceException` looks exactly like an engine failure.

Use **Minit Template → Build (debug, exceptions on)** — the same build with
`FullWithStacktrace`, minimal stripping and no compression. It prints the real
exception and stack, and it is how the Input System bug below was found in
about a minute after a long time guessing.

## Input: the Input System package, not `UnityEngine.Input`

The Unity 6 2D template ships `com.unity.inputsystem` and sets active input
handling to **the package**. Under that setting every call to the legacy
`UnityEngine.Input` class throws `InvalidOperationException` — and so does
uGUI's legacy `StandaloneInputModule`, on every frame, from inside
`EventSystem.Update`. With exception support off that is an instant silent
abort and the game renders nothing at all.

So `BouncyBall` reads `Touchscreen.current` / `Pointer.current`, and the
EventSystem uses `InputSystemUIInputModule`. If you paste in code that uses
`Input.GetMouseButtonDown` or `Input.touches`, this is what will happen.

## Testing

`agent-browser` is the quickest way to drive a build by hand:

```bash
node tools/serve.mjs Build/MinitWebGL 9901 &
agent-browser open http://localhost:9901/
agent-browser screenshot /tmp/shot.png
```

`tools/play.mjs` also works (CDP, no npm dependencies) but be patient: a 9 MB
IL2CPP build under software WebGL takes around 100 seconds to reach its first
frame, and screenshotting before then blocks on the frame.

Neither captures the console. To read Unity's own logs, copy the build and
inject a `console` hook into the copy's `index.html` — the shipping artifact
stays untouched.

Audio cannot be heard from a headless browser but it **can be measured**, and
measuring is the only trustworthy check — Unity reports `isPlaying == true` for
a source that produces nothing. Copy the build, inject the app's real
`injections/audio.ts` into the copy's `index.html`, splice an `AnalyserNode`
between the resulting `_muteGain` and `_trueDestination`, then tap and read the
peak RMS. A healthy build measures ~0.25; silence is 0.

To reproduce the app's suspended-context start, launch Chrome **without**
`--autoplay-policy=no-user-gesture-required` (`tools/cdp.mjs` takes
`autoplay: false`).
