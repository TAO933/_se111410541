-- school-system SQLite schema
-- 對應 index.html seedDB() 的 8 張表，localStorage key schoolDB_v1 -> school.db
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS users (
  id        TEXT PRIMARY KEY,
  account   TEXT NOT NULL UNIQUE,
  password  TEXT NOT NULL,
  role      TEXT NOT NULL CHECK (role IN ('student','teacher','admin')),
  name      TEXT NOT NULL,
  dept      TEXT DEFAULT '',
  email     TEXT DEFAULT '',
  phone     TEXT DEFAULT '',
  birthday  TEXT DEFAULT '',
  address   TEXT DEFAULT '',
  advisorId TEXT REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS courses (
  id        TEXT PRIMARY KEY,
  code      TEXT NOT NULL UNIQUE,
  name      TEXT NOT NULL,
  teacherId TEXT NOT NULL REFERENCES users(id),
  credits   INTEGER NOT NULL DEFAULT 3,
  semester  TEXT NOT NULL DEFAULT '114-1',
  time      TEXT DEFAULT '',
  room      TEXT DEFAULT '',
  capacity  INTEGER NOT NULL DEFAULT 60,
  description TEXT DEFAULT ''
);

CREATE TABLE IF NOT EXISTS enrollments (
  studentId TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  courseId  TEXT NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
  PRIMARY KEY (studentId, courseId)
);

CREATE TABLE IF NOT EXISTS grades (
  studentId TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  courseId  TEXT NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
  regular   REAL,
  midterm   REAL,
  final     REAL,
  score     INTEGER,
  PRIMARY KEY (studentId, courseId)
);

CREATE TABLE IF NOT EXISTS attendance (
  id        TEXT PRIMARY KEY,
  studentId TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  courseId  TEXT NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
  date      TEXT NOT NULL,
  status    TEXT NOT NULL CHECK (status IN ('出席','遲到','缺席','請假')),
  note      TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_attendance_student ON attendance(studentId);
CREATE INDEX IF NOT EXISTS idx_attendance_course_date ON attendance(courseId, date);

CREATE TABLE IF NOT EXISTS leaves (
  id         TEXT PRIMARY KEY,
  studentId  TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  date       TEXT NOT NULL,
  reason     TEXT NOT NULL,
  status     TEXT NOT NULL DEFAULT '待審核' CHECK (status IN ('待審核','通過','退回')),
  applyDate  TEXT DEFAULT '',
  reviewer   TEXT,
  reviewNote TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_leaves_student ON leaves(studentId);

CREATE TABLE IF NOT EXISTS rewards (
  id        TEXT PRIMARY KEY,
  studentId TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  type      TEXT NOT NULL CHECK (type IN ('嘉獎','小功','大功','警告','小過','大過')),
  date      TEXT NOT NULL,
  reason    TEXT NOT NULL,
  recorder  TEXT DEFAULT ''
);
CREATE INDEX IF NOT EXISTS idx_rewards_student ON rewards(studentId);

CREATE TABLE IF NOT EXISTS announcements (
  id         TEXT PRIMARY KEY,
  title      TEXT NOT NULL,
  content    TEXT NOT NULL,
  authorId   TEXT REFERENCES users(id) ON DELETE SET NULL,
  authorName TEXT DEFAULT '',
  date       TEXT NOT NULL,
  pinned     INTEGER NOT NULL DEFAULT 0
);
