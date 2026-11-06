# 🍜 美味小館 - 線上點餐系統

一個部署在 **Cloudflare Workers + D1** 上的輕量級線上點餐系統，顧客可以瀏覽菜單、選擇數量並送出訂單，店家則在廚房管理頁即時看單、控制出餐進度。

## 🌐 線上網址

| 頁面 | 網址 |
|------|------|
| 顧客點餐頁 | <https://food-ordering.taow8603.workers.dev/> |
| 廚房管理頁 | <https://food-ordering.taow8603.workers.dev/admin.html> |

> 管理頁目前為示範用途，未加權限驗證，正式營運建議加上 Cloudflare Access 或 Basic Auth。

## ✨ 功能介紹

### 顧客端（點餐頁）
- 菜單依分類（主食、湯品、飲料...）排版顯示
- 每道菜可加減數量，即時更新購物車總價
- 送單時填寫**姓名**、**桌號/取餐號碼**、**備註**（少辣、加蛋...）
- 送出後顯示**訂單編號**與**總金額**
- 售完菜品自動變灰、無法點選

### 店家端（廚房管理頁）
- 即時檢視所有訂單，每 **10 秒**自動刷新
- 依訂單狀態以顏色標示：🟠 處理中 / 🟢 已完成
- 可一鍵標記完成、重新開啟、刪除訂單
- 可切換是否顯示已完成訂單

### 後端 API
| 方法 | 路徑 | 用途 |
|------|------|------|
| GET | `/api/menu` | 查看菜單 |
| POST | `/api/menu` | 新增菜品 |
| POST | `/api/orders` | 送出訂單 |
| GET | `/api/orders` | 查看訂單（可用 `?status=pending` 過濾） |
| PATCH | `/api/orders/:id` | 更新訂單狀態 |
| DELETE | `/api/orders/:id` | 刪除訂單 |

> 安全性設計：訂單總價由後端依資料庫價格重新計算，而非信任前端資料，防止金額竄改。

## 🏗️ 架構與技術

```
用戶 (瀏覽器 / 手機)
        │
        ▼
Cloudflare Workers (Hono) ──── 靜態頁面 (public/)
        │
        ▼
Cloudflare D1 (SQLite)
  ├─ menu_items       菜單
  ├─ orders           訂單主表
  └─ order_items_placed  訂單明細
```

- **Cloudflare Workers**：全球邊緣節點運算，無伺服器、自動擴展
- **Hono**：專為 Workers 設計的輕量 web 框架
- **Cloudflare D1**：邊緣 SQLite 資料庫，免費額度即可運行
- **純原生前端**：HTML + CSS + Vanilla JS，無需 build step

## 📂 專案結構

```
food-ordering/
├── src/index.js       # 後端 API 路由與訂單邏輯
├── public/index.html  # 顧客點餐頁
├── public/admin.html  # 廚房管理頁
├── schema.sql         # 資料表結構
├── seed.sql           # 預設菜單資料
├── wrangler.jsonc     # Cloudflare 設定（Worker + D1 綁定）
├── package.json
└── README.md
```

## 🚀 快速開始

### 本機開發
```bash
npm run dev      # 啟動本機 dev server (http://localhost:8787)
```

首次在本機跑之前，先初始化本機資料庫：
```bash
npx wrangler d1 execute food-ordering-db --local --file=./schema.sql
npx wrangler d1 execute food-ordering-db --local --file=./seed.sql
```

### 部署到 Cloudflare
```bash
npx wrangler login          # 第一次需要登入（瀏覽器授權）
npm run deploy              # 套用 schema/seed 到雲端 + 部署 Worker
```

> ⚠️ 注意：`deploy` 腳本會重跑 `seed.sql`，多次部署會累積重複菜品。
> 若要修改菜單，建議直接對資料庫操作：
> ```bash
> npx wrangler d1 execute food-ordering-db --remote --command "UPDATE menu_items SET price=60 WHERE id=1"
> ```

## 📝 TODO（未來可擴充）

- [ ] 管理頁加上登入驗證
- [ ] 訂單流水號紀錄 / 每日結帳報表
- [ ] 菜品圖片支援
- [ ] LINE Notify / 電子郵件新單通知

---

Made with ☁️ Cloudflare Workers + D1
