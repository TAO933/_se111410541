-- 初始化資料庫：給 Node / Python 共用
CREATE TABLE IF NOT EXISTS visits (
    id SERIAL PRIMARY KEY,
    service TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS messages (
    id SERIAL PRIMARY KEY,
    author TEXT NOT NULL DEFAULT 'anonymous',
    content TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

INSERT INTO messages (author, content) VALUES
    ('system', 'Hello from Postgres! 資料庫初始化成功 🎉'),
    ('admin', '這是一則範例留言，試試從網頁新增留言吧！')
ON CONFLICT DO NOTHING;
