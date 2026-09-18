import { DatabaseSync } from 'node:sqlite';
import { readFileSync, existsSync, unlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const DB_PATH = join(__dirname, 'school.db');
const SCHEMA_PATH = join(__dirname, 'schema.sql');

// 成績換算邏輯需與 index.html seedDB() 完全一致
const _g = (sid, cid, r, m, f) => ({ studentId: sid, courseId: cid, regular: r, midterm: m, final: f, score: Math.round(r * 0.3 + m * 0.3 + f * 0.4) });
const _c = (s) => ({ r: Math.max(0, s - 2), m: Math.min(100, s + 2), f: Math.max(0, s - 1) });
const G = (sid, cid, s) => { const v = _c(s); return _g(sid, cid, v.r, v.m, v.f); };

const users = [
  { id: "admin", account: "admin", password: "admin123", role: "admin", name: "張主任", dept: "教務處", email: "admin@school.edu.tw", phone: "02-27712171", birthday: "1975-06-12", address: "台北市忠孝東路一段1號", advisorId: null },
  { id: "t001", account: "t001", password: "123456", role: "teacher", name: "陳志明", dept: "資訊工程系", email: "chen@school.edu.tw", phone: "0912-345678", birthday: "1980-03-15", address: "台北市大安區復興南路一段100號", advisorId: null },
  { id: "t002", account: "t002", password: "123456", role: "teacher", name: "李雅婷", dept: "數學系", email: "lee@school.edu.tw", phone: "0923-456789", birthday: "1985-07-22", address: "新北市板橋區文化路二段50號", advisorId: null },
  { id: "t003", account: "t003", password: "123456", role: "teacher", name: "黃美玲", dept: "外國語文系", email: "huang@school.edu.tw", phone: "0934-567890", birthday: "1988-11-30", address: "台北市士林區中山北路五段200號", advisorId: null },
  { id: "t004", account: "t004", password: "123456", role: "teacher", name: "林建國", dept: "體育系", email: "lin@school.edu.tw", phone: "0945-678901", birthday: "1978-01-08", address: "台北市松山區南京東路四段300號", advisorId: null },
  { id: "s001", account: "s001", password: "123456", role: "student", name: "王小明", dept: "資工二年", email: "s001@school.edu.tw", phone: "0911-111001", birthday: "2005-02-14", address: "台北市信義區松仁路88號5樓", advisorId: "t001" },
  { id: "s002", account: "s002", password: "123456", role: "student", name: "林小美", dept: "資工二年", email: "s002@school.edu.tw", phone: "0911-111002", birthday: "2005-05-20", address: "台北市內湖區江南街71巷10號", advisorId: "t001" },
  { id: "s003", account: "s003", password: "123456", role: "student", name: "陳大仁", dept: "資工二年", email: "s003@school.edu.tw", phone: "0911-111003", birthday: "2004-12-03", address: "新北市中和區景平路456號", advisorId: "t001" },
  { id: "s004", account: "s004", password: "123456", role: "student", name: "張小華", dept: "資工二年", email: "s004@school.edu.tw", phone: "0911-111004", birthday: "2005-08-27", address: "台北市文山區興隆路三段22號", advisorId: "t002" },
  { id: "s005", account: "s005", password: "123456", role: "student", name: "劉志偉", dept: "資工二年", email: "s005@school.edu.tw", phone: "0911-111005", birthday: "2005-01-11", address: "新北市新店區北新路一段78號", advisorId: "t002" },
  { id: "s006", account: "s006", password: "123456", role: "student", name: "蔡欣怡", dept: "資工一年", email: "s006@school.edu.tw", phone: "0911-111006", birthday: "2006-04-19", address: "台北市北投區光明路165號", advisorId: "t003" },
  { id: "s007", account: "s007", password: "123456", role: "student", name: "謝明哲", dept: "資工一年", email: "s007@school.edu.tw", phone: "0911-111007", birthday: "2006-09-25", address: "台北市大同區承德路三段90號", advisorId: "t003" },
  { id: "s008", account: "s008", password: "123456", role: "student", name: "韓佳玲", dept: "資工一年", email: "s008@school.edu.tw", phone: "0911-111008", birthday: "2006-07-07", address: "新北市土城區中央路一段200號", advisorId: "t003" },
  { id: "s009", account: "s009", password: "123456", role: "student", name: "賴俊宏", dept: "資工一年", email: "s009@school.edu.tw", phone: "0911-111009", birthday: "2006-03-16", address: "台北市中山區民生東路二段120號", advisorId: "t004" },
  { id: "s010", account: "s010", password: "123456", role: "student", name: "許雅筑", dept: "資工一年", email: "s010@school.edu.tw", phone: "0911-111010", birthday: "2006-10-29", address: "台北市南港區研究院路二段60號", advisorId: "t004" },
];

const courses = [
  { id: "C001", code: "CS101", name: "程式設計(一)", teacherId: "t001", credits: 3, semester: "114-1", time: "週一 09:00-12:00", room: "資102", capacity: 60, description: "C 語言基礎與邏輯訓練" },
  { id: "C002", code: "CS201", name: "資料結構", teacherId: "t001", credits: 3, semester: "114-1", time: "週二 09:00-12:00", room: "資102", capacity: 55, description: "陣列、鏈結串列、樹、圖" },
  { id: "C003", code: "MATH101", name: "微積分", teacherId: "t002", credits: 3, semester: "114-1", time: "週三 09:00-12:00", room: "理201", capacity: 80, description: "極限、微分、積分" },
  { id: "C004", code: "ENG101", name: "大一英文", teacherId: "t003", credits: 2, semester: "114-1", time: "週四 10:00-12:00", room: "文305", capacity: 50, description: "英文閱讀與寫作" },
  { id: "C005", code: "CS301", name: "資料庫系統", teacherId: "t001", credits: 3, semester: "114-1", time: "週五 09:00-12:00", room: "資103", capacity: 55, description: "SQL 與資料庫設計" },
  { id: "C006", code: "PE101", name: "體育", teacherId: "t004", credits: 1, semester: "114-1", time: "週二 14:00-16:00", room: "操場", capacity: 60, description: "體適能" },
  { id: "C007", code: "STAT201", name: "統計學", teacherId: "t002", credits: 3, semester: "114-1", time: "週四 13:00-16:00", room: "理202", capacity: 60, description: "機率與統計推論" },
  { id: "C008", code: "ENG201", name: "英文會話", teacherId: "t003", credits: 2, semester: "114-1", time: "週五 13:00-15:00", room: "文306", capacity: 45, description: "口說與聽力訓練" },
  { id: "H001", code: "CS100", name: "計算機概論", teacherId: "t001", credits: 3, semester: "113-1", time: "週一 09:00-12:00", room: "資102", capacity: 60, description: "已結算" },
  { id: "H002", code: "MATH100", name: "線性代數", teacherId: "t002", credits: 3, semester: "113-1", time: "週三 09:00-12:00", room: "理201", capacity: 80, description: "已結算" },
  { id: "H003", code: "CS102", name: "程式設計(二)", teacherId: "t001", credits: 3, semester: "113-2", time: "週一 09:00-12:00", room: "資102", capacity: 60, description: "已結算" },
  { id: "H004", code: "CS103", name: "離散數學", teacherId: "t002", credits: 3, semester: "113-2", time: "週四 09:00-12:00", room: "理201", capacity: 80, description: "已結算" },
];

const enrollments = [
  { studentId: "s001", courseId: "C001" }, { studentId: "s001", courseId: "C003" }, { studentId: "s001", courseId: "C005" },
  { studentId: "s002", courseId: "C001" }, { studentId: "s002", courseId: "C002" }, { studentId: "s002", courseId: "C004" },
  { studentId: "s003", courseId: "C002" }, { studentId: "s003", courseId: "C003" }, { studentId: "s003", courseId: "C006" },
  { studentId: "s004", courseId: "C001" }, { studentId: "s004", courseId: "C004" }, { studentId: "s004", courseId: "C007" },
  { studentId: "s005", courseId: "C002" }, { studentId: "s005", courseId: "C005" }, { studentId: "s005", courseId: "C006" },
  { studentId: "s006", courseId: "C003" }, { studentId: "s006", courseId: "C004" }, { studentId: "s006", courseId: "C008" },
  { studentId: "s007", courseId: "C001" }, { studentId: "s007", courseId: "C006" }, { studentId: "s007", courseId: "C007" },
  { studentId: "s008", courseId: "C004" }, { studentId: "s008", courseId: "C005" }, { studentId: "s008", courseId: "C008" },
  { studentId: "s009", courseId: "C002" }, { studentId: "s009", courseId: "C007" }, { studentId: "s009", courseId: "C008" },
  { studentId: "s010", courseId: "C003" }, { studentId: "s010", courseId: "C005" }, { studentId: "s010", courseId: "C006" },
  { studentId: "s001", courseId: "H001" }, { studentId: "s001", courseId: "H002" }, { studentId: "s001", courseId: "H003" }, { studentId: "s001", courseId: "H004" },
  { studentId: "s002", courseId: "H001" }, { studentId: "s002", courseId: "H002" }, { studentId: "s002", courseId: "H003" }, { studentId: "s002", courseId: "H004" },
  { studentId: "s003", courseId: "H001" }, { studentId: "s003", courseId: "H002" }, { studentId: "s003", courseId: "H003" }, { studentId: "s003", courseId: "H004" },
  { studentId: "s004", courseId: "H001" }, { studentId: "s004", courseId: "H002" }, { studentId: "s004", courseId: "H003" }, { studentId: "s004", courseId: "H004" },
  { studentId: "s005", courseId: "H001" }, { studentId: "s005", courseId: "H002" }, { studentId: "s005", courseId: "H003" }, { studentId: "s005", courseId: "H004" },
  { studentId: "s006", courseId: "H001" }, { studentId: "s006", courseId: "H002" },
  { studentId: "s007", courseId: "H001" }, { studentId: "s007", courseId: "H002" },
  { studentId: "s008", courseId: "H001" }, { studentId: "s008", courseId: "H002" },
  { studentId: "s009", courseId: "H001" }, { studentId: "s009", courseId: "H002" },
  { studentId: "s010", courseId: "H001" }, { studentId: "s010", courseId: "H002" },
];

const grades = [
  G("s001", "H001", 88), G("s001", "H002", 76), G("s001", "H003", 92), G("s001", "H004", 81),
  G("s002", "H001", 95), G("s002", "H002", 89), G("s002", "H003", 90), G("s002", "H004", 84),
  G("s003", "H001", 72), G("s003", "H002", 68), G("s003", "H003", 75), G("s003", "H004", 70),
  G("s004", "H001", 83), G("s004", "H002", 79), G("s004", "H003", 86), G("s004", "H004", 82),
  G("s005", "H001", 65), G("s005", "H002", 71), G("s005", "H003", 69), G("s005", "H004", 74),
  G("s006", "H001", 91), G("s006", "H002", 87),
  G("s007", "H001", 78), G("s007", "H002", 82),
  G("s008", "H001", 86), G("s008", "H002", 90),
  G("s009", "H001", 73), G("s009", "H002", 77),
  G("s010", "H001", 89), G("s010", "H002", 84),
  G("s001", "C001", 85), G("s002", "C001", 90), G("s004", "C001", 78), G("s007", "C001", 88),
];

const attendance = [
  { id: "A1", studentId: "s001", courseId: "C001", date: "2026-09-01", status: "出席", note: "" },
  { id: "A2", studentId: "s001", courseId: "C001", date: "2026-09-08", status: "遲到", note: "交通延誤" },
  { id: "A3", studentId: "s001", courseId: "C003", date: "2026-09-03", status: "出席", note: "" },
  { id: "A4", studentId: "s002", courseId: "C001", date: "2026-09-01", status: "出席", note: "" },
  { id: "A5", studentId: "s002", courseId: "C001", date: "2026-09-08", status: "缺席", note: "未請假" },
  { id: "A6", studentId: "s002", courseId: "C002", date: "2026-09-02", status: "請假", note: "病假" },
  { id: "A7", studentId: "s004", courseId: "C001", date: "2026-09-01", status: "出席", note: "" },
  { id: "A8", studentId: "s004", courseId: "C001", date: "2026-09-08", status: "出席", note: "" },
  { id: "A9", studentId: "s005", courseId: "C002", date: "2026-09-02", status: "遲到", note: "" },
  { id: "A10", studentId: "s006", courseId: "C004", date: "2026-09-04", status: "出席", note: "" },
  { id: "A11", studentId: "s003", courseId: "C006", date: "2026-09-02", status: "出席", note: "" },
  { id: "A12", studentId: "s007", courseId: "C007", date: "2026-09-04", status: "請假", note: "事假" },
  { id: "A13", studentId: "s008", courseId: "C008", date: "2026-09-05", status: "出席", note: "" },
  { id: "A14", studentId: "s010", courseId: "C003", date: "2026-09-03", status: "缺席", note: "未請假" },
  { id: "A15", studentId: "s005", courseId: "C002", date: "2026-09-09", status: "請假", note: "請假申請通過" },
  { id: "A16", studentId: "s005", courseId: "C005", date: "2026-09-09", status: "請假", note: "請假申請通過" },
  { id: "A17", studentId: "s005", courseId: "C006", date: "2026-09-09", status: "請假", note: "請假申請通過" },
];

const leaves = [
  { id: "L1", studentId: "s002", date: "2026-09-15", reason: "感冒就醫", status: "待審核", applyDate: "2026-09-12", reviewer: null, reviewNote: "" },
  { id: "L2", studentId: "s005", date: "2026-09-09", reason: "家中急事", status: "通過", applyDate: "2026-09-08", reviewer: "李雅婷", reviewNote: "准假" },
];

const rewards = [
  { id: "R1", studentId: "s002", type: "嘉獎", date: "2026-09-05", reason: "服務學習表現優良", recorder: "陳志明" },
  { id: "R2", studentId: "s001", type: "小功", date: "2026-09-06", reason: "程式競賽獲獎", recorder: "陳志明" },
  { id: "R3", studentId: "s005", type: "警告", date: "2026-09-07", reason: "上課遲到多次", recorder: "李雅婷" },
  { id: "R4", studentId: "s008", type: "嘉獎", date: "2026-09-08", reason: "英文演講比賽佳作", recorder: "黃美玲" },
];

const announcements = [
  { id: "N1", title: "【重要】114-1 學期加退選時程公告", content: "加退選期間：9/8 – 9/19，請同學至選課中心完成加退選，逾期不受理。", authorId: "admin", authorName: "張主任", date: "2026-09-05", pinned: 1 },
  { id: "N2", title: "程式設計(一) 第一週上課提醒", content: "請修課同學攜帶筆電，第一週介紹課程大綱與評分方式。", authorId: "t001", authorName: "陳志明", date: "2026-09-01", pinned: 0 },
  { id: "N3", title: "期中考週圖書館延長開放", content: "10/20–10/26 圖書館開放至 23:00，歡迎多加利用。", authorId: "admin", authorName: "張主任", date: "2026-09-10", pinned: 0 },
  { id: "N4", title: "英文會話課程分組名單公布", content: "修習英文會話的同學請至文306外公布欄確認分組名單。", authorId: "t003", authorName: "黃美玲", date: "2026-09-11", pinned: 0 },
];

if (existsSync(DB_PATH)) unlinkSync(DB_PATH);
const db = new DatabaseSync(DB_PATH);
db.exec(readFileSync(SCHEMA_PATH, 'utf8'));

const insertUser = db.prepare(`INSERT INTO users (id, account, password, role, name, dept, email, phone, birthday, address, advisorId) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);
const insertCourse = db.prepare(`INSERT INTO courses (id, code, name, teacherId, credits, semester, time, room, capacity, description) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);
const insertEnroll = db.prepare(`INSERT INTO enrollments (studentId, courseId) VALUES (?, ?)`);
const insertGrade = db.prepare(`INSERT INTO grades (studentId, courseId, regular, midterm, final, score) VALUES (?, ?, ?, ?, ?, ?)`);
const insertAtt = db.prepare(`INSERT INTO attendance (id, studentId, courseId, date, status, note) VALUES (?, ?, ?, ?, ?, ?)`);
const insertLeave = db.prepare(`INSERT INTO leaves (id, studentId, date, reason, status, applyDate, reviewer, reviewNote) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`);
const insertReward = db.prepare(`INSERT INTO rewards (id, studentId, type, date, reason, recorder) VALUES (?, ?, ?, ?, ?, ?)`);
const insertAnn = db.prepare(`INSERT INTO announcements (id, title, content, authorId, authorName, date, pinned) VALUES (?, ?, ?, ?, ?, ?, ?)`);

db.exec('BEGIN');
try {
  for (const u of users) insertUser.run(u.id, u.account, u.password, u.role, u.name, u.dept, u.email, u.phone, u.birthday, u.address, u.advisorId);
  for (const c of courses) insertCourse.run(c.id, c.code, c.name, c.teacherId, c.credits, c.semester, c.time, c.room, c.capacity, c.description);
  for (const e of enrollments) insertEnroll.run(e.studentId, e.courseId);
  for (const g of grades) insertGrade.run(g.studentId, g.courseId, g.regular, g.midterm, g.final, g.score);
  for (const a of attendance) insertAtt.run(a.id, a.studentId, a.courseId, a.date, a.status, a.note);
  for (const l of leaves) insertLeave.run(l.id, l.studentId, l.date, l.reason, l.status, l.applyDate, l.reviewer, l.reviewNote);
  for (const r of rewards) insertReward.run(r.id, r.studentId, r.type, r.date, r.reason, r.recorder);
  for (const n of announcements) insertAnn.run(n.id, n.title, n.content, n.authorId, n.authorName, n.date, n.pinned);
  db.exec('COMMIT');
} catch (e) {
  db.exec('ROLLBACK');
  throw e;
}

const counts = {};
for (const t of ['users', 'courses', 'enrollments', 'grades', 'attendance', 'leaves', 'rewards', 'announcements']) {
  counts[t] = db.prepare(`SELECT COUNT(*) AS c FROM ${t}`).get().c;
}
console.log(JSON.stringify({ db: DB_PATH, counts }, null, 2));
db.close();
