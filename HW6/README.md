# 🐳 MicroDocker — Docker 微服務系統（學習記錄 + 實作）

> 課程作業：用 Docker 建置一個微服務系統，繳交內容包含 `Dockerfile` 與 `docker-compose.yml`，
> 內含 **Nginx（reverse proxy）＋ PostgreSQL＋ Node.js / Python / Rust** 執行環境＋簡易 Web 介面。

---

## 1. 系統架構

```
                        ┌──────────────────────────────┐
                        │  Browser  http://localhost:8080 │
                        └──────────────┬───────────────┘
                                       │
                        ┌──────────────▼───────────────┐
                        │  reverse-proxy (nginx:1.27)  │
                        │  唯一對外窗口 :8080 → :80     │
                        └──┬────────┬────────┬────────┬──┘
                   /       │/api/node│/api/python│/api/rust
                  ▼        ▼        ▼        ▼
            ┌─────────┐┌────────┐┌────────┐┌────────┐    ┌──────────┐
            │frontend ││api-node││api-py  ││api-rust│───▶│ postgres │
            │nginx靜態││Node 20 ││Py 3.12 ││Rust1.78│    │ v16-alpine│
            │  :80    ││ :3001  ││ :5000  ││ :8000  │◀───│  :5432   │
            └─────────┘└────────┘└────────┘└────────┘    └──────────┘
                 全部在同一 bridge network「micronet」，用 service name 當 DNS
                 資料持久化：named volume「pgdata」
```

| 服務 | 映像 / 建置 | 功能 | 對外路徑（經 nginx） |
|---|---|---|---|
| reverse-proxy | `nginx:1.27-alpine` + `nginx/nginx.conf` | 反向代理、路由分發 | `http://localhost:8080` |
| frontend | 自建 `frontend/Dockerfile` | 簡易 Web 展示介面 | `/` |
| api-node | 自建 `backend-node/Dockerfile`（node:20-alpine） | Express API＋留言板＋DB 寫入 | `/api/node/` |
| api-python | 自建 `backend-python/Dockerfile`（python:3.12-slim） | Flask API＋DB 寫入 | `/api/python/` |
| api-rust | 多階段建置 `backend-rust/Dockerfile`（rust:bookworm → debian-slim） | Axum 高效能 API | `/api/rust/` |
| postgres | `postgres:16-alpine` + `db/init.sql` | 資料庫，healthcheck | 本機 `5432`（可選） |

### 目錄結構

```
.
├── docker-compose.yml      # 編排 6 個服務（本作業核心繳交物之一）
├── README.md               # 本文件（學習記錄）
├── nginx/nginx.conf        # 反向代理路由規則
├── frontend/
│   ├── Dockerfile          # FROM nginx:alpine 伺服靜態頁
│   └── index.html          # 展示介面：架構圖＋服務狀態＋留言板
├── backend-node/
│   ├── Dockerfile          # FROM node:20-alpine
│   ├── package.json
│   └── server.js           # Express + pg
├── backend-python/
│   ├── Dockerfile          # FROM python:3.12-slim
│   ├── requirements.txt
│   └── app.py              # Flask + psycopg2
├── backend-rust/
│   ├── Dockerfile          # 多階段建置
│   ├── Cargo.toml
│   └── src/main.rs         # Axum
└── db/init.sql             # 初始化 visits / messages 表
```

---

## 2. Docker 學習記錄

### 2.1 Docker 是什麼？為什麼要用？

- **映像（Image）**：唯讀模板，如 `nginx:1.27-alpine`。`Dockerfile` 就是食譜。
- **容器（Container）**：映像的執行個體，彼此隔離但共享宿主核心，比 VM 輕量。
- **倉庫（Registry）**：如 Docker Hub，`docker pull` 即下載。
- 本作業體會：以前要在本機裝 Node＋Python＋Rust＋Postgres＋Nginx 五種環境，版本衝突惡夢；
  用 Docker 後每個服務各自獨立，`docker compose up` 一鍵全起，換電腦也能重現。

### 2.2 學會的 Dockerfile 指令（本專案實際用到）

| 指令 | 作用 | 本專案範例 |
|---|---|---|
| `FROM` | 基底映像 | `FROM node:20-alpine`（輕量 Alpine 省 ~80% 體積） |
| `WORKDIR` | 工作目錄（自動 mkdir＋cd） | `WORKDIR /app` |
| `COPY` | 複製檔案進映像 | `COPY server.js ./` |
| `RUN` | 建置期執行（會留層） | `RUN npm install --omit=dev` / `RUN pip install -r requirements.txt` / `RUN cargo build --release` |
| `EXPOSE` | 聲明監聽埠（文件性質，真正映射靠 compose `ports`） | `EXPOSE 3001` |
| `CMD` | 容器啟動預設命令 | `CMD ["node","server.js"]` |
| `COPY --from=builder` | 多階段建置，只拿產物 | Rust：編譯階段 `rust:1.78` → 執行階段 `debian-slim`，映像從 ~1GB 縮到 ~80MB |

> **層快取（Layer Cache）心得**：把不常變的先 COPY（`package.json` / `requirements.txt` / `Cargo.toml`），
> 再 `RUN install`，最後才 COPY 原始碼。這樣改一行 code 不會重裝依賴，build 快很多。

### 2.3 學會的 docker-compose.yml 觀念

- **services**：定義 6 個容器，`container_name` 方便 `docker logs micro-node` 除錯。
- **build vs image**：自建服務用 `build: ./backend-node`；直接用官方的用 `image: postgres:16-alpine`。
- **ports vs expose**：`ports: "8080:80"` 打洞到宿主（只有 nginx 需要）；`expose` 僅容器間可見，更安全。
- **environment**：注入 `DATABASE_URL`，讓 Node/Python 用 `postgres` 這個 DNS 名連 DB（不是 localhost！這是新手最常踩的坑）。
- **depends_on + healthcheck**：`postgres` 設 `pg_isready` 健康檢查，Node/Python 等 DB ready 才啟動，避免連線被拒。
- **volumes**：`pgdata:/var/lib/postgresql/data` 讓 DB 資料 survives `docker compose down`；`./db/init.sql:/docker-entrypoint-initdb.d/...:ro` 首次啟動自動建表。
- **networks**：自建 `micronet` bridge，同網段容器可用 service name 互連（`http://api-python:5000`）。

### 2.4 學會的常用指令

```bash
docker compose up --build -d   # 建置＋背景啟動全部
docker compose ps              # 看服務狀態
docker compose logs -f         # 追 log（可加服務名，如 -f api-node）
docker compose down            # 停止並刪容器（volume 保留）
docker compose down -v         # 連 volume 一起刪（DB 資料清空，慎用）
docker exec -it micro-postgres psql -U app -d appdb  # 進 DB 下 SQL
docker images / docker ps      # 看映像 / 執行中容器
```

### 2.5 Nginx 反向代理心得

- 瀏覽器只打 `:8080`，nginx 依路徑前綴分流（`location /api/node/` 等），後端可隨意增減、換語言，前端無感。
- `rewrite ^/api/node/(.*)$ /$1 break;` 把前綴剝掉，後端只需寫乾淨的 `/`、`/health`、`/db`。
- `proxy_set_header Host / X-Real-IP` 保留原始請求資訊。
- `upstream` 區塊讓 compose DNS 負載均衡未來可擴展（`docker compose up --scale api-node=3`）。

---

## 3. 快速啟動

### 前置需求

- Docker Desktop（已含 Compose v2）或 Docker Engine 24+。確認：
  ```bash
  docker --version && docker compose version
  ```

### 一鍵啟動

```bash
cd docker
docker compose up --build -d
```

開啟瀏覽器 → **http://localhost:8080**，應看到深色展示頁：

- 🏗 架構圖
- 🟢 四張服務卡（Node / Python / Rust / PostgreSQL）→ 按「檢測」會打對應 API 並顯示 JSON
- 💬 留言板 → 寫入 PostgreSQL `messages` 表（經 Node API），重整仍在（volume 持久化）

### 直接測 API（走 nginx）

```bash
curl http://localhost:8080/api/node/    # Node
curl http://localhost:8080/api/python/  # Python
curl http://localhost:8080/api/rust/    # Rust
curl http://localhost:8080/api/node/db  # DB 寫入測試（total_visits 會累加）
curl http://localhost:8080/healthz      # nginx 本體
```

### 進資料庫驗證

```bash
docker exec -it micro-postgres psql -U app -d appdb -c "SELECT * FROM visits ORDER BY id DESC LIMIT 5;"
docker exec -it micro-postgres psql -U app -d appdb -c "SELECT * FROM messages;"
```

### 停止 / 清理

```bash
docker compose down       # 保留 DB 資料
docker compose down -v    # 連 DB 資料一起刪
```

---

## 4. 展示介面說明（`frontend/index.html`）

- 純靜態、無需 build，由 `frontend` 容器的 nginx 伺服。
- 功能：
  1. 架構文字圖＋一鍵「重新檢測所有服務」。
  2. 四張卡片分別 `fetch('/api/node/')`、`/api/python/`、`/api/rust/`、`/api/node/db`，綠/紅燈顯示狀態，`<pre>` 顯示回傳 JSON。
  3. 留言板 `GET/POST /api/node/messages`，證明 Postgres＋volume 正常。

---

## 5. 遇到的坑與解法（供老師／同學參考）

| 坑 | 解法 |
|---|---|
| 後端連不到 DB（`localhost:5432 refused`） | 容器內 `localhost` 是自己，必須用 service name `postgres:5432`，靠 `DATABASE_URL` 注入 |
| Node 比 DB 先啟動導致 crash | `depends_on: condition: service_healthy`＋`pg_isready` healthcheck |
| `/api/node` 404 | nginx `location` 需處理無尾斜線，另加 `location = /api/node { return 301 /api/node/; }` |
| Rust 映像 1GB 太大 | 改多階段建置，執行期只用 `debian:bookworm-slim`＋二進位檔 |
| Windows 換行 `^M` 導致 sh 失敗 | 全用 JSON 陣列 `CMD`，避開 shell；存檔用 LF |
| Port 被佔用（5432/8080） | `docker ps` 查兇手，或改 compose `ports` 左側 |

---

## 6. 可延伸方向

- 加 Redis 快取服務、加 Prometheus＋Grafana 監控。
- `docker compose up --scale api-node=3` 驗證 nginx 負載均衡。
- 寫 `frontend` 的多階段 build（npm build → nginx），展示真正的前端編譯流程。
- 接 CI（GitHub Actions `docker build`＋`compose config` 檢查）。

---

**執行結果截圖**：
