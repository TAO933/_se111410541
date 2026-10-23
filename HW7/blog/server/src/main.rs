use tower_http::cors::CorsLayer;

use blog_server::{make_router, shared_state, PostRepo};

#[tokio::main]
async fn main() {
    let db_path =
        std::env::var("BLOG_DB").unwrap_or_else(|_| "blog.db".to_string());
    let repo = PostRepo::new(&db_path).expect("無法開啟資料庫");
    let app = make_router(shared_state(repo)).layer(CorsLayer::permissive());

    let port = std::env::var("PORT").unwrap_or_else(|_| "8080".to_string());
    let addr = format!("0.0.0.0:{port}");
    let listener = tokio::net::TcpListener::bind(&addr)
        .await
        .expect("綁定 port 失敗");
    println!("Blog API listening on http://{addr}");
    axum::serve(listener, app).await.unwrap();
}
