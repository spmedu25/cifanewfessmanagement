window.chatInit = () => { try { if ('Notification' in window && Notification.permission === 'default') Notification.requestPermission(); } catch (e) { } };
window.chatBeep = () => {
  try {
    const c = new (window.AudioContext || window.webkitAudioContext)(), o = c.createOscillator(), g = c.createGain();
    o.connect(g); g.connect(c.destination); o.frequency.value = 880; g.gain.value = 0.08; o.start(); o.stop(c.currentTime + 0.18);
  } catch (e) { }
};
window.chatNotify = (title, body) => {
  window.chatBeep();
  try { if ('Notification' in window && Notification.permission === 'granted' && document.hidden) new Notification(title, { body }); } catch (e) { }
};
window.chatScroll = (id) => { const el = document.getElementById(id); if (el) el.scrollTop = el.scrollHeight; };
