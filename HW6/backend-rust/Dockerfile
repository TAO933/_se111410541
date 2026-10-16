# ── 建置階段（需 Rust ≥1.85 才支援 edition2024 的 hyper-util） ──
FROM rust:bookworm AS builder
WORKDIR /app
COPY Cargo.toml ./
COPY src ./src
RUN cargo build --release

# ── 執行階段（小體積） ──
FROM debian:bookworm-slim
WORKDIR /app
COPY --from=builder /app/target/release/micro-rust /app/micro-rust
EXPOSE 8000
CMD ["/app/micro-rust"]
