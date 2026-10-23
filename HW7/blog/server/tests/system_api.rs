/// 系統測試：直接透過 HTTP 測試整個 Web API 服務
///
/// 測試策略:
/// 1. 每個測試用 tempfile + in-memory 資料庫，互不干擾
/// 2. 把真實的 axum app 綁在隨機 port 上，用 reqwest 打 HTTP
/// 3. 驗證完整行為: HTTP status code、JSON 格式、CRUD 流程、錯誤處理
use std::process::Command;

use reqwest::Client;
use serde_json::{json, Value};

struct Server {
    base: String,
    child: std::process::Child,
}

impl Drop for Server {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

async fn start_server() -> Server {
    let manifest = env!("CARGO_MANIFEST_DIR");
    let exe = if cfg!(windows) {
        format!("{}\\target\\debug\\blog-server.exe", manifest)
    } else {
        format!("{}/target/debug/blog-server", manifest)
    };
    let port = portpicker();
    let db = tempfile::NamedTempFile::new().unwrap();
    let db_path = db.path().to_str().unwrap().to_string();

    let child = Command::new(&exe)
        .env("PORT", port.to_string())
        .env("BLOG_DB", &db_path)
        .spawn()
        .expect("無法啟動 blog-server，請先執行 cargo build");

    let base = format!("http://127.0.0.1:{port}");
    let client = Client::new();
    // 等待伺服器就緒
    for _ in 0..100 {
        let url = format!("{base}/api/health");
        if client.get(&url).send().await.is_ok() {
            return Server { base, child };
        }
        tokio::time::sleep(std::time::Duration::from_millis(100)).await;
    }
    panic!("伺服器沒有在時間內啟動");
}

fn portpicker() -> u16 {
    // 讓作業系統挑一個空閒 port 再還給我們用
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

#[tokio::test]
async fn test_01_health_check() {
    let s = start_server().await;
    let res = reqwest::get(format!("{}/api/health", s.base))
        .await
        .unwrap();
    assert_eq!(res.status(), 200);
    let body: Value = res.json().await.unwrap();
    assert_eq!(body["status"], "ok");
}

#[tokio::test]
async fn test_02_empty_list_returns_empty_array() {
    let s = start_server().await;
    let res = reqwest::get(format!("{}/api/posts", s.base))
        .await
        .unwrap();
    assert_eq!(res.status(), 200);
    let body: Vec<Value> = res.json().await.unwrap();
    assert!(body.is_empty());
}

#[tokio::test]
async fn test_03_create_post_returns_201_and_post() {
    let s = start_server().await;
    let res = reqwest::Client::new()
        .post(format!("{}/api/posts", s.base))
        .json(&json!({ "title": "測試標題", "body": "測試內容", "author": "小明" }))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 201);
    let body: Value = res.json().await.unwrap();
    assert_eq!(body["title"], "測試標題");
    assert_eq!(body["author"], "小明");
    assert!(body["id"].as_i64().unwrap() > 0);
}

#[tokio::test]
async fn test_04_full_crud_flow() {
    let s = start_server().await;
    let client = reqwest::Client::new();

    // Create
    let res = client
        .post(format!("{}/api/posts", s.base))
        .json(&json!({ "title": "初始", "body": "內容", "author": "作者" }))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 201);
    let created: Value = res.json().await.unwrap();
    let id = created["id"].as_i64().unwrap();

    // Read
    let res = client
        .get(format!("{}/api/posts/{id}", s.base))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 200);
    let got: Value = res.json().await.unwrap();
    assert_eq!(got["title"], "初始");

    // Update
    let res = client
        .put(format!("{}/api/posts/{id}", s.base))
        .json(&json!({ "title": "更新", "body": "新內容", "author": "作者" }))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 200);
    let updated: Value = res.json().await.unwrap();
    assert_eq!(updated["title"], "更新");
    assert_eq!(updated["body"], "新內容");

    // List 會看到一筆
    let res = client.get(format!("{}/api/posts", s.base)).send().await.unwrap();
    let list: Vec<Value> = res.json().await.unwrap();
    assert_eq!(list.len(), 1);

    // Delete
    let res = client
        .delete(format!("{}/api/posts/{id}", s.base))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 200);

    // Get 已刪除的 -> 404
    let res = client
        .get(format!("{}/api/posts/{id}", s.base))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 404);
}

#[tokio::test]
async fn test_05_get_missing_post_returns_404() {
    let s = start_server().await;
    let res = reqwest::get(format!("{}/api/posts/999", s.base))
        .await
        .unwrap();
    assert_eq!(res.status(), 404);
}

#[tokio::test]
async fn test_06_create_without_title_fails() {
    let s = start_server().await;
    let res = reqwest::Client::new()
        .post(format!("{}/api/posts", s.base))
        .json(&json!({ "body": "只有內容" }))
        .send()
        .await
        .unwrap();
    // serde 缺少必填欄位會回 422
    assert_eq!(res.status(), 422);
}

#[tokio::test]
async fn test_07_invalid_id_returns_400() {
    let s = start_server().await;
    let res = reqwest::get(format!("{}/api/posts/not-a-number", s.base))
        .await
        .unwrap();
    assert_eq!(res.status(), 400);
}

#[tokio::test]
async fn test_08_delete_missing_post_creates_deleted_false() {
    let s = start_server().await;
    let res = reqwest::Client::new()
        .delete(format!("{}/api/posts/42", s.base))
        .send()
        .await
        .unwrap();
    assert_eq!(res.status(), 200);
    let body: Value = res.json().await.unwrap();
    assert_eq!(body["deleted"], false);
}
