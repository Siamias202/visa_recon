-- ============================================================================
-- ISSUING RECONCILIATION V2 - FRESH DATABASE CREATION SCRIPT (MySQL 8+)
-- ============================================================================
-- This is a target schema for a fresh database. It does not migrate existing
-- tables and must not be run over the current production schema unchanged.
--
-- Time convention:
--   * Store all DATETIME values in UTC.
--   * The application supplies reconciliation_date in the reporting timezone.
--
-- Matching convention:
--   * Source transactions are retained permanently and remain eligible for a
--     future cross-match while reconciliation_status = 'UNMATCHED'.
--   * A historical transaction matched today belongs to today's output because
--     its match points to a run whose reconciliation_date is today.
--   * The match table is authoritative. Status/match fields on source rows are
--     cached values and must be updated in the same transaction as the match.
-- ============================================================================


-- ============================================================================
-- 1. UPLOAD BATCHES
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_upload_batch
(
    id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    source_type       VARCHAR(10) NOT NULL,
    file_name         VARCHAR(255),
    file_sha256       CHAR(64),
    uploaded_at       DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at      DATETIME(6),
    status            VARCHAR(20) NOT NULL DEFAULT 'PROCESSING',
    total_rows        INT UNSIGNED NOT NULL DEFAULT 0,
    accepted_rows     INT UNSIGNED NOT NULL DEFAULT 0,
    rejected_rows     INT UNSIGNED NOT NULL DEFAULT 0,
    error_message     TEXT,

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_upload_file
        (source_type, file_sha256),
    KEY ix_issuing_upload_date
        (uploaded_at, source_type, id),

    CONSTRAINT chk_issuing_upload_source
        CHECK (source_type IN ('CBS', 'BO')),
    CONSTRAINT chk_issuing_upload_status
        CHECK (status IN ('PROCESSING', 'COMPLETED', 'FAILED'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 2. GL ACCOUNT CLASSIFICATION
-- ============================================================================
-- reconciliation_currency means the GL/settlement currency used for reporting.
-- It is deliberately different from BO txn_currency. A non-BDT transaction
-- settled through a USD GL is reported under USD.

CREATE TABLE IF NOT EXISTS issuing_gl_account_mapping
(
    account_no                  VARCHAR(32) NOT NULL,
    reconciliation_currency    CHAR(3) NOT NULL,
    transaction_category       VARCHAR(20) NOT NULL,
    display_name               VARCHAR(100) NOT NULL,
    is_active                  TINYINT(1) NOT NULL DEFAULT 1,
    created_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                                   ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (account_no),
    KEY ix_issuing_gl_currency_category
        (reconciliation_currency, transaction_category),
    KEY ix_issuing_gl_category_currency
        (transaction_category, reconciliation_currency),

    CONSTRAINT chk_issuing_gl_currency
        CHECK (reconciliation_currency IN ('BDT', 'USD')),
    CONSTRAINT chk_issuing_gl_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


INSERT INTO issuing_gl_account_mapping
(
    account_no,
    reconciliation_currency,
    transaction_category,
    display_name
)
VALUES
    ('9900832418050', 'BDT', 'ATM',     'BDT ATM'),
    ('9900832428050', 'BDT', 'POS',     'BDT POS/Purchase'),
    ('9900832392840', 'USD', 'POS',     'USD POS/Purchase'),
    ('9900832393840', 'USD', 'PREAUTH', 'USD PreAuth'),
    ('9900832394840', 'USD', 'ATM',     'USD ATM')
ON DUPLICATE KEY UPDATE
    reconciliation_currency = VALUES(reconciliation_currency),
    transaction_category = VALUES(transaction_category),
    display_name = VALUES(display_name),
    is_active = 1;


-- ============================================================================
-- 3. RECONCILIATION RUNS
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_reconciliation_run
(
    id                       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,

    -- The business/reporting day shown to the user. The application supplies
    -- this value; do not derive it from source transaction dates.
    reconciliation_date      DATE NOT NULL,

    started_at               DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at             DATETIME(6),
    status                   VARCHAR(20) NOT NULL DEFAULT 'RUNNING',
    run_type                 VARCHAR(20) NOT NULL DEFAULT 'AUTOMATIC',
    rule_version             VARCHAR(30) NOT NULL,

    -- A stable input boundary. Rows uploaded after these IDs wait for the next
    -- run, while older UNMATCHED rows may still match current candidates.
    cbs_cutoff_id             BIGINT,
    bo_cutoff_id              BIGINT,

    primary_match_count      INT UNSIGNED NOT NULL DEFAULT 0,
    secondary_match_count    INT UNSIGNED NOT NULL DEFAULT 0,
    manual_match_count       INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_cbs_count     INT UNSIGNED NOT NULL DEFAULT 0,
    missing_in_bo_count      INT UNSIGNED NOT NULL DEFAULT 0,
    reversal_count           INT UNSIGNED NOT NULL DEFAULT 0,
    error_message            TEXT,

    PRIMARY KEY (id),
    KEY ix_issuing_run_reporting
        (reconciliation_date, status, id),
    KEY ix_issuing_run_status
        (status, started_at, id),

    CONSTRAINT chk_issuing_run_status
        CHECK (status IN ('RUNNING', 'COMPLETED', 'FAILED')),
    CONSTRAINT chk_issuing_run_type
        CHECK (run_type IN ('AUTOMATIC', 'MANUAL'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 4. CBS/GL SOURCE TRANSACTIONS
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_cbs_transactions
(
    id                           BIGINT NOT NULL AUTO_INCREMENT,
    upload_batch_id              BIGINT UNSIGNED,

    account_no                   VARCHAR(32),
    posting_date                 DATETIME,
    value_date                   DATETIME,
    batch_id                     VARCHAR(255),
    posting_branch               VARCHAR(255),
    unique_reference_no          VARCHAR(255),
    debit_credit                 VARCHAR(20),
    amount                       DECIMAL(18,2),
    transaction_code             VARCHAR(100),
    transaction_name             VARCHAR(255),
    currency                     VARCHAR(10),
    time_stamp                   VARCHAR(100),
    unique_id                    VARCHAR(255),
    narrative_1                  VARCHAR(100),
    narrative_2                  VARCHAR(100),
    narrative_3                  VARCHAR(100),
    narrative_4                  VARCHAR(100),
    rrn                          VARCHAR(100),
    -- Some source files contain extended authorization references. The match
    -- index uses the fixed-width SHA-256 key, so preserving the full value here
    -- does not make the candidate index larger.
    auth_code                    VARCHAR(500),

    uploaded_at                  DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    reconciliation_currency     CHAR(3) NOT NULL,
    transaction_category        VARCHAR(20) NOT NULL,
    reconciliation_status        VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    last_attempted_at             DATETIME(6),
    last_reconciliation_run_id   BIGINT UNSIGNED,

    -- Cached match information. issuing_reconciliation_match is authoritative.
    matched_at                   DATETIME(6),
    match_rule                   VARCHAR(20),

    -- Populate once during upload. Use exactly the same canonicalization on
    -- both sources. Suggested input:
    -- PRIMARY   = CURRENCY + CATEGORY + UTRNNO + RRN + AUTH_CODE + amount(0.00)
    -- SECONDARY = CURRENCY + CATEGORY + AUTH_CODE + amount(0.00)
    -- Store UNHEX(SHA2(canonical_input, 256)). Keep NULL when required fields
    -- for that rule are missing.
    primary_match_key            BINARY(32),
    secondary_match_key          BINARY(32),

    PRIMARY KEY (id),

    KEY ix_issuing_cbs_upload
        (upload_batch_id, id),
    KEY ix_issuing_cbs_queue
        (reconciliation_status, id),
    -- Currency-only and Currency + Category searches.
    KEY ix_issuing_cbs_currency_category
        (reconciliation_currency, transaction_category,
         reconciliation_status, id),
    -- Category-only and Category + Currency searches.
    KEY ix_issuing_cbs_category_currency
        (transaction_category, reconciliation_currency,
         reconciliation_status, id),
    KEY ix_issuing_cbs_primary_candidate
        (primary_match_key, reconciliation_status, id),
    KEY ix_issuing_cbs_secondary_candidate
        (secondary_match_key, reconciliation_status, id),
    KEY ix_issuing_cbs_last_run
        (last_reconciliation_run_id, reconciliation_status, id),
    KEY ix_issuing_cbs_account_filter
        (account_no),
    KEY ix_issuing_cbs_business_date
        (posting_date, id),

    CONSTRAINT fk_issuing_cbs_upload
        FOREIGN KEY (upload_batch_id)
        REFERENCES issuing_upload_batch (id),
    CONSTRAINT fk_issuing_cbs_last_run
        FOREIGN KEY (last_reconciliation_run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT chk_issuing_cbs_status
        CHECK (reconciliation_status IN
               ('PENDING', 'UNMATCHED', 'MATCHED', 'EXCLUDED', 'REVERSED')),
    CONSTRAINT chk_issuing_cbs_currency
        CHECK (reconciliation_currency IN ('BDT', 'USD')),
    CONSTRAINT chk_issuing_cbs_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH')),
    CONSTRAINT chk_issuing_cbs_match_rule
        CHECK (match_rule IS NULL OR match_rule IN
               ('PRIMARY', 'SECONDARY', 'MANUAL'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 5. BO SOURCE TRANSACTIONS
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_bo_transaction
(
    id                           BIGINT NOT NULL AUTO_INCREMENT,
    upload_batch_id              BIGINT UNSIGNED,

    session_id                   VARCHAR(100),
    bo_oper_id                   VARCHAR(100),
    ep_sttl_date                 VARCHAR(100),
    run_date                     VARCHAR(100),
    trx_type                     VARCHAR(100),
    message_type                 VARCHAR(100),
    contract_type                VARCHAR(100),
    card_number                  VARCHAR(100),
    account_number               VARCHAR(100),
    sender_account_number        VARCHAR(100),
    auth_code                    VARCHAR(500),
    arn                          VARCHAR(100),
    trans_date                   VARCHAR(100),
    txn_currency                 VARCHAR(10),
    sttl_amount                  DECIMAL(18,2),
    st_rev                       TINYINT,
    merchant_name                VARCHAR(100),
    merchant_country             VARCHAR(100),
    transaction_date             VARCHAR(100),
    reversal_flag                TINYINT,
    auth_message_type            VARCHAR(100),
    utrnno                       VARCHAR(100),
    rrn                          VARCHAR(100),

    uploaded_at                  DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    -- Populate at upload time. Recommended BO classification:
    --   reconciliation_currency = 'BDT' when txn_currency = 'BDT'; otherwise
    --                             'USD' for foreign transactions settled in USD.
    --   transaction_category    = 'ATM' for ATM CASH WITHDRAWAL,
    --                             'POS' for PURCHASE/POS PURCHASE,
    --                             'PREAUTH' for the PreAuth variants.
    reconciliation_currency     CHAR(3) NOT NULL,
    transaction_category        VARCHAR(20) NOT NULL,
    reconciliation_status        VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    last_attempted_at             DATETIME(6),
    last_reconciliation_run_id   BIGINT UNSIGNED,

    -- Cached match information. issuing_reconciliation_match is authoritative.
    matched_at                   DATETIME(6),
    match_rule                   VARCHAR(20),

    primary_match_key            BINARY(32),
    secondary_match_key          BINARY(32),

    PRIMARY KEY (id),

    KEY ix_issuing_bo_upload
        (upload_batch_id, id),
    KEY ix_issuing_bo_queue
        (reconciliation_status, id),
    KEY ix_issuing_bo_currency_category
        (reconciliation_currency, transaction_category,
         reconciliation_status, id),
    KEY ix_issuing_bo_category_currency
        (transaction_category, reconciliation_currency,
         reconciliation_status, id),
    KEY ix_issuing_bo_primary_candidate
        (primary_match_key, reconciliation_status, id),
    KEY ix_issuing_bo_secondary_candidate
        (secondary_match_key, reconciliation_status, id),
    KEY ix_issuing_bo_last_run
        (last_reconciliation_run_id, reconciliation_status, id),
    KEY ix_issuing_bo_reversal
        (reversal_flag, utrnno, auth_code, id),
    KEY ix_issuing_bo_account_filter
        (account_number),
    KEY ix_issuing_bo_sender_account_filter
        (sender_account_number),
    KEY ix_issuing_bo_currency_category_filter
        (txn_currency, trx_type, id),

    CONSTRAINT fk_issuing_bo_upload
        FOREIGN KEY (upload_batch_id)
        REFERENCES issuing_upload_batch (id),
    CONSTRAINT fk_issuing_bo_last_run
        FOREIGN KEY (last_reconciliation_run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT chk_issuing_bo_status
        CHECK (reconciliation_status IN
               ('PENDING', 'UNMATCHED', 'MATCHED', 'EXCLUDED', 'REVERSED')),
    CONSTRAINT chk_issuing_bo_currency
        CHECK (reconciliation_currency IN ('BDT', 'USD')),
    CONSTRAINT chk_issuing_bo_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH', 'OTHER')),
    CONSTRAINT chk_issuing_bo_match_rule
        CHECK (match_rule IS NULL OR match_rule IN
               ('PRIMARY', 'SECONDARY', 'MANUAL'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 6. MANUAL MATCH REQUESTS AND CONFIRMATIONS
-- ============================================================================
-- A manual match is never implemented as a direct source-status update. A user
-- proposes an exact CBS/BO pair and records the reason/evidence. Confirmation
-- creates an issuing_reconciliation_match row with match_rule = 'MANUAL'.

CREATE TABLE IF NOT EXISTS issuing_manual_match_request
(
    id                       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    cbs_transaction_id       BIGINT NOT NULL,
    bo_transaction_id        BIGINT NOT NULL,

    request_status           VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    requested_by             VARCHAR(100) NOT NULL,
    requested_at             DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    reason                   VARCHAR(1000) NOT NULL,
    evidence_reference       VARCHAR(500),

    reviewed_by              VARCHAR(100),
    reviewed_at              DATETIME(6),
    review_note              VARCHAR(1000),
    approved_run_id          BIGINT UNSIGNED,

    PRIMARY KEY (id),
    KEY ix_issuing_manual_request_status
        (request_status, requested_at, id),
    KEY ix_issuing_manual_request_cbs
        (cbs_transaction_id, request_status, id),
    KEY ix_issuing_manual_request_bo
        (bo_transaction_id, request_status, id),
    KEY ix_issuing_manual_request_run
        (approved_run_id, id),

    CONSTRAINT fk_issuing_manual_request_cbs
        FOREIGN KEY (cbs_transaction_id)
        REFERENCES issuing_cbs_transactions (id),
    CONSTRAINT fk_issuing_manual_request_bo
        FOREIGN KEY (bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT fk_issuing_manual_request_run
        FOREIGN KEY (approved_run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT chk_issuing_manual_request_status
        CHECK (request_status IN
               ('PENDING', 'APPROVED', 'REJECTED', 'CANCELLED'))
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 7. PERMANENT ID-BASED MATCHES
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_reconciliation_match
(
    id                         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                     BIGINT UNSIGNED NOT NULL,
    cbs_transaction_id         BIGINT NOT NULL,
    bo_transaction_id          BIGINT NOT NULL,
    reconciliation_currency   CHAR(3) NOT NULL,
    transaction_category      VARCHAR(20) NOT NULL,
    match_rule                 VARCHAR(20) NOT NULL,
    rule_version               VARCHAR(30) NOT NULL,
    matched_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    manual_match_request_id    BIGINT UNSIGNED,

    -- VOIDED retains audit history and permits a controlled re-match.
    match_status               VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    voided_at                  DATETIME(6),
    void_reason                VARCHAR(500),

    -- MySQL permits multiple NULL values in a UNIQUE index. These generated
    -- columns therefore enforce one active match per source transaction while
    -- retaining any number of historical VOIDED matches.
    active_cbs_transaction_id BIGINT GENERATED ALWAYS AS
        (CASE WHEN match_status = 'ACTIVE' THEN cbs_transaction_id END) STORED,
    active_bo_transaction_id  BIGINT GENERATED ALWAYS AS
        (CASE WHEN match_status = 'ACTIVE' THEN bo_transaction_id END) STORED,

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_active_match_cbs
        (active_cbs_transaction_id),
    UNIQUE KEY ux_issuing_active_match_bo
        (active_bo_transaction_id),
    UNIQUE KEY ux_issuing_match_manual_request
        (manual_match_request_id),
    KEY ix_issuing_match_run
        (run_id, match_rule, id),
    KEY ix_issuing_match_run_currency_category
        (run_id, reconciliation_currency, transaction_category, id),
    KEY ix_issuing_match_run_category_currency
        (run_id, transaction_category, reconciliation_currency, id),
    KEY ix_issuing_match_time
        (matched_at, id),
    KEY ix_issuing_match_cbs_history
        (cbs_transaction_id, matched_at, id),
    KEY ix_issuing_match_bo_history
        (bo_transaction_id, matched_at, id),

    CONSTRAINT fk_issuing_match_run
        FOREIGN KEY (run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT fk_issuing_match_cbs
        FOREIGN KEY (cbs_transaction_id)
        REFERENCES issuing_cbs_transactions (id),
    CONSTRAINT fk_issuing_match_bo
        FOREIGN KEY (bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT fk_issuing_match_manual_request
        FOREIGN KEY (manual_match_request_id)
        REFERENCES issuing_manual_match_request (id),
    CONSTRAINT chk_issuing_match_rule
        CHECK (match_rule IN ('PRIMARY', 'SECONDARY', 'MANUAL')),
    CONSTRAINT chk_issuing_match_currency
        CHECK (reconciliation_currency IN ('BDT', 'USD')),
    CONSTRAINT chk_issuing_match_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH')),
    CONSTRAINT chk_issuing_match_manual_source
        CHECK
        (
            (match_rule = 'MANUAL' AND manual_match_request_id IS NOT NULL)
            OR
            (match_rule <> 'MANUAL' AND manual_match_request_id IS NULL)
        ),
    CONSTRAINT chk_issuing_match_status
        CHECK (match_status IN ('ACTIVE', 'VOIDED')),
    CONSTRAINT chk_issuing_match_void_fields
        CHECK (
            (match_status = 'ACTIVE' AND voided_at IS NULL)
            OR
            (match_status = 'VOIDED' AND voided_at IS NOT NULL)
        )
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 8. ID-BASED RESULTS FOR EACH RUN
-- ============================================================================
-- Records what the run actually processed. Historical UNMATCHED records should
-- only be reconsidered and inserted again when a new opposite-side candidate
-- with the same indexed match key exists. Do not copy every old unmatched row
-- into every run.

CREATE TABLE IF NOT EXISTS issuing_reconciliation_run_result
(
    id                         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                     BIGINT UNSIGNED NOT NULL,
    result_status              VARCHAR(30) NOT NULL,
    cbs_transaction_id         BIGINT,
    bo_transaction_id          BIGINT,
    match_id                   BIGINT UNSIGNED,
    reconciliation_currency   CHAR(3) NOT NULL,
    transaction_category      VARCHAR(20) NOT NULL,
    business_date              DATE,
    created_at                 DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_run_result_cbs
        (run_id, cbs_transaction_id),
    UNIQUE KEY ux_issuing_run_result_bo
        (run_id, bo_transaction_id),
    UNIQUE KEY ux_issuing_run_result_match
        (match_id),
    KEY ix_issuing_run_result_page
        (run_id, result_status, id),
    KEY ix_issuing_run_result_date
        (run_id, result_status, business_date, id),
    KEY ix_issuing_result_currency_category
        (run_id, reconciliation_currency, transaction_category,
         result_status, id),
    KEY ix_issuing_result_category_currency
        (run_id, transaction_category, reconciliation_currency,
         result_status, id),

    CONSTRAINT fk_issuing_run_result_run
        FOREIGN KEY (run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT fk_issuing_run_result_cbs
        FOREIGN KEY (cbs_transaction_id)
        REFERENCES issuing_cbs_transactions (id),
    CONSTRAINT fk_issuing_run_result_bo
        FOREIGN KEY (bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT fk_issuing_run_result_match
        FOREIGN KEY (match_id)
        REFERENCES issuing_reconciliation_match (id),
    CONSTRAINT chk_issuing_run_result_status
        CHECK (result_status IN
               ('MATCHED', 'MISSING_IN_CBS', 'MISSING_IN_BO')),
    CONSTRAINT chk_issuing_run_result_currency
        CHECK (reconciliation_currency IN ('BDT', 'USD')),
    CONSTRAINT chk_issuing_run_result_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH', 'OTHER')),
    CONSTRAINT chk_issuing_run_result_shape
        CHECK
        (
            (result_status = 'MATCHED'
             AND cbs_transaction_id IS NOT NULL
             AND bo_transaction_id IS NOT NULL
             AND match_id IS NOT NULL)
            OR
            (result_status = 'MISSING_IN_CBS'
             AND cbs_transaction_id IS NULL
             AND bo_transaction_id IS NOT NULL
             AND match_id IS NULL)
            OR
            (result_status = 'MISSING_IN_BO'
             AND cbs_transaction_id IS NOT NULL
             AND bo_transaction_id IS NULL
             AND match_id IS NULL)
        )
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- 9. BO REVERSAL PAIRS
-- ============================================================================

CREATE TABLE IF NOT EXISTS issuing_reversal_transaction
(
    id                         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                     BIGINT UNSIGNED NOT NULL,
    original_bo_transaction_id BIGINT NOT NULL,
    reversal_bo_transaction_id BIGINT NOT NULL,
    utrnno                     VARCHAR(100) NOT NULL,
    auth_code                  VARCHAR(100) NOT NULL,
    original_sttl_amount       DECIMAL(18,2),
    reversal_sttl_amount       DECIMAL(18,2),
    paired_at                  DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

    PRIMARY KEY (id),
    UNIQUE KEY ux_issuing_reversal_original
        (original_bo_transaction_id),
    UNIQUE KEY ux_issuing_reversal_reversed
        (reversal_bo_transaction_id),
    KEY ix_issuing_reversal_run
        (run_id, id),
    KEY ix_issuing_reversal_lookup
        (utrnno, auth_code, id),

    CONSTRAINT fk_issuing_reversal_run_v2
        FOREIGN KEY (run_id)
        REFERENCES issuing_reconciliation_run (id),
    CONSTRAINT fk_issuing_reversal_original_v2
        FOREIGN KEY (original_bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT fk_issuing_reversal_reversed_v2
        FOREIGN KEY (reversal_bo_transaction_id)
        REFERENCES issuing_bo_transaction (id),
    CONSTRAINT chk_issuing_reversal_different_v2
        CHECK (original_bo_transaction_id <> reversal_bo_transaction_id)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- ============================================================================
-- TODAY'S MATCHES
-- ============================================================================
-- Supply @ReconciliationDate from the application's reporting timezone. This
-- deliberately does not filter posting_date, trans_date, or uploaded_at.
-- Therefore a transaction from any historical date that matched today appears.
--
-- SET @ReconciliationDate = '2026-09-05';
--
-- SELECT
--     m.id AS match_id,
--     m.run_id,
--     r.reconciliation_date,
--     m.matched_at,
--     m.match_rule,
--     m.reconciliation_currency,
--     m.transaction_category,
--     c.id AS cbs_transaction_id,
--     c.posting_date,
--     c.account_no,
--     c.unique_reference_no,
--     c.rrn AS cbs_rrn,
--     c.auth_code AS cbs_auth_code,
--     c.amount AS cbs_amount,
--     b.id AS bo_transaction_id,
--     b.trans_date,
--     b.utrnno,
--     b.rrn AS bo_rrn,
--     b.auth_code AS bo_auth_code,
--     b.sttl_amount AS bo_amount
-- FROM issuing_reconciliation_match AS m
-- INNER JOIN issuing_reconciliation_run AS r
--     ON r.id = m.run_id
-- INNER JOIN issuing_cbs_transactions AS c
--     ON c.id = m.cbs_transaction_id
-- INNER JOIN issuing_bo_transaction AS b
--     ON b.id = m.bo_transaction_id
-- WHERE r.reconciliation_date = @ReconciliationDate
--   AND r.status = 'COMPLETED'
--   AND m.match_status = 'ACTIVE'
--   -- Append only the filters supplied by the user. Do not use
--   -- "@value IS NULL OR column = @value" for these high-volume searches.
--   -- AND m.reconciliation_currency = @Currency
--   -- AND m.transaction_category = @Category
-- ORDER BY m.id;
