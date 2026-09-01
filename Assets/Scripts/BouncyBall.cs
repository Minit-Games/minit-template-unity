using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using MinitGames;

/// <summary>
/// Bouncy Ball — the whole game, and a worked example of every Minit SDK call.
///
/// Tap the ball to bounce it; each tap scores. A 30 second clock ends
/// the run and reports the result. That is the entire game: it exists so the
/// lifecycle around it is easy to read.
///
/// WHAT TO COPY FROM HERE
///   * ReadConfig()          — config values, with defaults and clamping
///   * Minit.LoadingDone()   — at the end of Start, once the scene is built
///   * Finish()              — ReportResult exactly once, with flavour text,
///                             persisted userData and a delay for the outro
///   * Layout()              — no design surface; measured from Screen every frame
///
/// The whole view is built in code onto one Screen Space Overlay canvas, so a
/// unit is a screen pixel and the layout can be driven straight from
/// Screen.width/height. That is deliberate: the Minit app's game slot is roughly
/// 2:3, far wider relative to its height than a phone screen, and anything
/// pinned to a fixed design width leaves a band down one side.
/// </summary>
public class BouncyBall : MonoBehaviour
{
    // Motion is expressed in layout units, not pixels, so the ball behaves the
    // same on every viewport.
    const float Gravity = 30f;      // units/s^2
    const float TapImpulse = 13f;   // units/s, upward
    const float BounceDamp = 0.62f; // energy kept on each landing
    const float RestSpeed = 1.4f;   // below this the ball stops rather than jittering
    const int EndDelayMs = 900;     // how long the host waits before its result screen

    int _pointsPerTap = 10;
    bool _soundOn = true, _musicOn = true;

    int _score, _taps, _best = -1;
    bool _finished, _started, _musicStarted;

    /// A drop is one session; the clock ends it, not a button.
    const float RoundSeconds = 30f;
    float _remaining = RoundSeconds;
    int _shownSecond = (int)RoundSeconds;
    float _musicRetry;

    float _vy, _ballY, _squash;

    // Layout, rebuilt from Screen whenever it changes.
    int _w, _h;
    float _unit = 1f, _groundY, _ballX, _ballRestY, _ballR;

    Image _sky, _ground, _shadow, _ball, _puff, _dim;
    readonly Image[] _turf = new Image[20];
    Text _score_, _hint, _timer, _result, _resultLabel, _caption;
    RectTransform _canvasRect;
    AudioSource _tapSfx, _bounceSfx, _finishSfx, _music;
    float _puffAge = -1f;

    void Start()
    {
        ReadConfig();

        // The player's single persisted slot, written by ReportResult last run.
        string stored = Minit.GetUserData();
        if (!string.IsNullOrEmpty(stored) && int.TryParse(stored, out int best)) _best = best;

        BuildView();
        Layout();
        _ballY = _ballRestY;
        RefreshScore();

        // The scene is built and placed, so the host can reveal the game now.
        Minit.LoadingDone();
    }

    void ReadConfig()
    {
        // Values arrive as strings from the post's URL query. Booleans follow the
        // backend's own coercion: anything other than "true" is false.
        _pointsPerTap = int.TryParse(Minit.GetConfigValue("pointsPerTap", "10"), out int p)
            ? Mathf.Clamp(p, 1, 100) : 10;
        _soundOn = Minit.GetConfigValue("sound", "true") != "false";
        _musicOn = Minit.GetConfigValue("music", "true") != "false";
    }

    // ------------------------------------------------------------------ view

    Sprite Art(string name)
    {
        var sprite = Resources.Load<Sprite>("Art/" + name);
        if (sprite == null) Debug.LogWarning($"[BouncyBall] missing sprite Resources/Art/{name}");
        return sprite;
    }

    Image AddImage(Transform parent, string name, string art)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Art(art);
        img.raycastTarget = false;           // only the button takes clicks
        Anchor(go.GetComponent<RectTransform>());
        return img;
    }

    // Everything is anchored bottom-left with a centre pivot, so anchoredPosition
    // is a plain (x, y) in screen pixels with Y pointing up.
    static void Anchor(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    Text AddText(Transform parent, string name, string content, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = content;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.color = Color.white;
        Anchor(go.GetComponent<RectTransform>());
        return t;
    }

    AudioSource AddAudio(string clip, float volume, bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = Resources.Load<AudioClip>("Audio/" + clip);
        Debug.Log($"[BouncyBall] audio '{clip}' -> " +
                  (src.clip == null ? "MISSING" : $"{src.clip.length:0.00}s {src.clip.frequency}Hz " +
                   $"ch{src.clip.channels} state={src.clip.loadState}"));
        src.volume = volume;
        src.loop = loop;                     // Unity loops a clip natively
        src.playOnAwake = false;
        return src;
    }

    void BuildView()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Constant pixel size at scale 1: one canvas unit IS one screen pixel, so
        // the layout below reads straight from Screen.width/height.
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        _canvasRect = canvasGo.GetComponent<RectTransform>();

        // InputSystemUIInputModule, not StandaloneInputModule: this project has
        // active input handling set to the Input System package (the Unity 6 2D
        // template's default), and the legacy module reads UnityEngine.Input,
        // which throws under that setting.
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(InputSystemUIInputModule));
        }

        var font = Resources.Load<Font>("Fonts/TitanOne-Regular");
        if (font == null) Debug.LogWarning("[BouncyBall] missing font Resources/Fonts/TitanOne-Regular");

        _sky = AddImage(_canvasRect, "Sky", "sky");
        _ground = AddImage(_canvasRect, "Ground", "ground");
        for (int i = 0; i < _turf.Length; i++)
        {
            _turf[i] = AddImage(_canvasRect, "Turf" + i, "turf");
            _turf[i].enabled = false;
        }
        _shadow = AddImage(_canvasRect, "Shadow", "shadow");
        _ball = AddImage(_canvasRect, "Ball", "ball");
        _puff = AddImage(_canvasRect, "Puff", "puff");
        _puff.enabled = false;

        _dim = AddImage(_canvasRect, "Dim", "square");
        _dim.color = new Color(0.04f, 0.09f, 0.13f, 0f);
        _dim.enabled = false;

        _score_ = AddText(_canvasRect, "Score", "0", font);
        _timer = AddText(_canvasRect, "Timer", "0:30", font);
        _hint = AddText(_canvasRect, "Hint", "TAP THE BALL", font);
        _resultLabel = AddText(_canvasRect, "ResultLabel", "FINAL SCORE", font);
        _result = AddText(_canvasRect, "Result", "", font);
        _caption = AddText(_canvasRect, "Caption", "", font);
        _resultLabel.enabled = _result.enabled = _caption.enabled = false;

        _tapSfx = AddAudio("tap", 0.55f, false);
        _bounceSfx = AddAudio("bounce", 0.5f, false);
        _finishSfx = AddAudio("finish", 0.8f, false);
        _music = AddAudio("music", 0.45f, true);
    }

    // ---------------------------------------------------------------- layout

    void Layout()
    {
        _w = Screen.width;
        _h = Screen.height;
        float w = _w, h = _h;

        // Tracks the narrow axis but is capped against height, so a very wide
        // slot does not blow the art up past what fits vertically.
        _unit = Mathf.Min(w / 9f, h / 18f);
        _groundY = h * 0.44f;
        _ballR = _unit * 1.15f;
        _ballX = w * 0.5f;
        _ballRestY = _groundY + _ballR;

        Place(_sky, w * 0.5f, h * 0.5f, w, h);
        Place(_ground, w * 0.5f, _groundY * 0.5f, w, _groundY);
        Place(_dim, w * 0.5f, h * 0.5f, w, h);

        // The grass fringe is TILED, not stretched. One sprite scaled to the full
        // width smears the blades into blobs; repeating it at a uniform scale
        // keeps them the same shape on every viewport.
        float tileW = _unit * 1.7f;
        float tileH = 40f * (tileW / 128f);
        int tiles = Mathf.Min(_turf.Length, Mathf.CeilToInt(w / tileW) + 1);
        for (int i = 0; i < _turf.Length; i++)
        {
            bool on = i < tiles;
            _turf[i].enabled = on;
            if (on) Place(_turf[i], (i + 0.5f) * tileW, _groundY + tileH * 0.15f, tileW, tileH);
        }

        // The clock sits under the score, so both read as one HUD block.
        PlaceText(_timer, w * 0.5f, h - Mathf.Min(h * 0.11f, _unit * 2.2f) - _unit * 1.15f,
                  Fit("0:00", w * 0.34f, _unit * 0.8f));
        PlaceText(_hint, w * 0.5f, h - Mathf.Min(h * 0.11f, _unit * 2.2f) - _unit * 2.2f,
                  Fit("TAP THE BALL", w * 0.7f, _unit * 0.5f));
        RefreshScore();
        if (_finished) PlaceResult();
    }

    void Place(Image img, float cx, float cy, float w, float h)
    {
        var rt = img.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(cx, cy);
        rt.sizeDelta = new Vector2(w, h);
    }

    void PlaceText(Text t, float cx, float cy, float px)
    {
        t.fontSize = Mathf.Max(1, Mathf.RoundToInt(px));
        var rt = t.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(cx, cy);
        rt.sizeDelta = new Vector2(_w, px * 1.8f);
    }

    /// Largest font size at which <paramref name="text"/> still fits
    /// <paramref name="maxWidth"/>, capped at <paramref name="maxPx"/>.
    /// Deliberately over-estimates the average advance, so text ends up slightly
    /// smaller than it had to be rather than clipped.
    static float Fit(string text, float maxWidth, float maxPx)
        => Mathf.Min(maxPx, maxWidth / Mathf.Max(1, text.Length) / 0.70f);

    void RefreshScore()
    {
        _score_.text = _score.ToString();
        PlaceText(_score_, _w * 0.5f, _h - Mathf.Min(_h * 0.11f, _unit * 2.2f),
                  Fit(_score.ToString(), _w * 0.6f, _unit * 1.9f));
    }

    // ----------------------------------------------------------------- audio

    void Play(AudioSource src, float pitch = 1f)
    {
        // Nothing plays before the first tap: that tap is also the gesture that
        // lets the browser (and the app's WebView) resume audio.
        if (!_soundOn || !_started) return;
        src.pitch = pitch;
        src.Play();
        Debug.Log($"[BouncyBall] play {src.clip?.name} isPlaying={src.isPlaying} vol={src.volume} " +
                  $"listener={AudioListener.volume} paused={AudioListener.pause}");
    }

    /// Is the page's audio context actually running? Audio produced while it is
    /// suspended is dropped, so a loop started too early loses its opening and is
    /// heard from the middle whenever the context wakes. The WebGL template keeps
    /// a registry of contexts for exactly this question.
    bool AudioContextRunning()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return MinitAudioBridge.ContextRunning() != 0;
#else
        return true;
#endif
    }

    void TryStartMusic(float dt)
    {
        if (_musicStarted || !_musicOn || !_started) return;
        _musicRetry -= dt;
        if (_musicRetry > 0f) return;
        _musicRetry = 0.25f;                 // the check crosses into JS; throttle it
        if (AudioContextRunning()) { _music.Play(); _musicStarted = true; }
    }

    // -------------------------------------------------------------- gameplay

    void Update()
    {
        // The clock gets a looser cap than the physics. Sharing the 0.05 cap
        // makes every slow frame quietly donate time back to the player, so a
        // 30 second round measurably overruns.
        float tick = Mathf.Min(Time.deltaTime, 0.5f);
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (Screen.width != _w || Screen.height != _h) Layout();
        TryStartMusic(dt);
        HandleInput();

        // Counts from the moment the game is interactive, not from the first
        // tap: a run has to end on its own, or a player who never touches the
        // ball never produces a result at all.
        if (!_finished)
        {
            _remaining -= tick;
            int whole = Mathf.Max(0, Mathf.CeilToInt(_remaining));
            if (whole != _shownSecond)
            {
                _shownSecond = whole;
                // Rendered as a clock rather than a bare number: stacked under
                // the score with no label, "24" under "20" reads as a score.
                _timer.text = "0:" + whole.ToString("00");
            }
            if (_remaining <= 0f) Finish();
        }

        // Physics keeps running after the run ends, so the ball settles instead
        // of freezing in mid-air under the result card.
        _vy -= Gravity * _unit * dt;
        _ballY += _vy * dt;
        if (_ballY <= _ballRestY && _vy < 0f)
        {
            float impact = -_vy / _unit;
            _ballY = _ballRestY;
            if (impact > RestSpeed) { _vy = -_vy * BounceDamp; Land(impact); }
            else _vy = 0f;
        }

        // Squash and stretch springs back rather than being tweened, so a tap
        // landing mid-bounce simply replaces it.
        _squash *= Mathf.Max(0f, 1f - dt * 7f);
        float d = _ballR * 2f;
        Place(_ball, _ballX, _ballY, d * (1f + _squash), d * (1f - _squash));

        // The shadow shrinks and fades as the ball climbs.
        float lift = Mathf.Clamp01((_ballY - _ballRestY) / (_unit * 4f));
        float ss = _ballR * 2.1f * Mathf.Max(0.35f, 1f - lift * 0.5f);
        Place(_shadow, _ballX, _groundY + _unit * 0.05f, ss, ss * 0.32f);
        _shadow.color = new Color(1f, 1f, 1f, Mathf.Max(0.15f, 0.75f - lift * 0.45f));

        if (_puffAge >= 0f)
        {
            _puffAge += dt;
            float k = _puffAge / 0.34f;
            if (k >= 1f) { _puff.enabled = false; _puffAge = -1f; }
            else
            {
                float s = _ballR * 1.5f * Mathf.Lerp(0.4f, 1f, k);
                Place(_puff, _ballX, _groundY + _ballR * 0.35f, s, s);
                _puff.color = new Color(1f, 1f, 1f, 0.75f * (1f - k));
            }
        }
    }

    void Land(float impact)
    {
        Play(_bounceSfx, 0.9f + Mathf.Min(0.35f, impact * 0.02f));
        _squash = Mathf.Min(0.34f, 0.1f + impact * 0.016f);
        _puff.enabled = true;
        _puffAge = 0f;
    }

    void HandleInput()
    {
        if (_finished) return;

        // The Input System package, not the legacy UnityEngine.Input class:
        // this project has active input handling set to the package, and the
        // legacy class throws an InvalidOperationException on every call under
        // that setting. With WebGL exception support off that abort is silent
        // and the game renders nothing at all -- see the README.
        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.touches.Count > 0)
        {
            bool any = false;
            // Every finger has its own control, so multi-touch needs no extra
            // handling beyond walking the list.
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.wasPressedThisFrame)
                {
                    any = true;
                    TryHitBall(touch.position.ReadValue());
                }
            }
            if (any) return;
        }

        // Mouse (and anything else pointer-like) in the Editor and on desktop.
        var pointer = Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame)
        {
            TryHitBall(pointer.position.ReadValue());
        }
    }

    void TryHitBall(Vector2 at)
    {
        // Generous hit area: a near miss that reads as a hit is a better failure
        // than a hit that reads as a miss. The button is a real UI Button, so it
        // consumes its own clicks and never reaches here.
        if (Vector2.Distance(at, new Vector2(_ballX, _ballY)) > _ballR * 1.35f) return;

        if (!_started) { _started = true; _hint.enabled = false; }
        _taps++;
        _score += _pointsPerTap;
        RefreshScore();
        _vy = TapImpulse * _unit;
        _squash = -0.2f;                     // stretch upward
        Play(_tapSfx, 0.95f + Mathf.Min(0.5f, _taps * 0.012f));
    }

    // ------------------------------------------------------------------- end

    string Flavour()
    {
        string line = $"Bounced the ball {_taps} time{(_taps == 1 ? "" : "s")}.";
        if (_best >= 0 && _score > _best) line += " A new personal best.";
        return line;
    }

    public void Finish()
    {
        if (_finished) return;               // ReportResult must happen exactly once
        _finished = true;

        if (_musicStarted) _music.Stop();
        Play(_finishSfx);

        _hint.enabled = false;
        _timer.gameObject.SetActive(false);
        _score_.enabled = false;             // the card carries the number now
        _dim.enabled = true;
        _dim.color = new Color(0.04f, 0.09f, 0.13f, 0.72f);
        PlaceResult();

        // The one required call. `delay` holds the host's result screen back long
        // enough for the card to be seen; `userData` is this player's single
        // persisted slot, so the next run can compare against it.
        Minit.ReportResult(_score, Flavour(), EndDelayMs, Mathf.Max(_score, _best).ToString());
    }

    void PlaceResult()
    {
        // Stacked upward from just above where the ball comes to rest, rather
        // than pinned to fractions of the height — fractions drift into the ball
        // on a wide slot, where there is much less sky above the grass line.
        float w = _w;
        string line = Flavour();
        float ballTop = _ballRestY + _ballR;

        float capPx = Fit(line, w * 0.88f, _unit * 0.46f);
        float scorePx = Fit(_score.ToString(), w * 0.8f, _unit * 2.4f);
        float labPx = Fit("FINAL SCORE", w * 0.6f, _unit * 0.6f);

        _caption.enabled = _result.enabled = _resultLabel.enabled = true;
        _caption.text = line;
        _result.text = _score.ToString();

        float y = ballTop + _unit * 0.55f + capPx * 0.5f;
        PlaceText(_caption, w * 0.5f, y, capPx);
        y += capPx * 0.5f + _unit * 0.25f + scorePx * 0.5f;
        PlaceText(_result, w * 0.5f, y, scorePx);
        y += scorePx * 0.5f + _unit * 0.1f + labPx * 0.5f;
        PlaceText(_resultLabel, w * 0.5f, y, labPx);
    }
}
