const express = require('express');
const cors = require('cors');
const { Pool } = require('pg');

const app = express();
const PORT = process.env.PORT || 3001;
app.use(cors());
app.use(express.json());

const pool = new Pool({
  connectionString: process.env.DATABASE_URL || 'postgres://app:apppass@postgres:5432/appdb',
});

// 首頁：服務自我介紹
app.get('/', async (req, res) => {
  res.json({
    service: 'node',
    runtime: process.version,
    message: 'Hello from Node.js 微服務 🚀',
    time: new Date().toISOString(),
    endpoints: ['GET /', 'GET /health', 'GET /db', 'GET /messages', 'POST /messages'],
  });
});

app.get('/health', (req, res) => res.json({ status: 'ok', service: 'node' }));

// 資料庫測試：寫入 visits 並回傳總數
app.get('/db', async (req, res) => {
  try {
    await pool.query('INSERT INTO visits(service) VALUES($1)', ['node']);
    const { rows } = await pool.query('SELECT COUNT(*)::int AS count FROM visits');
    res.json({ status: 'ok', db: 'postgres connected ✅', total_visits: rows[0].count });
  } catch (e) {
    res.status(500).json({ status: 'error', message: e.message });
  }
});

// 留言板讀取
app.get('/messages', async (req, res) => {
  try {
    const { rows } = await pool.query('SELECT * FROM messages ORDER BY id DESC LIMIT 20');
    res.json({ status: 'ok', data: rows });
  } catch (e) {
    res.status(500).json({ status: 'error', message: e.message });
  }
});

// 留言板寫入
app.post('/messages', async (req, res) => {
  const { author = 'anonymous', content = '' } = req.body || {};
  if (!content) return res.status(400).json({ status: 'error', message: 'content required' });
  try {
    const { rows } = await pool.query(
      'INSERT INTO messages(author, content) VALUES($1, $2) RETURNING *',
      [author, content]
    );
    res.json({ status: 'ok', data: rows[0] });
  } catch (e) {
    res.status(500).json({ status: 'error', message: e.message });
  }
});

app.listen(PORT, '0.0.0.0', () => console.log(`[node] listening on ${PORT}`));
