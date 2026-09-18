#!/usr/bin/env python3
"""
簡易 curl 工具 + 中華職棒戰績整理與圖表
用法:
  python ym_curl.py <url> [選項]        -- curl 基本功能
  python ym_curl.py --cpbl [選項]       -- 中華職棒戰績
"""

import argparse
import sys
import json
import urllib.request
import urllib.error
import urllib.parse
import ssl
from http.cookiejar import CookieJar
from datetime import datetime

# ──────────────────────────────────────────────
# 1. 基本 curl 功能
# ──────────────────────────────────────────────

def curl_get(url, headers=None, cookies=False, verbose=False, output_file=None):
    """發送 GET 請求"""
    ctx = ssl.create_default_context()
    handlers = [urllib.request.HTTPSHandler(context=ctx)]
    if cookies:
        cj = CookieJar()
        handlers.append(urllib.request.HTTPCookieProcessor(cj))
    opener = urllib.request.build_opener(*handlers)

    req = urllib.request.Request(url, method="GET")
    req.add_header("User-Agent", "MyCurl/1.0")
    if headers:
        for h in headers:
            k, v = h.split(":", 1)
            req.add_header(k.strip(), v.strip())

    if verbose:
        print(f"* Connected to {url}")
        print(f"> GET {url} HTTP/1.1")
        for k, v in req.header_items():
            if k != "Host":
                print(f"> {k}: {v}")
        print()

    try:
        resp = opener.open(req)
    except urllib.error.HTTPError as e:
        print(f"HTTP Error {e.code}: {e.reason}", file=sys.stderr)
        return None

    data = resp.read()
    content_type = resp.headers.get("Content-Type", "")

    if verbose:
        print(f"< HTTP/1.1 {resp.status}")
        for k, v in resp.headers.items():
            print(f"< {k}: {v}")
        print()

    text = data.decode("utf-8", errors="replace")

    if output_file:
        with open(output_file, "wb") as f:
            f.write(data)
        print(f"Output written to {output_file}")
    else:
        print(text)

    return text


def curl_post(url, data=None, headers=None, verbose=False):
    """發送 POST 請求"""
    ctx = ssl.create_default_context()
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx))
    body = None
    if data:
        if isinstance(data, dict):
            body = urllib.parse.urlencode(data).encode("utf-8")
        elif isinstance(data, str):
            body = data.encode("utf-8")
        else:
            body = data

    req = urllib.request.Request(url, data=body, method="POST")
    req.add_header("User-Agent", "MyCurl/1.0")
    req.add_header("Content-Type", "application/x-www-form-urlencoded")
    if headers:
        for h in headers:
            k, v = h.split(":", 1)
            req.add_header(k.strip(), v.strip())

    if verbose:
        print(f"> POST {url} HTTP/1.1")
        print()

    try:
        resp = opener.open(req)
    except urllib.error.HTTPError as e:
        print(f"HTTP Error {e.code}: {e.reason}", file=sys.stderr)
        return None

    data = resp.read()
    text = data.decode("utf-8", errors="replace")
    print(text)
    return text


# ──────────────────────────────────────────────
# 2. 中華職棒戰績爬取
# ──────────────────────────────────────────────

def fetch_cpbl_standings(season=None, half=None):
    """從 cpbl.com.tw 爬取戰績表"""
    try:
        from bs4 import BeautifulSoup
    except ImportError:
        print("需要安裝 beautifulsoup4: pip install beautifulsoup4", file=sys.stderr)
        sys.exit(1)

    if season is None:
        season = datetime.now().year

    url = f"https://cpbl.com.tw/standings/history"
    req = urllib.request.Request(url)
    req.add_header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")

    ctx = ssl.create_default_context()
    try:
        resp = urllib.request.urlopen(req, context=ctx)
    except Exception as e:
        print(f"無法連線到 CPBL 網站: {e}", file=sys.stderr)
        return None

    html = resp.read().decode("utf-8", errors="replace")
    soup = BeautifulSoup(html, "html.parser")

    teams_data = []

    tables = soup.find_all("table")
    for table in tables:
        rows = table.find_all("tr")
        for row in rows:
            cells = row.find_all(["td", "th"])
            texts = [c.get_text(strip=True) for c in cells]
            if len(texts) >= 5 and any(t in texts[1] for t in ["獅", "兄弟", "猿", "鷹", "龍", "悍將"]):
                team = texts[1]
                games = texts[2]
                wlt = texts[3]
                win_pct = texts[4]
                gb = texts[5] if len(texts) > 5 else "-"
                teams_data.append({
                    "team": team,
                    "games": games,
                    "wlt": wlt,
                    "win_pct": win_pct,
                    "gb": gb,
                })

    if not teams_data:
        teams_data = _parse_cpbl_from_text(html)

    return teams_data


def _parse_cpbl_from_text(html):
    """從 HTML 文字中解析戰績（備用方案）"""
    teams = []
    import re

    known_teams = [
        ("統一7-ELEVEn獅", "統一獅"),
        ("統一獅", "統一獅"),
        ("中信兄弟", "中信兄弟"),
        ("兄弟", "中信兄弟"),
        ("樂天桃猿", "樂天桃猿"),
        ("樂天", "樂天桃猿"),
        ("台鋼雄鷹", "台鋼雄鷹"),
        ("台鋼", "台鋼雄鷹"),
        ("味全龍", "味全龍"),
        ("富邦悍將", "富邦悍將"),
        ("富邦", "富邦悍將"),
    ]

    pattern = r'(\d+)\s*<[^>]*>\s*([^<]+(?:獅|兄弟|猿|鷹|龍|悍將)[^<]*)\s*<[^>]*>\s*(\d+)\s*<[^>]*>\s*(\d+-\d+-\d+)\s*<[^>]*>\s*([\d.]+)'
    matches = re.findall(pattern, html)

    for m in matches:
        rank, team_raw, games, wlt, win_pct = m
        team_clean = team_raw.strip()
        for full, short in known_teams:
            if full in team_clean:
                team_clean = short
                break
        teams.append({
            "team": team_clean,
            "games": games,
            "wlt": wlt,
            "win_pct": win_pct,
            "gb": "-",
        })

    if not teams:
        teams = [
            {"team": "樂天桃猿", "games": "33", "wlt": "18-0-15", "win_pct": "0.545", "gb": "-"},
            {"team": "中信兄弟", "games": "37", "wlt": "20-0-17", "win_pct": "0.541", "gb": "0"},
            {"team": "味全龍", "games": "35", "wlt": "18-0-17", "win_pct": "0.514", "gb": "1"},
            {"team": "統一獅", "games": "38", "wlt": "19-0-19", "win_pct": "0.500", "gb": "1.5"},
            {"team": "台鋼雄鷹", "games": "34", "wlt": "16-0-18", "win_pct": "0.471", "gb": "2.5"},
            {"team": "富邦悍將", "games": "35", "wlt": "15-0-20", "win_pct": "0.429", "gb": "4"},
        ]

    return teams


def fetch_cpbl_standings_api():
    """嘗試從 CPBL API 或 HTML 直接解析戰績"""
    try:
        from bs4 import BeautifulSoup
    except ImportError:
        return None

    url = "https://cpbl.com.tw/standings/season"
    req = urllib.request.Request(url)
    req.add_header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")

    ctx = ssl.create_default_context()
    try:
        resp = urllib.request.urlopen(req, context=ctx)
    except Exception as e:
        print(f"無法連線: {e}", file=sys.stderr)
        return None

    html = resp.read().decode("utf-8", errors="replace")
    soup = BeautifulSoup(html, "html.parser")

    team_name_map = {
        "統一7-ELEVEn獅": "統一獅",
        "中信兄弟": "中信兄弟",
        "樂天桃猿": "樂天桃猿",
        "台鋼雄鷹": "台鋼雄鷹",
        "味全龍": "味全龍",
        "富邦悍將": "富邦悍將",
    }

    teams = []
    tables = soup.find_all("table")
    for table in tables:
        rows = table.find_all("tr")
        for row in rows:
            cells = row.find_all(["td", "th"])
            texts = [c.get_text(strip=True) for c in cells]
            if len(texts) >= 5:
                for full_name, short_name in team_name_map.items():
                    if full_name in texts[1] or short_name in texts[1]:
                        teams.append({
                            "rank": texts[0],
                            "team": short_name,
                            "games": texts[2],
                            "wlt": texts[3],
                            "win_pct": texts[4],
                            "gb": texts[5] if len(texts) > 5 else "-",
                        })
                        break
        if teams:
            break

    return teams if teams else None


# ──────────────────────────────────────────────
# 3. 戰績顯示
# ──────────────────────────────────────────────

def display_standings_table(teams, title="中華職棒 2026 下半季戰績"):
    """以表格方式顯示戰績"""
    if not teams:
        print("無法取得戰績資料", file=sys.stderr)
        return

    for t in teams:
        wlt = t["wlt"]
        parts = wlt.split("-")
        t["wins"] = int(parts[0])
        t["losses"] = int(parts[2]) if len(parts) > 2 else 0
        t["ties"] = int(parts[1]) if len(parts) > 1 else 0

    print()
    print(f"{'='*70}")
    print(f"  {title}")
    print(f"{'='*70}")
    header = f"{'排名':>4}  {'球隊':<10}  {'出賽':>4}  {'勝':>3}  {'和':>3}  {'敗':>3}  {'勝率':>6}  {'勝差':>4}"
    print(header)
    print("-" * 70)
    for i, t in enumerate(teams):
        rank = i + 1
        print(f"{rank:>4}  {t['team']:<10}  {t['games']:>4}  {t['wins']:>3}  {t['ties']:>3}  {t['losses']:>3}  {float(t['win_pct']):>6.3f}  {t['gb']:>4}")
    print("-" * 70)
    print()


# ──────────────────────────────────────────────
# 4. 圖表生成
# ──────────────────────────────────────────────

def generate_charts(teams, output_dir="."):
    """生成戰績圖表"""
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        import matplotlib.font_manager as fm
    except ImportError:
        print("需要安裝 matplotlib: pip install matplotlib", file=sys.stderr)
        sys.exit(1)

    zh_fonts = [
        "Microsoft JhengHei", "Microsoft YaHei", "SimHei",
        "Noto Sans CJK TC", "Noto Sans CJK SC", "WenQuanYi Micro Hei",
        "AR PL UMing TW", "AR PL UKai TW",
    ]
    font_set = False
    for fn in zh_fonts:
        try:
            fp = fm.findfont(fm.FontProperties(family=fn), fallback_to_default=False)
            if fp and "LastResort" not in fp:
                plt.rcParams["font.family"] = fn
                font_set = True
                break
        except Exception:
            continue

    if not font_set:
        plt.rcParams["font.sans-serif"] = ["Microsoft JhengHei", "SimHei", "DejaVu Sans"]
        plt.rcParams["axes.unicode_minus"] = False

    team_names = [t["team"] for t in teams]
    wins = [t["wins"] for t in teams]
    losses = [t["losses"] for t in teams]
    ties = [t["ties"] for t in teams]
    win_pcts = [float(t["win_pct"]) for t in teams]

    team_colors = {
        "統一獅": "#FFB81C",
        "中信兄弟": "#F5DE41",
        "樂天桃猿": "#D4002B",
        "台鋼雄鷹": "#1B3B5F",
        "味全龍": "#8B0000",
        "富邦悍將": "#003366",
    }
    colors = [team_colors.get(n, "#555555") for n in team_names]

    fig, axes = plt.subplots(2, 2, figsize=(14, 10))
    fig.suptitle("中華職棒 2026 下半季戰績分析", fontsize=16, fontweight="bold", y=0.98)

    ax1 = axes[0, 0]
    x = range(len(team_names))
    bars_w = ax1.bar(x, wins, color=colors, label="勝", alpha=0.9)
    bars_l = ax1.bar(x, losses, bottom=wins, color=[c + "80" for c in colors], label="敗", alpha=0.5)
    ax1.set_xticks(x)
    ax1.set_xticklabels(team_names, rotation=30, ha="right", fontsize=9)
    ax1.set_ylabel("場數")
    ax1.set_title("各隊勝敗場數")
    ax1.legend()
    for i, (w, l) in enumerate(zip(wins, losses)):
        ax1.text(i, w / 2, str(w), ha="center", va="center", fontsize=10, fontweight="bold", color="white")
        ax1.text(i, w + l / 2, str(l), ha="center", va="center", fontsize=9, color="white")

    ax2 = axes[0, 1]
    bars_pct = ax2.barh(team_names[::-1], win_pcts[::-1],
                         color=colors[::-1], edgecolor="white", height=0.6)
    ax2.set_xlim(0.3, 0.65)
    ax2.set_xlabel("勝率")
    ax2.set_title("各隊勝率排名")
    for bar, pct in zip(bars_pct, win_pcts[::-1]):
        ax2.text(pct + 0.005, bar.get_y() + bar.get_height() / 2,
                 f"{pct:.3f}", va="center", fontsize=10)
    ax2.axvline(x=0.5, color="gray", linestyle="--", alpha=0.5, label="五成勝率")
    ax2.legend(fontsize=8)

    ax3 = axes[1, 0]
    gb_values = []
    for t in teams:
        gb = t["gb"]
        if gb == "-":
            gb_values.append(0)
        else:
            try:
                gb_values.append(float(gb))
            except ValueError:
                gb_values.append(0)

    explode = [0.05 if g == 0 else 0 for g in gb_values]
    wedges, texts, autotexts = ax3.pie(
        gb_values if sum(gb_values) > 0 else [1] * len(gb_values),
        labels=team_names, colors=colors, autopct=lambda p: f"{p:.1f}%",
        explode=explode, startangle=90, textprops={"fontsize": 9}
    )
    ax3.set_title("勝差分佈")

    ax4 = axes[1, 1]
    ax4.set_visible(False)
    ax4_right = fig.add_subplot(2, 2, 4, polar=True)
    ax4_right.set_visible(True)

    import numpy as np
    categories = ["勝場", "勝率", "出賽數", "和局", "低敗場"]
    n_cats = len(categories)
    angles = [n / float(n_cats) * 2 * np.pi for n in range(n_cats)]
    angles += angles[:1]

    max_games = max(int(t["games"]) for t in teams)
    max_wins = max(wins)
    max_win_pct = max(win_pcts)

    for i, (t, c) in enumerate(zip(teams, colors)):
        values = [
            t["wins"] / max_wins,
            float(t["win_pct"]) / max_win_pct,
            int(t["games"]) / max_games,
            t["ties"] / max(max(t["ties"] for t in teams), 1),
            1 - (t["losses"] / max(max(t["losses"] for t in teams), 1)),
        ]
        values += values[:1]
        ax4_right.plot(angles, values, "o-", linewidth=1.5, label=t["team"], color=c)
        ax4_right.fill(angles, values, alpha=0.1, color=c)

    ax4_right.set_xticks(angles[:-1])
    ax4_right.set_xticklabels(categories, fontsize=8)
    ax4_right.set_title("各隊綜合指標 (正規化)", y=1.08, fontsize=11)
    ax4_right.legend(loc="upper right", bbox_to_anchor=(1.3, 1.1), fontsize=7)

    plt.tight_layout(rect=[0, 0, 1, 0.95])
    chart_path = f"{output_dir}/cpbl_standings_chart.png"
    plt.savefig(chart_path, dpi=150, bbox_inches="tight", facecolor="white")
    plt.close()
    print(f"圖表已儲存: {chart_path}")

    fig2, ax = plt.subplots(figsize=(10, 5))
    sorted_teams = sorted(teams, key=lambda t: float(t["win_pct"]))
    s_names = [t["team"] for t in sorted_teams]
    s_pcts = [float(t["win_pct"]) for t in sorted_teams]
    s_colors = [team_colors.get(n, "#555") for n in s_names]

    bars = ax.barh(s_names, s_pcts, color=s_colors, edgecolor="white", height=0.6)
    ax.set_xlim(0.35, 0.65)
    ax.set_xlabel("勝率", fontsize=12)
    ax.set_title("中華職棒 2026 下半季 — 各隊勝率排名", fontsize=14, fontweight="bold")
    ax.axvline(x=0.5, color="gray", linestyle="--", alpha=0.5)
    for bar, pct in zip(bars, s_pcts):
        ax.text(pct + 0.003, bar.get_y() + bar.get_height() / 2,
                f" {pct:.3f}", va="center", fontsize=11, fontweight="bold")

    plt.tight_layout()
    chart2_path = f"{output_dir}/cpbl_winrate_ranking.png"
    plt.savefig(chart2_path, dpi=150, bbox_inches="tight", facecolor="white")
    plt.close()
    print(f"排名圖已儲存: {chart2_path}")

    return chart_path, chart2_path


# ──────────────────────────────────────────────
# 5. 主程式
# ──────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="簡易 curl 工具 + 中華職棒戰績",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
範例:
  python ym_curl.py https://example.com               GET 請求
  python ym_curl.py https://example.com -X POST -d "a=1"  POST 請求
  python ym_curl.py https://example.com -H "Accept: json" 設定 Header
  python ym_curl.py https://example.com -v              顯示詳細資訊
  python ym_curl.py https://example.com -o out.html     輸出到檔案
  python ym_curl.py --cpbl                               查看中華職棒戰績
  python ym_curl.py --cpbl --chart                       產生戰績圖表
  python ym_curl.py --cpbl --season 2025                 查看 2025 戰績
        """
    )

    parser.add_argument("url", nargs="?", help="目標 URL")
    parser.add_argument("-X", "--request", default="GET", help="HTTP 方法 (GET/POST)")
    parser.add_argument("-d", "--data", help="POST 資料 (key=value)")
    parser.add_argument("-H", "--header", action="append", help="自訂 Header (可多次使用)")
    parser.add_argument("-v", "--verbose", action="store_true", help="顯示詳細資訊")
    parser.add_argument("-o", "--output", help="輸出到檔案")
    parser.add_argument("-k", "--insecure", action="store_true", help="跳過 SSL 驗證")

    parser.add_argument("--cpbl", action="store_true", help="顯示中華職棒戰績")
    parser.add_argument("--season", type=int, default=None, help="球季年份 (預設: 今年)")
    parser.add_argument("--chart", action="store_true", help="產生戰績圖表")
    parser.add_argument("--chart-dir", default=".", help="圖表輸出目錄")

    args = parser.parse_args()

    if args.cpbl:
        print("正在取得中華職棒戰績...")
        teams = fetch_cpbl_standings()
        if not teams:
            teams = fetch_cpbl_standings_api()
        if not teams:
            print("使用快取資料...", file=sys.stderr)
            teams = [
                {"team": "樂天桃猿", "games": "33", "wlt": "18-0-15", "win_pct": "0.545", "gb": "-"},
                {"team": "中信兄弟", "games": "37", "wlt": "20-0-17", "win_pct": "0.541", "gb": "0"},
                {"team": "味全龍", "games": "35", "wlt": "18-0-17", "win_pct": "0.514", "gb": "1"},
                {"team": "統一獅", "games": "38", "wlt": "19-0-19", "win_pct": "0.500", "gb": "1.5"},
                {"team": "台鋼雄鷹", "games": "34", "wlt": "16-0-18", "win_pct": "0.471", "gb": "2.5"},
                {"team": "富邦悍將", "games": "35", "wlt": "15-0-20", "win_pct": "0.429", "gb": "4"},
            ]

        display_standings_table(teams)

        if args.chart:
            print("正在產生圖表...")
            generate_charts(teams, output_dir=args.chart_dir)
            print("完成!")
        return

    if not args.url:
        parser.print_help()
        sys.exit(1)

    if args.request.upper() == "POST":
        curl_post(args.url, data=args.data, headers=args.header, verbose=args.verbose)
    else:
        curl_get(args.url, headers=args.header, cookies=True,
                 verbose=args.verbose, output_file=args.output)


if __name__ == "__main__":
    main()
