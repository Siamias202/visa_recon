-- Required for databases created with auth_code VARCHAR(100).
-- Safe to run after the V2 tables have already been created.

ALTER TABLE issuing_cbs_transactions
    MODIFY COLUMN auth_code VARCHAR(500) NULL;

ALTER TABLE issuing_bo_transaction
    MODIFY COLUMN auth_code VARCHAR(500) NULL;
