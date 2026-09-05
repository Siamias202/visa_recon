-- ============================================================================
-- VISA RECONCILIATION DATABASE SCHEMA (MySQL 8+)
-- ============================================================================
-- Creation order is dependency-safe:
--   1. Issuing source tables
--   2. Issuing reconciliation tables
--   3. Acquiring source tables
--   4. Acquiring reconciliation tables
--
-- Note: acquring_fe_transactions keeps the existing application/database
-- spelling ("acquring") for compatibility with the current repositories.
-- ============================================================================


-- ############################################################################
-- ISSUING
-- ############################################################################

-- ============================================================================
-- 1. ISSUING SOURCE TABLES
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_cbs_transactions
(
    id                   BIGINT NOT NULL AUTO_INCREMENT,
    account_no           VARCHAR(500),
    posting_date         DATETIME,
    value_date           DATETIME,
    batch_id             VARCHAR(255),
    posting_branch       VARCHAR(255),
    unique_reference_no  VARCHAR(255),
    debit_credit         VARCHAR(20),
    amount               DECIMAL(18,2),
    transaction_code     VARCHAR(100),
    transaction_name     VARCHAR(255),
    currency             VARCHAR(10),
    time_stamp           VARCHAR(100),
    unique_id            VARCHAR(255),
    narrative_1          VARCHAR(100),
    narrative_2          VARCHAR(100),
    narrative_3          VARCHAR(100),
    narrative_4          VARCHAR(100),
    rrn                  VARCHAR(100),
    auth_code            VARCHAR(100),

    PRIMARY KEY (id),
    KEY ix_issuing_cbs_primary_match
        (unique_reference_no, rrn, auth_code, amount),
    KEY ix_issuing_cbs_secondary_match
        (auth_code, amount),
    KEY ix_issuing_cbs_account_filter
        (account_no)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


CREATE TABLE IF NOT EXISTS issuing_bo_transaction
(
    id                     BIGINT NOT NULL AUTO_INCREMENT,
    session_id             VARCHAR(100),
    bo_oper_id             VARCHAR(100),
    ep_sttl_date           VARCHAR(100),
    run_date               VARCHAR(100),
    trx_type               VARCHAR(100),
    message_type           VARCHAR(100),
    contract_type          VARCHAR(100),
    card_number            VARCHAR(100),
    account_number         VARCHAR(100),
    sender_account_number  VARCHAR(100),
    auth_code              VARCHAR(100),
    arn                    VARCHAR(100),
    trans_date             VARCHAR(100),
    txn_currency           VARCHAR(10),
    sttl_amount            DECIMAL(18,2),
    st_rev                 TINYINT,
    merchant_name          VARCHAR(100),
    merchant_country       VARCHAR(100),
    transaction_date       VARCHAR(100),
    reversal_flag          TINYINT,
    auth_message_type      VARCHAR(100),
    utrnno                 VARCHAR(100),
    rrn                    VARCHAR(100),

    PRIMARY KEY (id),
    KEY ix_issuing_bo_primary_match
        (utrnno, rrn, auth_code, sttl_amount),
    KEY ix_issuing_bo_secondary_match
        (auth_code, sttl_amount),
    KEY ix_issuing_bo_reversal
        (utrnno, auth_code, reversal_flag),
    KEY ix_issuing_bo_account_filter
        (account_number),
    KEY ix_issuing_bo_sender_account_filter
        (sender_account_number),
    KEY ix_issuing_bo_currency_category_filter
        (txn_currency, trx_type)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 2. ISSUING RECONCILIATION TABLES
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_reconciliation_run
(
    id                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    started_at            DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at          DATETIME(6),
    status                VARCHAR(20) NOT NULL DEFAULT 'RUNNING',
    matched_count         INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_cbs_count  INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_bo_count   INT UNSIGNED NOT NULL DEFAULT 0,
    reverse_count         INT UNSIGNED NOT NULL DEFAULT 0,
    error_message         TEXT,

    PRIMARY KEY (id),
    KEY ix_issuing_recon_run_status (status, started_at),
    CONSTRAINT chk_issuing_recon_run_status
        CHECK (status IN ('RUNNING', 'COMPLETED', 'FAILED'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- Stores reversal=0/reversal=1 BO pairs. Source rows remain in
-- issuing_bo_transaction and are excluded from normal matching for the run.
CREATE TABLE IF NOT EXISTS issuing_reversal_transaction
(
    id                         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                     BIGINT UNSIGNED NOT NULL,
    utrnno                     VARCHAR(100) NOT NULL,
    auth_code                  VARCHAR(100) NOT NULL,
    original_bo_transaction_id BIGINT NOT NULL,
    reversal_bo_transaction_id BIGINT NOT NULL,
    original_sttl_amount       DECIMAL(18,2),
    reversal_sttl_amount       DECIMAL(18,2),
    created_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_reversal_pair
        (run_id, original_bo_transaction_id, reversal_bo_transaction_id),
    KEY ix_issuing_reversal_lookup
        (run_id, utrnno, auth_code),
    KEY ix_issuing_reversal_original
        (original_bo_transaction_id),
    KEY ix_issuing_reversal_reversed
        (reversal_bo_transaction_id),

    CONSTRAINT fk_issuing_reversal_run
        FOREIGN KEY (run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT fk_issuing_reversal_original
        FOREIGN KEY (original_bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT fk_issuing_reversal_reversed
        FOREIGN KEY (reversal_bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT chk_issuing_reversal_different_rows
        CHECK (original_bo_transaction_id <> reversal_bo_transaction_id)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- One row per transaction result for one issuing reconciliation run.
CREATE TABLE IF NOT EXISTS issuing_reconciliation_result
(
    id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                 BIGINT UNSIGNED NOT NULL,
    reconciliation_status  VARCHAR(30) NOT NULL,
    business_date          DATE,
    cbs_data               JSON,
    bo_data                JSON,
    created_at             DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    KEY ix_issuing_result_pagination
        (run_id, reconciliation_status, id),
    KEY ix_issuing_result_unresolved_age
        (run_id, reconciliation_status, business_date, id),

    CONSTRAINT fk_issuing_result_run
        FOREIGN KEY (run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT chk_issuing_result_status
        CHECK (reconciliation_status IN
               ('MATCHED', 'MISSING_IN_CBS', 'MISSING_IN_BO'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- Optional cross-run lifecycle table for unresolved issuing items.
CREATE TABLE IF NOT EXISTS issuing_reconciliation_issue
(
    id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    issue_key              CHAR(64) NOT NULL,
    reconciliation_status  VARCHAR(30) NOT NULL,
    issue_status           VARCHAR(20) NOT NULL DEFAULT 'OPEN',
    business_date          DATE NOT NULL,
    amount                 DECIMAL(18,2),
    first_seen_run_id      BIGINT UNSIGNED NOT NULL,
    last_seen_run_id       BIGINT UNSIGNED NOT NULL,
    first_seen_at          DATETIME(6) NOT NULL,
    last_seen_at           DATETIME(6) NOT NULL,
    resolved_at            DATETIME(6),
    cbs_data               JSON,
    bo_data                JSON,

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_issue_key (issue_key),
    KEY ix_issuing_issue_unresolved_age
        (issue_status, reconciliation_status, business_date),
    KEY ix_issuing_issue_first_run (first_seen_run_id),
    KEY ix_issuing_issue_last_run (last_seen_run_id),

    CONSTRAINT fk_issuing_issue_first_run
        FOREIGN KEY (first_seen_run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT fk_issuing_issue_last_run
        FOREIGN KEY (last_seen_run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT chk_issuing_issue_reconciliation_status
        CHECK (reconciliation_status IN
               ('MISSING_IN_CBS', 'MISSING_IN_BO')),
    CONSTRAINT chk_issuing_issue_status
        CHECK (issue_status IN ('OPEN', 'RESOLVED'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ############################################################################
-- ACQUIRING
-- ############################################################################

-- ============================================================================
-- 3. ACQUIRING SOURCE TABLES
-- ============================================================================

CREATE TABLE IF NOT EXISTS acquiring_gl_transactions
(
    id                   BIGINT NOT NULL AUTO_INCREMENT,
    account_no           VARCHAR(255),
    posting_date         DATETIME,
    value_date           DATETIME,
    batch_id             VARCHAR(255),
    posting_branch       VARCHAR(255),
    unique_reference_no  VARCHAR(255),
    debit_credit         VARCHAR(20),
    amount               DECIMAL(18,2),
    transaction_code     VARCHAR(100),
    transaction_name     VARCHAR(255),
    currency             VARCHAR(10),
    time_stamp           DATETIME,
    unique_id            VARCHAR(255),
    narrative_1          VARCHAR(100),
    narrative_2          VARCHAR(100),
    narrative_3          VARCHAR(100),
    narrative_4          VARCHAR(100),
    rrn                  VARCHAR(100),
    auth_code            VARCHAR(100),

    PRIMARY KEY (id),
    KEY ix_acq_gl_rrn (rrn),
    KEY ix_acq_gl_fe_match
        (unique_reference_no, auth_code, rrn, amount)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


CREATE TABLE IF NOT EXISTS acquring_fe_transactions
(
    id               BIGINT NOT NULL AUTO_INCREMENT,
    Atm_Id           VARCHAR(20),
    Reversal         TINYINT(1),
    Request_Amount   DECIMAL(18,2),
    BILLS1           INT,
    BILLS2           INT,
    BILLS3           INT,
    BILLS4           INT,
    Udate            INT,
    `Time`           VARCHAR(20),
    UtrNo            VARCHAR(50),
    IssuerInst       VARCHAR(20),
    Reference_Num    VARCHAR(50),
    Auth_Code        VARCHAR(20),
    acct1            VARCHAR(50),
    Hpan_Card        VARCHAR(50),

    PRIMARY KEY (id),
    KEY ix_acq_fe_issuer (IssuerInst),
    KEY ix_acq_fe_reversal
        (Reference_Num, Auth_Code, Reversal),
    KEY ix_acq_fe_gl_match
        (UtrNo, Auth_Code, Reference_Num, Request_Amount)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


CREATE TABLE IF NOT EXISTS acquiring_ep
(
    id           BIGINT NOT NULL AUTO_INCREMENT,
    PAN          VARCHAR(50),
    RRN          VARCHAR(50),
    ACQ          VARCHAR(20),
    INTEGRATEDP  VARCHAR(20),
    AYMEN        VARCHAR(20),
    TSYSTE       VARCHAR(20),
    M            VARCHAR(20),
    AMOUNTBDT    DECIMAL(18,2),
    CURRENCY     VARCHAR(10),
    AMOUNTUSD    DECIMAL(18,2),

    PRIMARY KEY (id),
    KEY ix_acq_ep_rrn (RRN)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 4. ACQUIRING RECONCILIATION TABLES
-- ============================================================================

CREATE TABLE IF NOT EXISTS acquiring_reconciliation_run
(
    id                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    started_at            DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at          DATETIME(6),
    status                VARCHAR(20) NOT NULL DEFAULT 'RUNNING',
    matched_count         INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_cbs_count  INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_bo_count   INT UNSIGNED NOT NULL DEFAULT 0,
    reversal_count        INT UNSIGNED NOT NULL DEFAULT 0,
    error_message         TEXT,

    PRIMARY KEY (id),
    KEY ix_acq_recon_run_status (status, started_at),
    CONSTRAINT chk_acq_recon_run_status
        CHECK (status IN ('RUNNING', 'COMPLETED', 'FAILED'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


CREATE TABLE IF NOT EXISTS acquiring_fe_reversal
(
    id                         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                     BIGINT UNSIGNED NOT NULL,
    reference_num              VARCHAR(50) NOT NULL,
    auth_code                  VARCHAR(20) NOT NULL,
    original_fe_transaction_id BIGINT NOT NULL,
    reversal_fe_transaction_id BIGINT NOT NULL,
    original_request_amount    DECIMAL(18,2),
    reversal_request_amount    DECIMAL(18,2),
    created_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    UNIQUE KEY ux_acq_fe_reversal_pair
        (run_id, original_fe_transaction_id, reversal_fe_transaction_id),
    KEY ix_acq_fe_reversal_lookup
        (run_id, reference_num, auth_code),
    KEY ix_acq_fe_reversal_original
        (original_fe_transaction_id),
    KEY ix_acq_fe_reversal_reversed
        (reversal_fe_transaction_id),

    CONSTRAINT fk_acq_fe_reversal_run
        FOREIGN KEY (run_id)
        REFERENCES acquiring_reconciliation_run (id),
    CONSTRAINT fk_acq_fe_reversal_original
        FOREIGN KEY (original_fe_transaction_id)
        REFERENCES acquring_fe_transactions (id),
    CONSTRAINT fk_acq_fe_reversal_reversed
        FOREIGN KEY (reversal_fe_transaction_id)
        REFERENCES acquring_fe_transactions (id),
    CONSTRAINT chk_acq_fe_reversal_different_rows
        CHECK (original_fe_transaction_id <> reversal_fe_transaction_id)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


CREATE TABLE IF NOT EXISTS acquiring_reconciliation_result
(
    id                      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                  BIGINT UNSIGNED NOT NULL,
    result_key              CHAR(64) NOT NULL,
    reconciliation_status   VARCHAR(30) NOT NULL,
    business_date           DATE,
    gl_transaction_id       BIGINT,
    ep_transaction_id       BIGINT,
    fe_transaction_id       BIGINT,
    rrn                     VARCHAR(100),
    gl_auth_code            VARCHAR(100),
    gl_unique_reference_no  VARCHAR(255),
    gl_amount               DECIMAL(18,2),
    fe_reference_num        VARCHAR(50),
    fe_auth_code            VARCHAR(20),
    fe_utr_no               VARCHAR(50),
    fe_request_amount       DECIMAL(18,2),
    mismatch_reason         VARCHAR(255),
    created_at              DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    UNIQUE KEY ux_acq_recon_result_key (run_id, result_key),
    KEY ix_acq_recon_result_page
        (run_id, reconciliation_status, id),
    KEY ix_acq_recon_result_date
        (run_id, reconciliation_status, business_date, id),
    KEY ix_acq_recon_result_rrn (rrn),
    KEY ix_acq_recon_result_gl (gl_transaction_id),
    KEY ix_acq_recon_result_ep (ep_transaction_id),
    KEY ix_acq_recon_result_fe (fe_transaction_id),

    CONSTRAINT fk_acq_recon_result_run
        FOREIGN KEY (run_id)
        REFERENCES acquiring_reconciliation_run (id),
    CONSTRAINT fk_acq_recon_result_gl
        FOREIGN KEY (gl_transaction_id)
        REFERENCES acquiring_gl_transactions (id),
    CONSTRAINT fk_acq_recon_result_ep
        FOREIGN KEY (ep_transaction_id)
        REFERENCES acquiring_ep (id),
    CONSTRAINT fk_acq_recon_result_fe
        FOREIGN KEY (fe_transaction_id)
        REFERENCES acquring_fe_transactions (id),
    CONSTRAINT chk_acq_recon_result_status
        CHECK (reconciliation_status IN
               ('MATCHED', 'MISSING_IN_CBS', 'MISSING_IN_BO'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;
