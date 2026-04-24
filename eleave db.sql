-- =========================
-- CREATE DATABASE
-- =========================
CREATE DATABASE IF NOT EXISTS eleave_db;
USE eleave_db;

-- =========================
-- USERS
-- =========================
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password VARCHAR(100) NOT NULL,
    role ENUM('employee','supervisor') NOT NULL,
    supervisor_id INT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (supervisor_id) REFERENCES users(id)
        ON DELETE SET NULL
);

-- =========================
-- LEAVE BALANCES
-- =========================
CREATE TABLE leave_balances (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    year INT NOT NULL,
    total_leave INT DEFAULT 12,
    used_leave INT DEFAULT 0,
    FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE
);

-- =========================
-- LEAVE REQUESTS
-- =========================
CREATE TABLE leave_requests (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    reason TEXT,
    status ENUM('pending','approved','rejected') DEFAULT 'pending',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE
);

-- =========================
-- APPROVAL HISTORY
-- =========================
CREATE TABLE approval_history (
    id INT AUTO_INCREMENT PRIMARY KEY,
    leave_request_id INT NOT NULL,
    approved_by INT NOT NULL,
    status ENUM('approved','rejected') NOT NULL,
    remarks TEXT,
    action_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (leave_request_id) REFERENCES leave_requests(id)
        ON DELETE CASCADE,
    FOREIGN KEY (approved_by) REFERENCES users(id)
);

-- =========================
-- (OPTIONAL) LEAVE TRANSACTIONS
-- =========================
CREATE TABLE leave_transactions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    leave_request_id INT NOT NULL,
    transaction_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (leave_request_id) REFERENCES leave_requests(id)
        ON DELETE CASCADE
);

-- =========================
-- DUMMY DATA (BIAR BISA TEST)
-- =========================

-- Supervisor
INSERT INTO users (name, email, password, role)
VALUES ('Supervisor 1', 'spv@mail.com', '123', 'supervisor');

-- Employee (punya supervisor)
INSERT INTO users (name, email, password, role, supervisor_id)
VALUES ('Employee 1', 'emp@mail.com', '123', 'employee', 1);

-- Leave balance
INSERT INTO leave_balances (user_id, year, total_leave, used_leave)
VALUES (2, 2026, 12, 0);