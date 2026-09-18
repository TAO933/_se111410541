# PasswordVault 密碼管理器

WinUI 3 桌面密碼管理器：本地加密密碼庫 ＋ 瀏覽器自動填入 ＋ 桌面/遊戲 Auto-Type ＋ 剪貼簿降級。
所有資料只存本機（`%LOCALAPPDATA%\PasswordVault`），不經任何雲端伺服器。

## 功能

### 密碼庫（Vault.App，WinUI 3）
- 主密碼建立 / 解鎖（至少 12 字元），Argon2id 派生金鑰
- 新增 / 列表 / 鎖定，MVVM（CommunityToolkit.Mvvm）
- 閒置 5 分鐘自動鎖定，金鑰以 `CryptographicOperations.ZeroMemory` 清除
- 鎖定時同時清空剪貼簿

### 瀏覽器自動填入（Chrome / Edge，Manifest V3）
- `content.js` 偵測 `input[type=password]`（含 SPA / iframe），React 相容的填值方式
- 登入頁自動帶入、提交新帳密時嗅探儲存
- `popup` 一鍵填入；擴充功能本身碰不到資料庫，一律經 Native Messaging 問 Vault.App
- 網址比對用精確 host 相等（防 `googIe-login.com` 類釣魚），不是 `Contains`

### 桌面 / 遊戲 Auto-Type
- 全域快捷鍵 `Ctrl+Shift+L`：抓前景視窗標題＋行程名 → Regex 配對項目 → `SendInput` 逐字打出 `{USERNAME}{TAB}{PASSWORD}{ENTER}`
- 每項目可綁 `window_title_regex`（例 `(?i).*elden.*`），UI 有「3 秒後抓取視窗」幫你產生建議 Regex
- `SendKeys` 不用（對 DirectX / 全螢幕 / UAC 普遍無效），改用 `SendInput` Unicode 模式＋ 10–20ms 間隔
- 按住 Ctrl/Shift 時會等放開再打，避免修飾鍵污染輸出

### 剪貼簿降級（反作弊 / 打不進去時）
- `Ctrl+Shift+C`：只複製配對項目的密碼，不打字，回遊戲 `Ctrl+V`
- 複製後 App 自動最小化，方便切回遊戲
- 10 秒自動清除，且只清「還是我們那串文字」時才清（你中途複製別的東西不會被誤刪）
- 啟動/解鎖/複製時檢查 `ClipboardPolicy`：
  - `HKCU\...\Clipboard\EnableClipboardHistory == 1` → 警告 Win+V 會留痕
  - 跨裝置同步未被政策禁用 → 警告可能上雲，導引到 設定→系統→剪貼簿 關閉

## 架構

```mermaid
UI (WinUI3 MainWindow + MainViewModel) <-- in-process --> VaultService (Locker + VaultStore)
                                                              ^
Browser Extension (MV3) <-- NativeMessaging (stdio) --> Vault.NativeHost --NamedPipe--> PipeServer (同使用者 SID)
Global Hotkey (Ctrl+Shift+L / Ctrl+Shift+C) --> HotkeyService --> AutoTypeService / ClipboardService
```

| 模組 | 位置 | 職責 |
|---|---|---|
| Vault.Core | `Vault.Core/` | 純邏輯：`Kdf`（Argon2id）、`Crypto`（AES-256-GCM）、`Locker`（記憶體金鑰＋自動鎖）、`VaultStore`（SQLCipher）、`AutoType`（序列解析＋標題配對） |
| Vault.App | `Vault.App/` | WinUI 3（`net10.0-windows10.0.22621`，WindowsAppSDK 2.5.1，Unpackaged）。`VaultService` 會話、`PipeServer`（NamedPipe `passwordvault-vault`，鎖當前使用者 SID）、`HotkeyService`、`AutoTyper`（SendInput）、`ClipboardService/Policy`、`ForegroundWindow` |
| Vault.NativeHost | `Vault.NativeHost/` | Console exe。Chrome 用 stdio 啟動，講 4-byte length-prefixed JSON（`ping / get-login / save-login`），轉發給 Vault.App 的 NamedPipe。自己不碰主密碼與金鑰 |
| BrowserExtension | `BrowserExtension/` | MV3：`manifest.json`、`background.js`（native 橋）、`content.js`（找表單＋填值＋嗅探）、`popup.html/js`、`com.passwordvault.host.json`＋`install-host.ps1`（寫 Chrome/Edge registry） |

### 安全設計
- KDF：Argon2id（m=64MB，t=3，p=4）→ 32 byte 金鑰；salt 16 byte 存側車檔（非機密）
- 驗證：只存 `SHA256(key)` 當 verifier，不存主密碼；解密時 `FixedTimeEquals` 比對
- 欄位級：AES-256-GCM，blob = nonce(12)＋cipher＋tag(16)；檔級：SQLCipher `PRAGMA key` 整檔加密
- 金鑰只在解鎖期間放記憶體（`Locker`），鎖定/`Dispose` 即 `ZeroMemory`；`SecureString` 已棄用故不用
- Regex 配對一律 200ms 超時，無效 pattern 永遠不命中、不拋錯
- `url_hash`（SHA256 正規化網址）是唯一明碼索引，只做快速比對

### 資料表（SQLCipher）
- `vault_meta(id=1, salt, verifier)`
- `items(id, folder_id, name_enc, url_hash, url_enc, username_enc, password_enc, totp_enc, notes_enc, window_title_regex, autotype_seq DEFAULT '{USERNAME}{TAB}{PASSWORD}{ENTER}', revision, created_utc, updated_utc)`
- `history(item_id, revision, snapshot_enc)`

## 快捷鍵

| 按鍵 | 場景 | 行為 |
|---|---|---|
| `Ctrl+Shift+L` | 遊戲/桌面程式前景時 | Auto-Type 打出帳密 |
| `Ctrl+Shift+C` | 遊戲/桌面程式前景時 | 複製配對密碼到剪貼簿（10s 清除） |

## 使用流程

### 桌面 App
1. 開 `Vault.App\bin\x64\Debug\net10.0-windows10.0.22621.0\Vault.App.exe`
2. 主密碼（≥12 字元）→ 建立新密碼庫 → 新增項目
3. 選項目 →「3 秒後抓取視窗」→ 切到遊戲 → 回來按「綁定」→ 切到遊戲按 `Ctrl+Shift+L`

### 瀏覽器接線（ID 拿到才能綁 allowlist）
1. `chrome://extensions` 開開發者模式 → 載入未封裝 `BrowserExtension/` → 抄下 ID
2. `dotnet build Vault.NativeHost -c Release`，再跑 `.\BrowserExtension\install-host.ps1 -ExtensionId <ID>`
3. 跑 `Vault.App.exe` 並解鎖 → 開登入頁點擴充 popup 填入

## 建置

需求：.NET 10 SDK、VS 2026（含 WinAppSDK 工作負載）、Node（僅 `node --check` 驗 JS）。

```powershell
# 整個桌面 App（含 Core）
dotnet build .\Vault.App\Vault.App.csproj -p:Platform=x64
# Native host
dotnet build .\Vault.NativeHost\Vault.NativeHost.csproj
# 瀏覽器 JS 語法檢查
node --check .\BrowserExtension\background.js
node --check .\BrowserExtension\content.js
node --check .\BrowserExtension\popup.js
```

執行檔：`Vault.App\bin\x64\Debug\net10.0-windows10.0.22621.0\Vault.App.exe`（Unpackaged，所以 NativeMessaging registry＋全域熱鍵才不受 MSIX 限制）。

## 已知限制
- 子網域要完全相等才命中瀏覽器填入（`a.example.com` ≠ `example.com`）
- 存新帳密目前直接寫入，無確認 UI
- 有反作弊（Vanguard / EAC）、管理員視窗（UAC）時 Auto-Type 可能無效 → 用剪貼簿降級，不要硬繞（會被當外掛）
- 剪貼簿明文期間同使用者行程都讀得到；Win+V 歷史 / 雲端同步開著會留痕（App 會警告，建議關閉）
- `Microsoft.Data.Sqlite` 傳遞依賴 `e_sqlite3 2.1.11` 有 `NU1903` 弱點通告，不擋編譯，等上游修再升版

## 路線圖
- 儲存前確認 prompt、子網域/多 URL 匹配
- TOTP（`otpauth://`）＋ Passkey 欄位
- 健康檢查：重用密碼偵測、HIBP k-anonymity 外洩查詢（不上傳完整雜湊）
