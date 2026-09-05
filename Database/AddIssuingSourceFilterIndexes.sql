-- One-time indexes for hierarchical Currency -> Category source searches.
-- The account numbers are resolved by IssuingReconciliationFilter.

ALTER TABLE issuing_cbs_transactions
    ADD INDEX ix_issuing_cbs_account_filter (account_no);

ALTER TABLE issuing_bo_transaction
    ADD INDEX ix_issuing_bo_account_filter (account_number),
    ADD INDEX ix_issuing_bo_sender_account_filter (sender_account_number);
