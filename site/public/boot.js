// Runs in <head>, before first paint, which is why it is a classic blocking script rather than a module:
// the theme and the platform must be on <html> before anything is drawn. It lives in a file because the
// content security policy says script-src 'self', and an inline script is refused outright.
document.documentElement.classList.add('js');
(() => {
  const root = document.documentElement;
  try {
    const fromUrl = new URLSearchParams(location.search).get('theme');
    const stored = fromUrl || localStorage.getItem('theme');
    const chosen = stored === 'light' || stored === 'dark' ? stored : stored === 'system' ? null : 'dark';
    if (chosen) root.dataset.theme = chosen;
    else delete root.dataset.theme;
  } catch {
    root.dataset.theme = 'dark';
  }
  try {
    const ua = navigator.userAgent || '';
    const platform = navigator.userAgentData?.platform || navigator.platform || ua;
    const touch = /android|iphone|ipod/i.test(ua) || (/ipad/i.test(ua)) || (/mac/i.test(platform) && navigator.maxTouchPoints > 1);
    root.dataset.platform = touch ? 'mobile'
      : /mac/i.test(platform) ? 'mac'
      : /win/i.test(platform) ? 'windows'
      : /cros/i.test(ua) ? 'other'
      : /linux|x11/i.test(platform) ? 'linux'
      : 'other';
  } catch {}
})();
