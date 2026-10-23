const API_BASE = window.location.port === "3000"
  ? "http://localhost:8080/api"
  : "/api";

const postList = document.getElementById("post-list");
const emptyMsg = document.getElementById("empty-msg");
const postCount = document.getElementById("post-count");
const form = document.getElementById("post-form");
const formError = document.getElementById("form-error");

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderPost(post) {
  const card = el("article", "post");
  card.dataset.testid = "post-item";
  card.dataset.postId = post.id;
  card.appendChild(el("div", "post-title", post.title));
  const meta = post.author ? `${post.author} · ${post.created_at}` : post.created_at;
  card.appendChild(el("div", "post-meta", meta));
  card.appendChild(el("div", "post-body", post.body));
  const del = el("button", "delete-btn", "刪除");
  del.dataset.testid = "delete-btn";
  del.addEventListener("click", async () => {
    await fetch(`${API_BASE}/posts/${post.id}`, { method: "DELETE" });
    load();
  });
  card.appendChild(del);
  return card;
}

async function load() {
  const posts = await fetch(`${API_BASE}/posts`).then((r) => r.json());
  postList.innerHTML = "";
  posts.forEach((p) => postList.appendChild(renderPost(p)));
  emptyMsg.hidden = posts.length > 0;
  postCount.textContent = `(${posts.length})`;
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  formError.hidden = true;
  const payload = {
    title: form.title.value.trim(),
    body: form.body.value.trim(),
    author: form.author.value.trim() || "匿名",
  };
  const res = await fetch(`${API_BASE}/posts`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    formError.textContent = "發表失敗，請確認 API 是否啟動";
    formError.hidden = false;
    return;
  }
  form.reset();
  load();
});

load();
