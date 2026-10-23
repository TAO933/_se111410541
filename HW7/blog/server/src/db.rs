use crate::models::{NewPost, Post};
use rusqlite::{params, Connection, Result};

pub struct PostRepo {
    conn: Connection,
}

impl PostRepo {
    pub fn new(path: &str) -> Result<Self> {
        let conn = Connection::open(path)?;
        let repo = Self { conn };
        repo.init_tables()?;
        Ok(repo)
    }

    pub fn new_in_memory() -> Result<Self> {
        let conn = Connection::open_in_memory()?;
        let repo = Self { conn };
        repo.init_tables()?;
        Ok(repo)
    }

    fn init_tables(&self) -> Result<()> {
        self.conn.execute(
            "CREATE TABLE IF NOT EXISTS posts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                author TEXT NOT NULL DEFAULT '匿名',
                created_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            )",
            [],
        )?;
        Ok(())
    }

    pub fn create_post(&self, draft: NewPost) -> Result<Post> {
        self.conn.execute(
            "INSERT INTO posts (title, body, author) VALUES (?1, ?2, ?3)",
            params![draft.title, draft.body, draft.author],
        )?;
        let id = self.conn.last_insert_rowid();
        self.get_post(id)
    }

    pub fn list_posts(&self) -> Result<Vec<Post>> {
        let mut stmt = self
            .conn
            .prepare("SELECT id, title, body, author, created_at FROM posts ORDER BY id DESC")?;
        let rows = stmt.query_map([], |row| {
            Ok(Post {
                id: row.get(0)?,
                title: row.get(1)?,
                body: row.get(2)?,
                author: row.get(3)?,
                created_at: row.get(4)?,
            })
        })?;
        rows.collect()
    }

    pub fn get_post(&self, id: i64) -> Result<Post> {
        self.conn
            .query_row(
                "SELECT id, title, body, author, created_at FROM posts WHERE id = ?1",
                params![id],
                |row| {
                    Ok(Post {
                        id: row.get(0)?,
                        title: row.get(1)?,
                        body: row.get(2)?,
                        author: row.get(3)?,
                        created_at: row.get(4)?,
                    })
                },
            )
    }

    pub fn update_post(&self, id: i64, draft: NewPost) -> Result<Post> {
        let affected = self.conn.execute(
            "UPDATE posts SET title = ?1, body = ?2, author = ?3 WHERE id = ?4",
            params![draft.title, draft.body, draft.author, id],
        )?;
        if affected == 0 {
            return Err(rusqlite::Error::QueryReturnedNoRows);
        }
        self.get_post(id)
    }

    pub fn delete_post(&self, id: i64) -> Result<bool> {
        let affected = self
            .conn
            .execute("DELETE FROM posts WHERE id = ?1", params![id])?;
        Ok(affected > 0)
    }

    pub fn count_posts(&self) -> Result<i64> {
        self.conn
            .query_row("SELECT COUNT(*) FROM posts", [], |row| row.get(0))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn repo() -> PostRepo {
        PostRepo::new_in_memory().unwrap()
    }

    fn draft(title: &str, body: &str) -> NewPost {
        NewPost {
            title: title.to_string(),
            body: body.to_string(),
            author: "Alice".to_string(),
        }
    }

    #[test]
    fn create_and_get_post() {
        let r = repo();
        let post = r.create_post(draft("第一篇", "大家好")).unwrap();
        assert_eq!(post.id, 1);
        assert_eq!(post.title, "第一篇");
        assert_eq!(post.author, "Alice");
        let fetched = r.get_post(1).unwrap();
        assert_eq!(fetched, post);
    }

    #[test]
    fn create_sequential_ids() {
        let r = repo();
        let p1 = r.create_post(draft("a", "a")).unwrap();
        let p2 = r.create_post(draft("b", "b")).unwrap();
        assert_eq!(p2.id, p1.id + 1);
    }

    #[test]
    fn list_posts_returns_newest_first() {
        let r = repo();
        r.create_post(draft("舊的", "...")).unwrap();
        r.create_post(draft("新的", "...")).unwrap();
        let posts = r.list_posts().unwrap();
        assert_eq!(posts.len(), 2);
        assert_eq!(posts[0].title, "新的");
    }

    #[test]
    fn update_post_changes_fields() {
        let r = repo();
        let p = r.create_post(draft("原標題", "原文")).unwrap();
        let updated = r.update_post(p.id, draft("新標題", "新文")).unwrap();
        assert_eq!(updated.title, "新標題");
        assert_eq!(updated.body, "新文");
        assert_eq!(updated.created_at, p.created_at);
    }

    #[test]
    fn update_missing_post_fails() {
        let r = repo();
        assert!(r.update_post(999, draft("x", "y")).is_err());
    }

    #[test]
    fn delete_post_works() {
        let r = repo();
        let p = r.create_post(draft("待刪", "...")).unwrap();
        assert!(r.delete_post(p.id).unwrap());
        assert!(r.delete_post(p.id).unwrap() == false);
        assert!(r.get_post(p.id).is_err());
        assert_eq!(r.count_posts().unwrap(), 0);
    }

    #[test]
    fn tables_start_empty() {
        let r = repo();
        assert_eq!(r.count_posts().unwrap(), 0);
    }
}
