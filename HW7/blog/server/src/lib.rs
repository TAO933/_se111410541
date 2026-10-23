pub mod api;
pub mod db;
pub mod models;

use std::sync::{Arc, Mutex};

pub use api::make_router;
pub use db::PostRepo;

pub fn shared_state(repo: PostRepo) -> api::SharedState {
    Arc::new(Mutex::new(repo))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shared_state_is_shareable() {
        let repo = PostRepo::new_in_memory().unwrap();
        let state = shared_state(repo);
        let cloned = state.clone();
        assert!(Arc::ptr_eq(&state, &cloned));
    }
}
