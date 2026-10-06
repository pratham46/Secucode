-- SecuCode database: schema + dev seed data.
-- Run:  mysql -u root -p < Database/setup.sql
-- WARNING: drops and recreates the `secucode` database.

DROP DATABASE IF EXISTS secucode;
CREATE DATABASE secucode CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE secucode;

CREATE TABLE branches (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  branch_name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE classes (
  id         INT AUTO_INCREMENT PRIMARY KEY,
  class_name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE batches (
  id         INT AUTO_INCREMENT PRIMARY KEY,
  batch_name VARCHAR(100) NOT NULL UNIQUE
);

-- password and pin are bcrypt hashes (use Database/add-user.ps1 to create users)
CREATE TABLE users (
  id        INT AUTO_INCREMENT PRIMARY KEY,
  username  VARCHAR(100) NOT NULL UNIQUE,
  password  VARCHAR(255) NOT NULL,
  role      VARCHAR(20)  NOT NULL CHECK (role IN ('Student', 'Teacher')),
  pin       VARCHAR(255) NULL,
  branch_id INT NULL,
  class_id  INT NULL,
  batch_id  INT NULL,
  FOREIGN KEY (branch_id) REFERENCES branches(id),
  FOREIGN KEY (class_id)  REFERENCES classes(id),
  FOREIGN KEY (batch_id)  REFERENCES batches(id)
);

CREATE TABLE examinations (
  id         INT AUTO_INCREMENT PRIMARY KEY,
  name       VARCHAR(255) NOT NULL,
  date       DATE NOT NULL,
  time_limit INT NOT NULL CHECK (time_limit > 0),
  branch_id  INT NOT NULL,
  class_id   INT NOT NULL,
  batch_id   INT NOT NULL,
  created_by INT NULL,
  is_active  TINYINT(1) NOT NULL DEFAULT 1,
  FOREIGN KEY (branch_id)  REFERENCES branches(id),
  FOREIGN KEY (class_id)   REFERENCES classes(id),
  FOREIGN KEY (batch_id)   REFERENCES batches(id),
  FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE questions (
  id             INT AUTO_INCREMENT PRIMARY KEY,
  examination_id INT NOT NULL,
  question_text  TEXT NOT NULL,
  FOREIGN KEY (examination_id) REFERENCES examinations(id) ON DELETE CASCADE
);

-- One row per finished attempt; also blocks retakes.
-- Webcam/screenshot/audio evidence stays on disk under SubmittedCodes/.
CREATE TABLE submissions (
  id             INT AUTO_INCREMENT PRIMARY KEY,
  examination_id INT NOT NULL,
  user_id        INT NOT NULL,
  question_text  TEXT NULL,
  language       VARCHAR(20) NOT NULL,
  code           MEDIUMTEXT NOT NULL,
  reason         VARCHAR(255) NOT NULL,
  submitted_at   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE (examination_id, user_id),
  FOREIGN KEY (examination_id) REFERENCES examinations(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id)        REFERENCES users(id) ON DELETE CASCADE
);

-- ---------- Dev seed data ----------
INSERT INTO branches (branch_name) VALUES ('Computer Engineering');
INSERT INTO classes  (class_name)  VALUES ('TE');
INSERT INTO batches  (batch_name)  VALUES ('A1');

-- teacher1 / teacher123            (teachers have no PIN)
-- student1 / student123, PIN 1234, batch A1
INSERT INTO users (username, password, role, pin, branch_id, class_id, batch_id) VALUES
  ('teacher1', '$2a$11$F5IUNIKYLyPAXxscIZU1nukLgDfTBsF7xICfZkgwrJaK3S50HiiYC', 'Teacher', NULL, NULL, NULL, NULL),
  ('student1', '$2a$11$ZATEqODcMDytqEugPVsOrOET8GPJVzqJusBNy51KgVWxLVzdbmew.', 'Student',
   '$2a$11$WvKgWcUTOE0Y7F8YKsjpPeOF8dMYZlOfrpZrmQExOmYJkpg0T2Bqm', 1, 1, 1);

-- Dated today so it can be taken right away.
INSERT INTO examinations (name, date, time_limit, branch_id, class_id, batch_id, created_by, is_active)
VALUES ('Sample Coding Test', CURDATE(), 30, 1, 1, 1, 1, 1);

INSERT INTO questions (examination_id, question_text) VALUES
  (1, 'Write a program that prints the numbers 1 to 10.'),
  (1, 'Read an integer n from input and print the sum of 1..n.');
