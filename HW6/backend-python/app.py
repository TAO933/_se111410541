import os, platform, datetime
import psycopg2
from flask import Flask, jsonify

app = Flask(__name__)
DATABASE_URL = os.getenv("DATABASE_URL", "postgres://app:apppass@postgres:5432/appdb")
PORT = int(os.getenv("PORT", "5000"))

@app.get("/")
def index():
    return jsonify(
        service="python",
        runtime=platform.python_version(),
        message="Hello from Python 微服務 🐍",
        time=datetime.datetime.utcnow().isoformat() + "Z",
        endpoints=["GET /", "GET /health", "GET /db"],
    )

@app.get("/health")
def health():
    return jsonify(status="ok", service="python")

@app.get("/db")
def db():
    try:
        conn = psycopg2.connect(DATABASE_URL)
        cur = conn.cursor()
        cur.execute("INSERT INTO visits(service) VALUES(%s)", ("python",))
        conn.commit()
        cur.execute("SELECT COUNT(*) FROM visits")
        count = cur.fetchone()[0]
        cur.close(); conn.close()
        return jsonify(status="ok", db="postgres connected ✅", total_visits=count)
    except Exception as e:
        return jsonify(status="error", message=str(e)), 500

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=PORT)
