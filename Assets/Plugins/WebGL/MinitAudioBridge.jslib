mergeInto(LibraryManager.library, {
  // 1 when every AudioContext the page knows about is running, 0 otherwise.
  // Returns 1 when none exists yet, so a caller polling this cannot deadlock.
  // The registry is populated by the Minit WebGL template.
  MinitAudioContextRunning: function () {
    var contexts = window.__minitAudioContexts;
    if (!contexts || !contexts.length) return 1;
    for (var i = 0; i < contexts.length; i++) {
      if (contexts[i].state !== 'running') return 0;
    }
    return 1;
  },
});
