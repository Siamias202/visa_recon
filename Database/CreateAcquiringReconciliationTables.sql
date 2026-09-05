-- Acquiring reconciliation storage design (MySQL 8+).
--
-- Assumptions:
--   * acquiring_gl_transactions is the CBS source.
--   * acquring_fe_transactions is the BO source.
--   * acquiring_ep validates the GL population by RRN.
--   * FE reversal=0/reversal=1 pairs are archived here and excluded from
--     reconciliation; source upload rows are never deleted.

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


-- One row represents one FE original/reversal pair. Both source rows are
-- retained for audit, but neither is used by the normal matching stages.
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
    KEY ix_acq_fe_reversal_original (original_fe_transaction_id),
    KEY ix_acq_fe_reversal_reversed (reversal_fe_transaction_id),

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


-- A single status-partitioned result table is more efficient than separate
-- matched/missing tables and keeps querying and pagination consistent.
CREATE TABLE IF NOT EXISTS acquiring_reconciliation_result
(
    id                       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    run_id                   BIGINT UNSIGNED NOT NULL,
    result_key               CHAR(64) NOT NULL,
    reconciliation_status    VARCHAR(30) NOT NULL,
    business_date            DATE,

    gl_transaction_id        BIGINT,
    ep_transaction_id        BIGINT,
    fe_transaction_id        BIGINT,

    -- Requested GL/CBS comparison snapshot.
    rrn                      VARCHAR(100),
    gl_auth_code             VARCHAR(100),
    gl_unique_reference_no   VARCHAR(255),
    gl_amount                DECIMAL(18,2),

    -- Requested FE/BO comparison snapshot.
    fe_reference_num         VARCHAR(50),
    fe_auth_code             VARCHAR(20),
    fe_utr_no                VARCHAR(50),
    fe_request_amount        DECIMAL(18,2),

    mismatch_reason          VARCHAR(255),
    created_at               DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),

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


-- Source-side indexes required by the reversal and matching joins.
-- Run these once. Remove an ADD INDEX clause if an equivalent index already
-- exists in the target database.
-- Request_Amount is money and must be DECIMAL for reliable equality matching.
ALTER TABLE acquring_fe_transactions
    MODIFY COLUMN Request_Amount DECIMAL(18,2);

ALTER TABLE acquiring_gl_transactions
    ADD INDEX ix_acq_gl_rrn (rrn),
    ADD INDEX ix_acq_gl_fe_match
        (unique_reference_no, auth_code, rrn, amount);

ALTER TABLE acquiring_ep
    ADD INDEX ix_acq_ep_rrn (RRN);

ALTER TABLE acquring_fe_transactions
    ADD INDEX ix_acq_fe_reversal
        (Reference_Num, Auth_Code, Reversal),
    ADD INDEX ix_acq_fe_gl_match
        (UtrNo, Auth_Code, Reference_Num, Request_Amount);
