use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Post {
    pub id: i64,
    pub title: String,
    pub body: String,
    pub author: String,
    pub created_at: String,
}

#[derive(Debug, Deserialize)]
pub struct NewPost {
    pub title: String,
    pub body: String,
    #[serde(default)]
    pub author: String,
}
