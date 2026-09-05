-- One-time migration for BO-based Currency -> Category filtering.
-- Existing rows cannot be backfilled reliably; re-upload the BO source files
-- after applying this migration.

ALTER TABLE issuing_bo_transaction
    ADD COLUMN txn_currency VARCHAR(10) NULL AFTER trans_date,
    ADD INDEX ix_issuing_bo_currency_category_filter
        (txn_currency, trx_type);
