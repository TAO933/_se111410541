use std::sync::Mutex;

use axum::{
    extract::{Path, State},
    http::StatusCode,
    routing::get,
    Json, Router,
};
use rusqlite::Error as DbError;
use serde_json::json;

use crate::db::PostRepo;
use crate::models::{NewPost, Post};

pub type AppState = Mutex<PostRepo>;

pub type SharedState = std::sync::Arc<AppState>;

fn db_err(e: DbError) -> (StatusCode, Json<serde_json::Value>) {
    let status = match &e {
        DbError::QueryReturnedNoRows => StatusCode::NOT_FOUND,
        _ => StatusCode::INTERNAL_SERVER_ERROR,
    };
    (
        status,
        Json(json!({ "error": e.to_string() })),
    )
}

pub fn make_router(app: SharedState) -> Router {
    Router::new()
        .route("/api/health", get(health))
        .route("/api/posts", get(list_posts).post(create_post))
        .route(
            "/api/posts/{id}",
            get(get_post).put(update_post).delete(delete_post),
        )
        .with_state(app)
}

async fn health() -> Json<serde_json::Value> {
    Json(json!({ "status": "ok" }))
}

async fn list_posts(
    State(state): State<SharedState>,
) -> Result<Json<Vec<Post>>, (StatusCode, Json<serde_json::Value>)> {
    let repo = state.lock().unwrap();
    repo.list_posts().map(Json).map_err(db_err)
}

async fn create_post(
    State(state): State<SharedState>,
    Json(draft): Json<NewPost>,
) -> Result<(StatusCode, Json<Post>), (StatusCode, Json<serde_json::Value>)> {
    let repo = state.lock().unwrap();
    repo.create_post(draft)
        .map(|p| (StatusCode::CREATED, Json(p)))
        .map_err(db_err)
}

async fn get_post(
    State(state): State<SharedState>,
    Path(id): Path<i64>,
) -> Result<Json<Post>, (StatusCode, Json<serde_json::Value>)> {
    let repo = state.lock().unwrap();
    repo.get_post(id).map(Json).map_err(db_err)
}

async fn update_post(
    State(state): State<SharedState>,
    Path(id): Path<i64>,
    Json(draft): Json<NewPost>,
) -> Result<Json<Post>, (StatusCode, Json<serde_json::Value>)> {
    let repo = state.lock().unwrap();
    repo.update_post(id, draft).map(Json).map_err(db_err)
}

async fn delete_post(
    State(state): State<SharedState>,
    Path(id): Path<i64>,
) -> Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)> {
    let repo = state.lock().unwrap();
    repo.delete_post(id)
        .map(|deleted| Json(json!({ "deleted": deleted })))
        .map_err(db_err)
}
