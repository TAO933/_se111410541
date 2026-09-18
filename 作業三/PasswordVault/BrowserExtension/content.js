(() => {
  'use strict';

  const visible = (el) => {
    if (!el || el.disabled || el.readOnly) return false;
    const r = el.getBoundingClientRect();
    if (r.width === 0 && r.height === 0) return false;
    const s = getComputedStyle(el);
    return s.visibility !== 'hidden' && s.display !== 'none';
  };

  const describe = (el) =>
    `${el.type || ''} ${el.name || ''} ${el.id || ''} ${el.autocomplete || ''} ${el.placeholder || ''}`.toLowerCase();

  const findFields = () => {
    const passes = [...document.querySelectorAll('input[type="password"]')].filter(visible);
    if (!passes.length) return null;
    const pass = passes[0];
    const scope = pass.form || document;
    const texts = [...scope.querySelectorAll('input')].filter(
      (el) => el !== pass && visible(el) && /^(text|email|tel|username)$/i.test(el.type || 'text') === false
        ? /text|email|tel|username|login/.test(describe(el))
        : /^(text|email|tel)$/i.test(el.type || ''),
    );
    // simpler fallback: any visible non-password text-like input
    const pool = texts.length ? texts : [...scope.querySelectorAll('input')].filter(
      (el) => el !== pass && visible(el) && el.type !== 'password' && el.type !== 'hidden' && el.type !== 'submit' && el.type !== 'checkbox',
    );
    const user =
      pool.find((el) => /username|login/.test(describe(el))) ||
      pool.find((el) => /email/.test(describe(el))) ||
      pool[0] || null;
    return { user, pass };
  };

  const setValue = (el, value) => {
    const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };

  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg.type === 'pv-fill') {
      const f = findFields();
      if (!f) {
        sendResponse({ ok: false, error: 'no login form on page' });
        return undefined;
      }
      if (f.user) setValue(f.user, msg.username || '');
      setValue(f.pass, msg.password || '');
      sendResponse({ ok: true });
      return undefined;
    }
    if (msg.type === 'pv-has-fields') {
      sendResponse({ has: !!findFields() });
      return undefined;
    }
    return undefined;
  });

  // Badge hint: tell background a login form exists on this page.
  try {
    if (findFields()) chrome.runtime.sendMessage({ type: 'pv-fields-found' }).catch(() => {});
  } catch { /* extension context revoked */ }

  // Observe user-typed credentials on submit (capture phase, works with SPA handlers).
  document.addEventListener('submit', () => {
    try {
      const f = findFields();
      if (f && f.user && f.user.value && f.pass.value) {
        chrome.runtime
          .sendMessage({ type: 'pv-observed', username: f.user.value, password: f.pass.value })
          .catch(() => {});
      }
    } catch { /* ignore */ }
  }, true);
})();
