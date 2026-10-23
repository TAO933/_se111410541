# Blog — 現代軟體工程測試示範專案

一個完整的部落格應用，專門示範**三種層級的軟體測試**：

| 測試層級 | 測試對象 | 技術 | 位置 |
|---------|---------|------|------|
| 1. 單元測試 (Unit Test) | Rust Server（必測） | `cargo test` | `server/src/db.rs` 內嵌測試 |
| 2. 系統測試 (System Test) | Web API（整個服務） | Rust + `reqwest` 打真實 HTTP | `server/tests/system_api.rs` |
| 3. E2E 測試 (End-to-End) | 完整網頁（無頭瀏覽器） | Playwright + Chromium Headless | `e2e/tests/blog.spec.js` |

> **測試金字塔示意**
>
> ```
>        ／ E2E (4 個) ＼          ← 最少，最慢，最接近真實使用者
>      ／ 系統測試 (8 個) ＼       ← 中等，驗證 API 合約
>    ／ 單元測試 (9 個) ＼         ← 最多，最快，驗證最小單位
> ```

---

## 一、專案架構

```
blog/
├── server/                 # Rust 後端（axum + SQLite）
│   ├── src/
│   │   ├── main.rs         # 進入點：啟動 HTTP 伺服器 (port 8080)
│   │   ├── lib.rs          # 讓測試可以 import 內部模組
│   │   ├── api.rs          # HTTP 路由與 handler（/api/posts CRUD）
│   │   ├── db.rs           # PostRepo：SQLite 資料存取 ← 單元測試就在這裡
│   │   └── models.rs       # 資料模型 (Post / NewPost)
│   └── tests/
│       └── system_api.rs   # 系統測試：透過真實 HTTP 測 Web API
├── client/                 # Node.js 前端（原生 HTML/JS，不需測試）
│   ├── server.js           # 極簡 Node 靜態檔伺服器 (port 3000)
│   └── public/
│       ├── index.html      # 部落格頁面（含 data-testid 供 E2E 定位）
│       ├── app.js          # 呼叫 API、渲染文章列表
│       └── style.css
├── e2e/                    # Playwright E2E 測試（無頭瀏覽器）
│   ├── playwright.config.js
│   └── tests/blog.spec.js
└── README.md               # 本檔案
```

### 技術選型理由

- **Server 用 Rust（一定要測）**：Rust 生態系有內建的測試框架（內建 `#[test]` 屬性＋`cargo test`），零額外依賴；且系統程式語言的邏輯錯誤後果嚴重，因此後端是測試重點。
- **Client 用 Node.js（可不測）**：前端只是簡單的靜態頁面（原生 HTML/JS），商業邏輯極少，依照「server 一定要測、client 可以不測」的原則，把測試資源集中在後端。
- **SQLite**：單檔資料庫，測試時可輕鬆建立 in-memory 資料庫，讓單元測試之間完全隔離。

### Web API 規格（系統測試的標的）

| Method | Path | 說明 | 成功狀態碼 |
|--------|------|------|-----------|
| GET | `/api/health` | 健康檢查 | 200 |
| GET | `/api/posts` | 文章列表（新→舊） | 200 |
| POST | `/api/posts` | 建立文章 | 201 |
| GET | `/api/posts/{id}` | 取得單篇 | 200 / 404 |
| PUT | `/api/posts/{id}` | 更新文章 | 200 / 404 |
| DELETE | `/api/posts/{id}` | 刪除文章 | 200 |

建立文章的請求主體：`{ "title": "標題", "body": "內容", "author": "作者" }`（缺少 `title` 會回 `422`）。

---

## 二、三種測試的詳細說明

### 1. 單元測試 — Rust Server

**測什麼**：最小可測單位。`PostRepo` 的每個 CRUD 方法在 in-memory SQLite 上逐一驗證，不經過 HTTP、不經過 axum，非常快。

**如何測**：Rust 內建測試框架，直接在原始碼同檔案寫 `#[cfg(test)] mod tests`，用 `cargo test` 執行。

**測試案例**（`server/src/db.rs` 底部）：

| 測試 | 驗證點 |
|------|--------|
| `create_and_get_post` | 建立後能取回相同內容 |
| `create_sequential_ids` | id 遞增 |
| `list_posts_returns_newest_first` | 列表排序為新→舊 |
| `update_post_changes_fields` | 更新改變欄位、保留 created_at |
| `update_missing_post_fails` | 更新不存在的文章要失敗 |
| `delete_post_works` | 刪除一次成功、重複刪除回 false |
| `tables_start_empty` | 新資料庫是空的 |

**執行方式**：

```powershell
cd blog/server
cargo test
```

### 2. 系統測試 — Web API 測試

**測什麼**：把整個 axum 伺服器**真的跑起來**（綁隨機 port、用暫存 SQLite 檔），用 `reqwest` HTTP client 打真的網路請求，驗證：

- HTTP **狀態碼**是否正確（200/201/404/400/422）
- 回應 **JSON 格式**與欄位內容
- 完整的 **CRUD 業務流程**（建立→查詢→更新→列表→刪除→查詢變 404）
- **錯誤處理**（查無此文章 404；缺必填欄位 422；id 不是數字 400）

這一層已經包含 HTTP 框架、序列化、路由，但**不包含瀏覽器**，所以比 E2E 快、比單元測試更接近真實系統行為。每個測試都啟動自己獨立的伺服器＋暫存資料庫，互不干擾。

**執行方式**（也可單獨跑這組整合測試）：

```powershell
cd blog/server
cargo test --test system_api
```

**測試案例**（`server/tests/system_api.rs`）：

| 測試 | 驗證點 |
|------|--------|
| `test_01_health_check` | GET /api/health → 200 + `{"status":"ok"}` |
| `test_02_empty_list_returns_empty_array` | 空資料庫列表回 `[]` |
| `test_03_create_post_returns_201_and_post` | POST → 201 與完整欄位 |
| `test_04_full_crud_flow` | 完整 CRUD 流程含 404 |
| `test_05_get_missing_post_returns_404` | 查不存在的文章 → 404 |
| `test_06_create_without_title_fails` | 缺 title → 422 |
| `test_07_invalid_id_returns_400` | id 非數字 → 400 |
| `test_08_delete_missing_post_creates_deleted_false` | 刪不存在 → `{"deleted":false}` |

### 3. E2E 測試 — Playwright 無頭瀏覽器

**測什麼**：從「使用者」的角度測試整個系統：真的開一個（無頭 = headless，不顯示視窗的）Chromium 瀏覽器，載入 `http://localhost:3000`，模擬使用者填表單、按下按鈕、看畫面上有沒有出現預期的內容。

**特點**：

- `playwright.config.js` 會**自動啟動**兩個伺服器（Rust API + Node 前端），測完自動關閉（`webServer` 設定；若服務已在跑則會重用）
- 使用 `data-testid` 屬性定位元素，不受改版動畫/文字影響
- 失敗時自動截圖（`screenshot: "only-on-failure"`）＋保存測試軌跡（trace）

**測試案例**（`e2e/tests/blog.spec.js`）：

| 測試 | 模擬的使用者故事 |
|------|----------------|
| 首頁正確載入 | 開啟首頁，看得到標題和發文表單 |
| 發表新文章會出現在列表 | 填寫表單→點「發表」→新文章出現在頁面上 |
| 標題沒填時表單不能送出 | 只填內容不填標題，瀏覽器擋下送出（HTML required） |
| 建立的文章重新載入後仍存在 | 發表→重新整理→文章仍在（驗證資料真的存進資料庫） |

**執行方式**：

```powershell
cd blog/e2e

# 第一次需要安裝瀏覽器
npm install
npx playwright install chromium

# 執行測試（自動帶起 API 與前端伺服器）
npx playwright test

# 想看瀏覽器視窗執行過程（有頭模式）
npx playwright test --headed

# 查看測試報告
npx playwright show report
```

---

## 三、一鍵執行所有測試

```powershell
# 1 + 2：單元測試 + 系統測試（共 17 個）
cd blog/server ; cargo test

# 3：E2E 測試（4 個）
cd blog/e2e ; npx playwright test
```

本機實測結果：

- 單元測試：9 passed
- 系統測試：8 passed（0.88s）
- E2E 測試：4 passed (14.9s)，無頭 Chromium

## 四、如何執行這個專案（非測試模式）

```powershell
# 終端機 1：啟動 Rust API (port 8080)
cd blog/server
cargo run

# 終端機 2：啟動 Node.js 前端 (port 3000)
cd blog/client
npm start

# 瀏覽器打開 http://localhost:3000
```

也可以直接用 curl 驗證 API：

```powershell
curl http://localhost:8080/api/posts
curl -X POST http://localhost:8080/api/posts -H "Content-Type: application/json" -d '{\"title\":\"你好\", \"body\":\"第一篇文章\", \"author\":\"小明\"}'
curl http://localhost:8080/api/posts/1
```
