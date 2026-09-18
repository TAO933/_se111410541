(() => {
  'use strict';
  const status = document.getElementById('status');
  const list = document.getElementById('list');

  const hostOf = (url) => {
    try {
      return new URL(url).hostname.toLowerCase();
    } catch {
      return '';
    }
  };

  const render = (tabId, host, res) => {
    list.textContent = '';
    if (!res || res.ok !== true) {
      status.textContent = res && /lock/i.test(res.error || '')
        ? '密碼庫已鎖定：請先在桌面 App 解鎖。'
        : `無法連線：${(res && res.error) || 'native host 未安裝'}`;
      return;
    }
    const items = res.items || [];
    if (!items.length) {
      status.textContent = `此網站 (${host}) 沒有儲存的帳號。在此頁登入一次會自動記住。`;
      return;
    }
    status.textContent = `${host} 有 ${items.length} 組帳號：`;
    for (const it of items) {
      const row = document.createElement('div');
      row.className = 'row';
      const meta = document.createElement('div');
      meta.className = 'meta';
      const name = document.createElement('div');
      name.className = 'name';
      name.textContent = it.name || host;
      const user = document.createElement('div');
      user.className = 'user';
      user.textContent = it.username || '';
      meta.append(name, user);
      const btn = document.createElement('button');
      btn.textContent = '填入';
      btn.addEventListener('click', async () => {
        try {
          await chrome.tabs.sendMessage(tabId, { type: 'pv-fill', username: it.username, password: it.password });
          window.close();
        } catch {
          status.textContent = '填入失敗：頁面沒有可辨識的登入表單。';
        }
      });
      row.append(meta, btn);
      list.append(row);
    }
  };

  (async () => {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || !tab.url) {
      status.textContent = '無法取得目前分頁網址。';
      return;
    }
    const host = hostOf(tab.url);
    const res = await chrome.runtime.sendMessage({ type: 'pv-get-logins', host });
    render(tab.id, host, res);
  })().catch((e) => {
    status.textContent = `錯誤：${e.message}`;
  });
})();
