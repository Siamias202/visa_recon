-- Run once after creating issuing_reconciliation_result.
-- The relational date keeps monthly aging and pagination index-friendly.

ALTER TABLE issuing_reconciliation_result
    ADD COLUMN business_date DATE NULL
        AFTER reconciliation_status;

CREATE INDEX ix_reconciliation_unresolved_age
    ON issuing_reconciliation_result
    (
        run_id,
        reconciliation_status,
        business_date,
        id
    );
