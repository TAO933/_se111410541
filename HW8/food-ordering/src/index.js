import { Hono } from "hono";

const app = new Hono();

// 查看菜單
app.get("/api/menu", async (c) => {
  const { results } = await c.env.DB.prepare(
    "SELECT id, name, description, price, category, available FROM menu_items ORDER BY category, id"
  ).all();
  return c.json(results);
});

// 新增菜單項目（管理用）
app.post("/api/menu", async (c) => {
  const { name, description, price, category, available } = await c.req.json();
  const { meta } = await c.env.DB.prepare(
    "INSERT INTO menu_items (name, description, price, category, available) VALUES (?, ?, ?, ?, ?)"
  ).bind(name, description, price, category, available ? 1 : 1).run();
  return c.json({ id: meta.last_row_id }, 201);
});

// 送出訂單
app.post("/api/orders", async (c) => {
  const { customer_name, table_no, note, items } = await c.req.json();

  if (!items || !Array.isArray(items) || items.length === 0) {
    return c.json({ error: "訂單不能為空" }, 400);
  }
  if (!customer_name) {
    return c.json({ error: "請填寫姓名" }, 400);
  }

  const db = c.env.DB;

  // 以資料庫價格為準，重新計算總價
  const placeholders = items.map(() => "?").join(",");
  const { results: menuRows } = await db
    .prepare(`SELECT id, name, price, available FROM menu_items WHERE id IN (${placeholders})`)
    .bind(...items.map((i) => i.menu_item_id))
    .all();
  const menuMap = new Map(menuRows.map((r) => [r.id, r]));

  let total = 0;
  const detailRows = [];
  for (const item of items) {
    const menuItem = menuMap.get(item.menu_item_id);
    if (!menuItem || !menuItem.available) {
      return c.json({ error: "部分菜品不存在或已售完" }, 400);
    }
    const qty = Math.max(1, Number(item.quantity) || 1);
    total += menuItem.price * qty;
    detailRows.push([menuItem.name, menuItem.price, qty]);
  }

  const { meta } = await db
    .prepare("INSERT INTO orders (customer_name, table_no, note, total) VALUES (?, ?, ?, ?)")
    .bind(customer_name, table_no || "", note || "", total)
    .run();
  const orderId = meta.last_row_id;

  await db.batch(
    detailRows.map((row) =>
      db
        .prepare(
          "INSERT INTO order_items_placed (order_id, item_name, price, quantity) VALUES (?, ?, ?, ?)"
        )
        .bind(orderId, ...row)
    )
  );

  return c.json({ id: orderId, total }, 201);
});

// 查看所有訂單（管理用）
app.get("/api/orders", async (c) => {
  const status = c.req.query("status");
  let sql =
    "SELECT id, customer_name, table_no, note, total, status, created_at FROM orders";
  if (status) sql += ` WHERE status = ?`;
  sql += " ORDER BY id DESC";
  const stmt = status
    ? c.env.DB.prepare(sql).bind(status)
    : c.env.DB.prepare(sql);
  const { results: orders } = await stmt.all();

  for (const o of orders) {
    const { results: items } = await c.env.DB.prepare(
      "SELECT item_name, price, quantity FROM order_items_placed WHERE order_id = ?"
    )
      .bind(o.id)
      .all();
    o.items = items;
  }
  return c.json(orders);
});

// 更新訂單狀態（管理用）
app.patch("/api/orders/:id", async (c) => {
  const { status } = await c.req.json();
  if (!["pending", "completed"].includes(status)) {
    return c.json({ error: "狀態不正確" }, 400);
  }
  const { meta } = await c.env.DB.prepare(
    "UPDATE orders SET status = ? WHERE id = ?"
  )
    .bind(status, c.req.param("id"))
    .run();
  return c.json({ success: true, changes: meta.changes });
});

// 刪除訂單（管理用）
app.delete("/api/orders/:id", async (c) => {
  const db = c.env.DB;
  await db.prepare("DELETE FROM order_items_placed WHERE order_id = ?").bind(c.req.param("id")).run();
  await db.prepare("DELETE FROM orders WHERE id = ?").bind(c.req.param("id")).run();
  return c.json({ success: true });
});

export default app;
