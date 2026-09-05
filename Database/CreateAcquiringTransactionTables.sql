CREATE TABLE IF NOT EXISTS acquiring_gl_transactions (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    account_no VARCHAR(255), posting_date DATETIME, value_date DATETIME,
    batch_id VARCHAR(255), posting_branch VARCHAR(255), unique_reference_no VARCHAR(255),
    debit_credit VARCHAR(20), amount DECIMAL(18,2), transaction_code VARCHAR(100),
    transaction_name VARCHAR(255), currency VARCHAR(10), time_stamp DATETIME,
    unique_id VARCHAR(255), narrative_1 VARCHAR(100), narrative_2 VARCHAR(100),
    narrative_3 VARCHAR(100), narrative_4 VARCHAR(100), rrn VARCHAR(100), auth_code VARCHAR(100)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS acquring_fe_transactions (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    Atm_Id VARCHAR(20), Reversal TINYINT(1), Request_Amount DECIMAL(18,2),
    BILLS1 INT, BILLS2 INT, BILLS3 INT, BILLS4 INT, Udate INT,
    `Time` VARCHAR(20), UtrNo VARCHAR(50), IssuerInst VARCHAR(20),
    Reference_Num VARCHAR(50), Auth_Code VARCHAR(20), acct1 VARCHAR(50), Hpan_Card VARCHAR(50)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS acquiring_ep (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    PAN VARCHAR(50), RRN VARCHAR(50), ACQ VARCHAR(20), INTEGRATEDP VARCHAR(20),
    AYMEN VARCHAR(20), TSYSTE VARCHAR(20), M VARCHAR(20),
    AMOUNTBDT DECIMAL(18,2), CURRENCY VARCHAR(10), AMOUNTUSD DECIMAL(18,2)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;
