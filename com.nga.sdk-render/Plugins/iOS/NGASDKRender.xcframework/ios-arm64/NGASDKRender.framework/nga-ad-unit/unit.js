/* NGA ad unit 0.1.0 */
(function () {
'use strict';
var AD_UNIT_VERSION = "0.1.0";

var Bridge = (function () {
  var OUT = {
    ready: { ad_unit_version: isString },
    started: null,
    progress: { elapsed_ms: isMs, quartile: optional(isQuartile), part: isPart },
    click: { x: isNumber, y: isNumber, target: optional(isString) },
    close: null,
    error: { code: isString, message: isString },
    log: { code: isString, message: isString },
  };
  var IN = {
    init: { manifest: isManifest, policy: isPolicy, insets: isInsets, locale: isString, volume: isVolume },
    pause: null,
    resume: null,
    back: null,
    visibility: { visible: isBool, fraction: isFraction },
    volume: { level: isFraction, muted: isBool },
    reward_granted: null,
  };

  function isString(v) {
    return typeof v === 'string' && v.length <= 256;
  }
  function isNumber(v) {
    return typeof v === 'number' && isFinite(v);
  }
  function isMs(v) {
    return isNumber(v) && v >= 0;
  }
  function isBool(v) {
    return typeof v === 'boolean';
  }
  function isFraction(v) {
    return isNumber(v) && v >= 0 && v <= 1;
  }
  function isQuartile(v) {
    return v === 1 || v === 2 || v === 3 || v === 4;
  }
  function isPart(v) {
    return (
      isObject(v) && isMs(v.index) && (v.kind === 'video' || v.kind === 'endcard') && keysWithin(v, ['index', 'kind'])
    );
  }
  function isObject(v) {
    return v !== null && typeof v === 'object' && !Array.isArray(v);
  }
  function optional(check) {
    var f = function (v) {
      return check(v);
    };
    f.optional = true;
    return f;
  }
  function isManifest(v) {
    return isObject(v) && isString(v.version);
  }
  function isPolicy(v) {
    if (!isObject(v) || !isMs(v.close_delay_ms) || !isObject(v.impression) || !isMs(v.ttl_s)) return false;
    var opt = ['backup_close_ms', 'skippable_after_ms', 'reward_after_ms'];
    for (var i = 0; i < opt.length; i++) if (v[opt[i]] !== undefined && !isMs(v[opt[i]])) return false;
    return v.reward_eligible === undefined || isBool(v.reward_eligible);
  }
  function isInsets(v) {
    return isObject(v) && isMs(v.top) && isMs(v.right) && isMs(v.bottom) && isMs(v.left);
  }
  function isVolume(v) {
    return isObject(v) && isFraction(v.level) && isBool(v.muted);
  }
  function keysWithin(o, allowed) {
    for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k) && allowed.indexOf(k) < 0) return false;
    return true;
  }
  function matches(spec, payload) {
    if (spec === null) return payload === undefined;
    if (!isObject(payload)) return false;
    if (!keysWithin(payload, Object.keys(spec))) return false;
    for (var k in spec) {
      var check = spec[k];
      if (payload[k] === undefined) {
        if (!check.optional) return false;
      } else if (!check(payload[k])) return false;
    }
    return true;
  }

  function channel() {
    var w = window;
    if (w.NGABridge && typeof w.NGABridge.post === 'function') {
      return function (json) {
        w.NGABridge.post(json);
      };
    }
    if (w.webkit && w.webkit.messageHandlers && w.webkit.messageHandlers.nga) {
      return function (json) {
        w.webkit.messageHandlers.nga.postMessage(json);
      };
    }
    return null;
  }

  var handlers = {};

  function send(command, payload) {
    var spec = OUT[command];
    if (spec === undefined || !matches(spec, payload)) return false;
    var post = channel();
    if (!post) return false;
    post(JSON.stringify(payload === undefined ? { command: command } : { command: command, payload: payload }));
    return true;
  }

  function receive(message) {
    var m = message;
    try {
      if (typeof m === 'string') m = JSON.parse(m);
    } catch {
      send('log', { code: 'bridge.unreadable', message: 'unparseable message' });
      return;
    }
    if (!isObject(m) || typeof m.command !== 'string' || !keysWithin(m, ['command', 'payload'])) {
      send('log', { code: 'bridge.unreadable', message: 'not a command' });
      return;
    }
    var spec = IN[m.command];
    if (spec === undefined) {
      send('log', { code: 'bridge.unknown_command', message: m.command.slice(0, 64) });
      return;
    }
    if (!matches(spec, m.payload)) {
      send('log', { code: 'bridge.bad_payload', message: m.command });
      return;
    }
    var h = handlers[m.command];
    if (h) h(m.payload);
  }

  function on(command, handler) {
    handlers[command] = handler;
  }

  return { send: send, receive: receive, on: on, OUT: OUT, IN: IN };
})();


var Creative = (function () {
  function textOf(doc) {
    var pres = doc.getElementsByTagName('pre');
    for (var i = 0; i < pres.length; i++) {
      var t = pres[i].textContent || '';
      if (t.trim().charAt(0) === '{') return t;
    }
    return doc.body ? doc.body.textContent || '' : '';
  }

  function valid(c) {
    if (!c || typeof c !== 'object' || !Array.isArray(c.parts) || c.parts.length < 1 || c.parts.length > 2)
      return false;
    for (var i = 0; i < c.parts.length; i++) {
      var p = c.parts[i];
      if (!p || (p.kind !== 'video' && p.kind !== 'endcard')) return false;
      if (!Array.isArray(p.assets) || !p.assets.length || typeof p.assets[0].path !== 'string') return false;
      if (p.kind === 'video' && !(typeof p.duration_ms === 'number' && p.duration_ms > 0)) return false;
    }
    return c.parts[0].kind === 'video';
  }

  function load(done) {
    var frame = document.createElement('iframe');
    frame.hidden = true;
    frame.setAttribute('aria-hidden', 'true');
    frame.onload = function () {
      var c = null;
      try {
        c = JSON.parse(textOf(frame.contentDocument));
      } catch {
        c = null;
      }
      frame.parentNode.removeChild(frame);
      if (c === null) done('creative.unreadable', null);
      else if (!valid(c)) done('creative.invalid', null);
      else done(null, c);
    };
    frame.src = 'creative.json';
    document.body.appendChild(frame);
  }

  return { load: load, valid: valid };
})();


var Player = (function () {
  var video = null;
  var endcard = null;
  var parts = [];
  var index = -1;
  var quartile = 0;
  var startedSent = false;
  var cb = null;
  var released = false;
  var wanted = false;
  var priming = false;
  var wantMuted = true;
  var endcardFirst = false;
  var endcardReported = false;

  function partInfo() {
    return { index: index, kind: parts[index].kind };
  }

  function progress(q) {
    var elapsed = parts[index].kind === 'video' ? Math.max(0, Math.round(video.currentTime * 1000)) : 0;
    var payload = { elapsed_ms: elapsed, part: partInfo() };
    if (q) payload.quartile = q;
    Bridge.send('progress', payload);
  }

  function durationS() {
    var d = video.duration;
    return isFinite(d) && d > 0 ? d : parts[index].duration_ms / 1000;
  }

  function onTime() {
    if (!wanted || index < 0 || parts[index].kind !== 'video') return;
    var f = video.currentTime / durationS();
    while (quartile < 3 && f >= (quartile + 1) / 4) {
      quartile += 1;
      progress(quartile);
    }
  }

  function onEnded() {
    if (parts[index].kind !== 'video') return;
    while (quartile < 4) {
      quartile += 1;
      progress(quartile);
    }
    next();
  }

  function showEndcard() {
    video.pause();
    video.hidden = true;
    endcard.hidden = false;
    progress();
    if (cb.onPart) cb.onPart(partInfo());
  }

  function next() {
    if (index + 1 < parts.length) {
      index += 1;
      quartile = 0;
      if (parts[index].kind === 'endcard') showEndcard();
    } else if (cb.onFinished) cb.onFinished();
  }

  function start(creative, muted, callbacks) {
    cb = callbacks;
    parts = creative.parts;
    video = document.getElementById('video');
    endcard = document.getElementById('endcard');
    var ec = parts.length > 1 ? parts[1] : null;
    var displayable = false;
    var endcardFailed = false;
    var once = function () {
      if (!displayable) {
        displayable = true;
        cb.onDisplayable();
      }
    };
    if (ec) {
      endcard.onerror = function () {
        endcardFailed = true;
        Bridge.send('error', { code: 'endcard.error', message: ec.assets[0].path.slice(0, 128) });
        if (endcardFirst && !displayable && !released) cb.onUndrawable();
      };
      endcard.onload = function () {
        if (endcardFirst) once();
      };
      endcard.src = ec.assets[0].path;
    }
    wantMuted = muted;
    video.muted = muted;
    video.addEventListener('loadedmetadata', function () {
      if (displayable || wanted || video.readyState >= 2) return;
      priming = true;
      video.muted = true;
      var p = video.play();
      if (p && typeof p.catch === 'function') p.catch(function () {});
    });
    video.addEventListener('loadeddata', function () {
      if (priming) {
        priming = false;
        if (!wanted) {
          video.pause();
          video.currentTime = 0;
        }
        video.muted = wantMuted;
      }
      once();
    });
    video.addEventListener('timeupdate', onTime);
    video.addEventListener('ended', onEnded);
    video.addEventListener('playing', function () {
      if (!wanted) {
        video.pause();
        video.currentTime = 0;
        return;
      }
      if (!startedSent) {
        startedSent = true;
        Bridge.send('started');
      }
    });
    video.addEventListener('error', function () {
      if (released) return;
      Bridge.send('error', { code: 'video.error', message: String((video.error && video.error.code) || 0) });
      if (displayable) {
        if (index === 0 && ec) next();
        return;
      }
      if (ec && !endcardFailed) {
        endcardFirst = true;
        index = 1;
        quartile = 0;
        video.pause();
        video.hidden = true;
        endcard.hidden = false;
        if (endcard.complete && endcard.naturalWidth > 0) once();
      } else cb.onUndrawable();
    });
    index = 0;
    quartile = 0;
    video.src = parts[0].assets[0].path;
  }

  function play() {
    if (endcardFirst && !endcardReported) {
      endcardReported = true;
      progress();
      if (cb.onPart) cb.onPart(partInfo());
      return;
    }
    if (index === 0 && parts[0].kind === 'video') {
      wanted = true;
      progress();
      var p = video.play();
      if (p && typeof p.catch === 'function') p.catch(function () {});
    }
  }

  function pause() {
    if (video && !released) video.pause();
  }

  function resume() {
    if (wanted && video && !released && index >= 0 && parts[index].kind === 'video' && !video.ended) {
      var p = video.play();
      if (p && typeof p.catch === 'function') p.catch(function () {});
    }
  }

  function skip() {
    if (index !== 0 || parts[0].kind !== 'video') return;
    video.pause();
    if (parts.length > 1) next();
    else if (cb.onFinished) cb.onFinished();
  }

  function setMuted(m) {
    wantMuted = m;
    if (video && !priming) video.muted = m;
  }

  function release() {
    if (!video || released) return;
    released = true;
    video.pause();
    video.removeAttribute('src');
    video.load();
  }

  function current() {
    return index >= 0 ? partInfo() : null;
  }

  return {
    start: start,
    play: play,
    pause: pause,
    resume: resume,
    skip: skip,
    setMuted: setMuted,
    release: release,
    current: current,
  };
})();


var Chrome = (function () {
  var STRINGS = {
    en: {
      cta: 'Install',
      skip: 'Skip',
      rewardIn: 'Reward in {n}s',
      rewardEarned: 'Reward earned',
      mute: 'Mute ad',
      unmute: 'Unmute ad',
    },
  };
  var t = STRINGS.en;
  var el = {};
  var policy = null;
  var readyAt = -1;
  var closeOpen = false;
  var skipOpen = false;
  var rewardShown = false;
  var rewardEarned = false;
  var activeMs = 0;
  var activeSince = -1;
  var paused = false;
  var visible = true;
  var timer = null;
  var lastClose = -1;
  var lastReward = -1;
  var onSkip = null;
  var onMute = null;
  var inVideo = true;

  function now() {
    return performance.now();
  }
  function setText(node, s) {
    if (node.textContent !== s) node.textContent = s;
  }

  function setup(p, locale, volume, handlers) {
    policy = p;
    var lang = String(locale || 'en')
      .toLowerCase()
      .split(/[-_]/)[0];
    t = STRINGS[lang] || STRINGS.en;
    ['chrome', 'close', 'close-countdown', 'skip', 'reward', 'mute', 'cta'].forEach(function (id) {
      el[id] = document.getElementById(id);
    });
    onSkip = handlers.onSkip;
    onMute = handlers.onMute;
    setText(el.cta, t.cta);
    setText(el.skip, t.skip);
    setMuted(volume.muted);
    el.mute.addEventListener('click', function () {
      var m = el.mute.getAttribute('aria-pressed') !== 'true';
      setMuted(m);
      onMute(m);
    });
    el.skip.addEventListener('click', function () {
      if (skipOpen && inVideo) {
        el.skip.hidden = true;
        onSkip();
      }
    });
    el.close.addEventListener('click', function () {
      if (closeOpen) handlers.onClose();
    });
    rewardShown = policy.reward_after_ms !== undefined && policy.reward_eligible !== false;
    el.reward.hidden = !rewardShown;
    el.chrome.hidden = false;
    paint();
  }

  function setMuted(m) {
    el.mute.setAttribute('aria-pressed', m ? 'true' : 'false');
    el.mute.setAttribute('aria-label', m ? t.unmute : t.mute);
  }

  function counting() {
    return readyAt >= 0 && !paused && visible;
  }
  function active() {
    return activeMs + (activeSince >= 0 ? now() - activeSince : 0);
  }
  function setCounting() {
    var c = counting();
    if (c && activeSince < 0) activeSince = now();
    if (!c && activeSince >= 0) {
      activeMs += now() - activeSince;
      activeSince = -1;
    }
  }

  function paint() {
    if (readyAt < 0) {
      el['close-countdown'].hidden = false;
      setText(el['close-countdown'], String(Math.ceil(policy.close_delay_ms / 1000)));
      if (rewardShown) setText(el.reward, t.rewardIn.replace('{n}', String(Math.ceil(policy.reward_after_ms / 1000))));
      return;
    }
    var since = now() - readyAt;
    var closeLeft = policy.close_delay_ms - since;
    if (closeLeft <= 0) {
      if (!closeOpen) {
        closeOpen = true;
        el['close-countdown'].hidden = true;
        el.close.hidden = false;
      }
    } else {
      var s = Math.ceil(closeLeft / 1000);
      if (s !== lastClose) {
        lastClose = s;
        setText(el['close-countdown'], String(s));
      }
    }
    if (policy.skippable_after_ms !== undefined && !skipOpen && inVideo && since >= policy.skippable_after_ms) {
      skipOpen = true;
      el.skip.hidden = false;
    }
    if (rewardShown && !rewardEarned) {
      var r = Math.max(0, Math.ceil((policy.reward_after_ms - active()) / 1000));
      if (r !== lastReward) {
        lastReward = r;
        setText(el.reward, t.rewardIn.replace('{n}', String(r)));
      }
    }
  }

  function loop() {
    paint();
    if (
      !closeOpen ||
      (rewardShown && !rewardEarned) ||
      (policy.skippable_after_ms !== undefined && !skipOpen && inVideo)
    ) {
      timer = setTimeout(loop, 250);
    } else timer = null;
  }

  function onReady() {
    readyAt = now();
    setCounting();
    loop();
  }

  function rewardGranted() {
    if (!rewardShown || rewardEarned) return;
    rewardEarned = true;
    el.reward.className = 'pill earned';
    setText(el.reward, t.rewardEarned);
  }

  function setPaused(p) {
    paused = p;
    setCounting();
  }
  function setVisible(v) {
    visible = v;
    setCounting();
  }
  function partChanged(kind) {
    inVideo = kind === 'video';
    if (!inVideo) el.skip.hidden = true;
  }
  function closeOnly() {
    closeOpen = true;
    el['close-countdown'].hidden = true;
    el.close.hidden = false;
    el.skip.hidden = true;
    el.reward.hidden = true;
    el.cta.hidden = true;
    el.mute.hidden = true;
  }
  function closeAllowed() {
    return closeOpen || (readyAt >= 0 && now() - readyAt >= policy.close_delay_ms);
  }
  function stop() {
    if (timer) clearTimeout(timer);
    timer = null;
  }

  return {
    setup: setup,
    onReady: onReady,
    rewardGranted: rewardGranted,
    setMuted: setMuted,
    setPaused: setPaused,
    setVisible: setVisible,
    partChanged: partChanged,
    closeAllowed: closeAllowed,
    closeOnly: closeOnly,
    stop: stop,
  };
})();


(function () {
  var initialised = false;
  var readySent = false;
  var closed = false;
  var lifecyclePaused = false;
  var hidden = false;
  var muted = true;

  function doClose() {
    if (closed || !Chrome.closeAllowed()) return;
    closed = true;
    Chrome.stop();
    Player.release();
    Bridge.send('close');
  }

  var TAP_SLOP = 10;
  var downs = {};
  document.addEventListener(
    'pointerdown',
    function (e) {
      downs[e.pointerId] = { x: e.clientX, y: e.clientY };
    },
    true,
  );
  document.addEventListener(
    'pointercancel',
    function (e) {
      delete downs[e.pointerId];
    },
    true,
  );

  function click(target) {
    return function (e) {
      if (!e.isTrusted || closed || !readySent) return;
      var down = downs[e.pointerId];
      delete downs[e.pointerId];
      if (!down || Math.abs(e.clientX - down.x) + Math.abs(e.clientY - down.y) > TAP_SLOP) return;
      Bridge.send('click', { x: Math.round(e.clientX), y: Math.round(e.clientY), target: target });
    };
  }

  Bridge.on('init', function (p) {
    if (initialised) return;
    initialised = true;
    var root = document.documentElement.style;
    root.setProperty('--inset-top', p.insets.top + 'px');
    root.setProperty('--inset-right', p.insets.right + 'px');
    root.setProperty('--inset-bottom', p.insets.bottom + 'px');
    root.setProperty('--inset-left', p.insets.left + 'px');
    document.documentElement.lang = String(p.locale).slice(0, 35);
    muted = p.volume.muted;
    Chrome.setup(p.policy, p.locale, p.volume, {
      onClose: doClose,
      onSkip: function () {
        Player.skip();
      },
      onMute: function (m) {
        muted = m;
        Player.setMuted(m);
      },
    });
    Creative.load(function (err, creative) {
      if (err) {
        Bridge.send('error', { code: err, message: 'creative.json' });
        return;
      }
      if (p.policy.skippable_after_ms === undefined && creative.skippable_after_ms !== undefined) {
        p.policy.skippable_after_ms = creative.skippable_after_ms;
      }
      Player.start(creative, muted, {
        onDisplayable: function () {
          requestAnimationFrame(function () {
            if (readySent) return;
            readySent = true;
            Bridge.send('ready', { ad_unit_version: AD_UNIT_VERSION });
            Chrome.onReady();
            Player.play();
          });
        },
        onPart: function (part) {
          Chrome.partChanged(part.kind);
        },
        onFinished: function () {
          Chrome.partChanged('endcard');
        },
        onUndrawable: function () {
          if (!readySent) Chrome.closeOnly();
        },
      });
    });
  });
  Bridge.on('pause', function () {
    lifecyclePaused = true;
    Player.pause();
    Chrome.setPaused(true);
  });
  Bridge.on('resume', function () {
    lifecyclePaused = false;
    Chrome.setPaused(false);
    if (!hidden) Player.resume();
  });
  Bridge.on('visibility', function (v) {
    hidden = !v.visible;
    Chrome.setVisible(v.visible);
    if (hidden) Player.pause();
    else if (!lifecyclePaused) Player.resume();
  });
  Bridge.on('volume', function (v) {
    muted = v.muted;
    Chrome.setMuted(v.muted);
    Player.setMuted(v.muted);
  });
  Bridge.on('back', function () {
    doClose();
  });
  Bridge.on('reward_granted', function () {
    Chrome.rewardGranted();
  });

  document.getElementById('stage').addEventListener('pointerup', function (e) {
    var part = Player.current();
    click(part && part.kind === 'endcard' ? 'endcard' : 'video')(e);
  });
  document.getElementById('cta').addEventListener('pointerup', click('cta'));
  window.addEventListener('pagehide', function () {
    Player.release();
  });

  Object.defineProperty(window, 'NGA', {
    value: Object.freeze({ receive: Bridge.receive }),
    writable: false,
    configurable: false,
    enumerable: false,
  });
})();

})();
