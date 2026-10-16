use axum::{routing::get, Json, Router};
use serde_json::{json, Value};
use std::net::SocketAddr;

async fn index() -> Json<Value> {
    Json(json!({
        "service": "rust",
        "runtime": rustc_version(),
        "message": "Hello from Rust 微服務 🦀",
        "time": chrono::Utc::now().to_rfc3339(),
        "endpoints": ["GET /", "GET /health"]
    }))
}

async fn health() -> Json<Value> {
    Json(json!({ "status": "ok", "service": "rust" }))
}

fn rustc_version() -> String {
    // 編譯期寫死版本字串，避免執行期依賴 rustc
    option_env!("RUSTC_VERSION").unwrap_or("rustc 1.78+").to_string()
}

#[tokio::main]
async fn main() {
    let app = Router::new().route("/", get(index)).route("/health", get(health));
    let port: u16 = std::env::var("PORT").unwrap_or("8000".into()).parse().unwrap_or(8000);
    let addr = SocketAddr::from(([0, 0, 0, 0], port));
    println!("[rust] listening on {addr}");
    axum::serve(tokio::net::TcpListener::bind(addr).await.unwrap(), app)
        .await
        .unwrap();
}
