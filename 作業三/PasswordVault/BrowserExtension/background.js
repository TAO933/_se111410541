// Background service worker: bridge between pages/popup and the native host.
const HOST = 'com.passwordvault.host';

const hostOf = (url) => {
  try {
    return new URL(url).hostname.toLowerCase();
  } catch {
    return '';
  }
};

const native = async (msg) => {
  try {
    return await chrome.runtime.sendNativeMessage(HOST, msg);
  } catch (e) {
    return { ok: false, error: String((e && e.message) || e) };
  }
};

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    switch (msg.type) {
      case 'pv-get-logins':
        return native({ op: 'get-login', host: msg.host || '' });
      case 'pv-save-login':
        return native({
          op: 'save-login',
          host: msg.host || '',
          name: msg.name || msg.host || '',
          username: msg.username || '',
          password: msg.password || '',
        });
      case 'pv-observed': {
        const host = hostOf(sender.tab && sender.tab.url);
        if (!host || !msg.username || !msg.password) return { ok: false, error: 'nothing to save' };
        return native({ op: 'save-login', host, name: host, username: msg.username, password: msg.password });
      }
      case 'pv-fields-found':
        if (sender.tab && sender.tab.id != null) {
          chrome.action.setBadgeText({ tabId: sender.tab.id, text: '?' }).catch(() => {});
          chrome.action.setBadgeBackgroundColor({ color: '#b45309' }).catch(() => {});
        }
        return { ok: true };
      default:
        return { ok: false, error: 'unknown message' };
    }
  })().then(sendResponse);
  return true; // async response
});
