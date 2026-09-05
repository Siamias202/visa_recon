-- One-time migration for the existing issuing tables (MySQL 8+).
-- Back up the database before applying this migration.

-- Stable row identifiers are required for one-to-one reversal archiving and
-- correct outer-join unmatched detection.
ALTER TABLE issuing_cbs_transactions
    ADD COLUMN id BIGINT NOT NULL AUTO_INCREMENT FIRST,
    ADD PRIMARY KEY (id),
    MODIFY COLUMN account_no VARCHAR(500),
    ADD INDEX ix_issuing_cbs_primary_match
        (unique_reference_no, rrn, auth_code, amount),
    ADD INDEX ix_issuing_cbs_secondary_match
        (auth_code, amount);

-- Normalize blank numeric values before changing their types.
UPDATE issuing_bo_transaction
SET sttl_amount = NULL
WHERE TRIM(sttl_amount) = '';

UPDATE issuing_bo_transaction
SET st_rev = NULL
WHERE TRIM(st_rev) = '';

UPDATE issuing_bo_transaction
SET reversal_flag = NULL
WHERE TRIM(reversal_flag) = '';

ALTER TABLE issuing_bo_transaction
    ADD COLUMN id BIGINT NOT NULL AUTO_INCREMENT FIRST,
    ADD PRIMARY KEY (id),
    MODIFY COLUMN sttl_amount DECIMAL(18,2),
    MODIFY COLUMN st_rev TINYINT,
    MODIFY COLUMN reversal_flag TINYINT,
    ADD INDEX ix_issuing_bo_primary_match
        (utrnno, rrn, auth_code, sttl_amount),
    ADD INDEX ix_issuing_bo_secondary_match
        (auth_code, sttl_amount),
    ADD INDEX ix_issuing_bo_reversal
        (utrnno, auth_code, reversal_flag);

-- Reversal pairs are archived per reconciliation run. The source BO rows are
-- retained and excluded from that run's normal matching.
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
    KEY ix_issuing_reversal_lookup (run_id, utrnno, auth_code),
    KEY ix_issuing_reversal_original (original_bo_transaction_id),
    KEY ix_issuing_reversal_reversed (reversal_bo_transaction_id),

    CONSTRAINT fk_issuing_reversal_run
        FOREIGN KEY (run_id) REFERENCES issuing_reconciliation_run (id),
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
