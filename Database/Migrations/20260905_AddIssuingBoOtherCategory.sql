-- Required for V2 databases created before unsupported-but-retained BO
-- transaction types were classified as OTHER.

ALTER TABLE issuing_bo_transaction
    DROP CHECK chk_issuing_bo_category,
    ADD CONSTRAINT chk_issuing_bo_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH', 'OTHER'));

ALTER TABLE issuing_reconciliation_run_result
    DROP CHECK chk_issuing_run_result_category,
    ADD CONSTRAINT chk_issuing_run_result_category
        CHECK (transaction_category IN ('ATM', 'POS', 'PREAUTH', 'OTHER'));
