import { createServer } from 'node:http';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync, existsSync } from 'node:fs';
import { dirname, join, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = join(__dirname, '..');
const DB_PATH = join(__dirname, 'school.db');
const PORT = Number(process.env.PORT || 3001);

if (!existsSync(DB_PATH)) {
  console.error(`找不到 ${DB_PATH}，請先執行: node db/seed.mjs`);
  process.exit(1);
}

const db = new DatabaseSync(DB_PATH);
db.exec('PRAGMA foreign_keys = ON');

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg' };

// 白名單表，避免 SQL injection（表名不能用參數綁定）
const TABLES = new Set(['users', 'courses', 'enrollments', 'grades', 'attendance', 'leaves', 'rewards', 'announcements']);

function send(res, code, obj, contentType = 'application/json; charset=utf-8') {
  if (Buffer.isBuffer(obj)) {
    res.writeHead(code, { 'Content-Type': contentType });
    res.end(obj);
    return;
  }
  const body = typeof obj === 'string' ? obj : JSON.stringify(obj);
  res.writeHead(code, { 'Content-Type': contentType, 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type' });
  res.end(body);
}

function readBody(req) {
  return new Promise((resolve) => {
    let s = '';
    req.on('data', (c) => (s += c));
    req.on('end', () => {
      try { resolve(s ? JSON.parse(s) : {}); } catch { resolve({}); }
    });
  });
}

const server = createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  if (req.method === 'OPTIONS') return send(res, 204, '');

  // --- 靜態檔：讓 http://127.0.0.1:3001/ 直接開 index.html ---
  if (!url.pathname.startsWith('/api/')) {
    const file = url.pathname === '/' ? '/index.html' : url.pathname;
    const path = join(ROOT, decodeURIComponent(file));
    if (!path.startsWith(ROOT) || !existsSync(path)) return send(res, 404, 'Not found', 'text/plain');
    return send(res, 200, readFileSync(path), MIME[extname(path)] || 'application/octet-stream');
  }

  const parts = url.pathname.slice(5).split('/').filter(Boolean); // 去掉 /api/
  const [table, id] = parts;

  // 登入：POST /api/login {account,password,role}
  if (table === 'login' && req.method === 'POST') {
    const { account, password, role } = await readBody(req);
    const u = db.prepare('SELECT * FROM users WHERE account = ? AND password = ?').get(account, password);
    if (!u) return send(res, 401, { error: '帳號或密碼錯誤' });
    if (role && u.role !== role) return send(res, 403, { error: '身分頁籤不符' });
    const { password: _pw, ...safe } = u;
    return send(res, 200, safe);
  }

  // 整庫讀寫：GET /api/db 回傳 8 張表；POST /api/db 整庫覆寫（前端 saveDB 用）
  if (table === 'db') {
    if (req.method === 'GET') {
      const out = {};
      for (const t of TABLES) out[t] = db.prepare(`SELECT * FROM ${t}`).all();
      // DB 存 description，前端用 desc：補上 desc 別名保持相容
      out.courses = out.courses.map((c) => ({ ...c, desc: c.description }));
      return send(res, 200, out);
    }
    if (req.method === 'POST') {
      const body = await readBody(req);
      for (const t of TABLES) if (!Array.isArray(body[t])) return send(res, 400, { error: `missing table ${t}` });
      const COLS = {
        users: ['id', 'account', 'password', 'role', 'name', 'dept', 'email', 'phone', 'birthday', 'address', 'advisorId'],
        courses: ['id', 'code', 'name', 'teacherId', 'credits', 'semester', 'time', 'room', 'capacity', 'description'],
        enrollments: ['studentId', 'courseId'],
        grades: ['studentId', 'courseId', 'regular', 'midterm', 'final', 'score'],
        attendance: ['id', 'studentId', 'courseId', 'date', 'status', 'note'],
        leaves: ['id', 'studentId', 'date', 'reason', 'status', 'applyDate', 'reviewer', 'reviewNote'],
        rewards: ['id', 'studentId', 'type', 'date', 'reason', 'recorder'],
        announcements: ['id', 'title', 'content', 'authorId', 'authorName', 'date', 'pinned'],
      };
      const normCourse = (c) => ({ ...c, description: c.description ?? c.desc ?? '' });
      try {
        db.exec('BEGIN');
        for (const t of ['enrollments', 'grades', 'attendance', 'leaves', 'rewards', 'announcements', 'courses', 'users']) {
          db.prepare(`DELETE FROM ${t}`).run();
        }
        const put = (t, row) => {
          const cols = COLS[t];
          db.prepare(`INSERT INTO ${t} (${cols.map((k) => `"${k}"`).join(',')}) VALUES (${cols.map(() => '?').join(',')})`)
            .run(...cols.map((k) => row[k] ?? null));
        };
        for (const u of body.users) put('users', u);
        for (const c of body.courses) put('courses', normCourse(c));
        for (const e of body.enrollments) put('enrollments', e);
        for (const g of body.grades) put('grades', g);
        for (const a of body.attendance) put('attendance', a);
        for (const l of body.leaves) put('leaves', l);
        for (const r of body.rewards) put('rewards', r);
        for (const n of body.announcements) put('announcements', { ...n, pinned: n.pinned ? 1 : 0 });
        db.exec('COMMIT');
      } catch (e) {
        try { db.exec('ROLLBACK'); } catch {}
        return send(res, 500, { error: String(e.message || e) });
      }
      return send(res, 200, { ok: true });
    }
    return send(res, 405, { error: 'method not allowed' });
  }

  // 重置：POST /api/reset（僅展示用）
  if (table === 'reset' && req.method === 'POST') {
    return send(res, 200, { ok: true, hint: '請執行 node db/seed.mjs 後重啟 server' });
  }

  if (!TABLES.has(table)) return send(res, 404, { error: 'unknown table' });

  try {
    if (req.method === 'GET') {
      // 支援 ?studentId= / ?courseId= 等簡單過濾
      const filters = [...url.searchParams.entries()];
      let sql = `SELECT * FROM ${table}`;
      const args = [];
      if (filters.length) {
        sql += ' WHERE ' + filters.map(([k]) => `"${k}" = ?`).join(' AND ');
        for (const [, v] of filters) args.push(v);
      }
      const rows = db.prepare(sql).all(...args);
      return send(res, 200, rows);
    }
    if (req.method === 'POST') {
      const body = await readBody(req);
      const keys = Object.keys(body);
      if (!keys.length) return send(res, 400, { error: 'empty body' });
      const sql = `INSERT INTO ${table} (${keys.map((k) => `"${k}"`).join(',')}) VALUES (${keys.map(() => '?').join(',')})`;
      db.prepare(sql).run(...keys.map((k) => body[k]));
      return send(res, 201, { ok: true });
    }
    if (req.method === 'PUT') {
      const body = await readBody(req);
      delete body.id; delete body.studentId; delete body.courseId;
      const keys = Object.keys(body);
      if (!keys.length) return send(res, 400, { error: 'empty body' });
      if (table === 'enrollments' || table === 'grades') {
        const { studentId, courseId } = url.searchParams.entries ? Object.fromEntries(url.searchParams) : {};
        // id 用 studentId,courseId 組合：PUT /api/grades/s001_C001
        const [sid, cid] = (id || '').split('_');
        if (!sid || !cid) return send(res, 400, { error: 'need id as studentId_courseId' });
        const sql = `UPDATE ${table} SET ${keys.map((k) => `"${k}" = ?`).join(',')} WHERE studentId = ? AND courseId = ?`;
        db.prepare(sql).run(...keys.map((k) => body[k]), sid, cid);
      } else {
        if (!id) return send(res, 400, { error: 'need id' });
        const sql = `UPDATE ${table} SET ${keys.map((k) => `"${k}" = ?`).join(',')} WHERE id = ?`;
        db.prepare(sql).run(...keys.map((k) => body[k]), id);
      }
      return send(res, 200, { ok: true });
    }
    if (req.method === 'DELETE') {
      if (table === 'enrollments' || table === 'grades') {
        const [sid, cid] = (id || '').split('_');
        if (!sid || !cid) return send(res, 400, { error: 'need id as studentId_courseId' });
        db.prepare(`DELETE FROM ${table} WHERE studentId = ? AND courseId = ?`).run(sid, cid);
      } else {
        if (!id) return send(res, 400, { error: 'need id' });
        db.prepare(`DELETE FROM ${table} WHERE id = ?`).run(id);
      }
      return send(res, 200, { ok: true });
    }
    return send(res, 405, { error: 'method not allowed' });
  } catch (e) {
    return send(res, 500, { error: String(e.message || e) });
  }
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`school-system API: http://127.0.0.1:${PORT}/ (DB: ${DB_PATH})`);
});
