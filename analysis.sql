-- ============================================================================
-- VISA ISSUING RECONCILIATION - FULL MATCHING LOGIC
-- Target engine : MySQL 8.0+ (uses window functions, REGEXP_REPLACE, CTEs)
-- Source tables : issuing_cbs_transactions (CBS ledger - "deducted" side)
--                 issuing_bo_transaction   (CMS/VISA claim - "claimed" side)
-- ============================================================================


-- ============================================================================
-- SECTION 0 - OUTPUT TABLES
-- ============================================================================

DROP TABLE IF EXISTS issuing_reversal_log;
CREATE TABLE issuing_reversal_log (
    id                  BIGINT AUTO_INCREMENT PRIMARY KEY,
    utrnno              VARCHAR(50),
    auth_code           VARCHAR(50),
    account_number      VARCHAR(30),
    txn_amount          DECIMAL(18,2),
    reversal_flag       SMALLINT,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

DROP TABLE IF EXISTS issuing_matched_transactions;
CREATE TABLE issuing_matched_transactions (
    id                  BIGINT AUTO_INCREMENT PRIMARY KEY,
    utrnno              VARCHAR(50),
    rrn                 VARCHAR(50),
    auth_code           VARCHAR(50),
    account_number      VARCHAR(30),
    cbs_amount          DECIMAL(18,2),
    bo_amount           DECIMAL(18,2),
    match_key_used      VARCHAR(30),         -- PRIMARY | FALLBACK_AUTH_ACCT
    match_status        VARCHAR(40),         -- FULLY_MATCHED | MATCHED_MULTI_CLEARING | MATCHED_PARTIAL_SETTLEMENT
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

DROP TABLE IF EXISTS issuing_unmatched_transactions;
CREATE TABLE issuing_unmatched_transactions (
    id                  BIGINT AUTO_INCREMENT PRIMARY KEY,
    utrnno              VARCHAR(50),
    rrn                 VARCHAR(50),
    auth_code           VARCHAR(50),
    account_number      VARCHAR(30),
    cbs_amount          DECIMAL(18,2),
    bo_amount           DECIMAL(18,2),
    match_status        VARCHAR(40),         -- MISSING_IN_CBS | MISSING_IN_BO | AMOUNT_DEVIATION_10PCT |
                                              -- EXCESS_DEDUCTION_REFUND_DUE | NO_AUTHORIZATION_FOUND |
                                              -- MULTI_CLEARING_PARTIAL | MULTI_CLEARING_EXCEEDS_AUTH
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);


-- ============================================================================
-- SECTION 1 - NORMALIZED VIEWS  (single source of truth for key cleanup)
-- ============================================================================

CREATE OR REPLACE VIEW v_issuing_cbs_norm AS
SELECT
    c.*,
    TRIM(CAST(c.account_no AS CHAR))                                       AS norm_account_no,
    TRIM(c.auth_code)                                                       AS norm_auth_code,
    TRIM(c.rrn)                                                             AS norm_rrn,
    TRIM(c.unique_reference_no)                                             AS norm_utrnno
FROM issuing_cbs_transactions c;

CREATE OR REPLACE VIEW v_issuing_bo_norm AS
SELECT
    b.*,
    TRIM(b.account_number)                                                  AS norm_account_no,
    TRIM(b.auth_code)                                                       AS norm_auth_code,
    TRIM(b.rrn)                                                             AS norm_rrn,
    TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0+$', ''))              AS norm_utrnno
FROM issuing_bo_transaction b;


-- ============================================================================
-- SECTION 2 - REVERSAL DETECTION  (must run BEFORE any match/mismatch logic)
-- ============================================================================

CREATE OR REPLACE VIEW v_reversal_pairs AS
SELECT
    norm_utrnno,
    norm_auth_code
FROM v_issuing_bo_norm
GROUP BY norm_utrnno, norm_auth_code
HAVING COUNT(DISTINCT reversal_flag) > 1
   AND COUNT(*) = 2;

INSERT INTO issuing_reversal_log (utrnno, auth_code, account_number, txn_amount, reversal_flag)
SELECT
    b.norm_utrnno,
    b.norm_auth_code,
    b.norm_account_no,
    b.sttl_amount,
    b.reversal_flag
FROM v_issuing_bo_norm b
INNER JOIN v_reversal_pairs r
    ON r.norm_utrnno = b.norm_utrnno
   AND r.norm_auth_code = b.norm_auth_code;

-- Active BO pool = everything except confirmed reversal pairs
CREATE OR REPLACE VIEW v_issuing_bo_active AS
SELECT b.*
FROM v_issuing_bo_norm b
WHERE NOT EXISTS (
    SELECT 1 FROM v_reversal_pairs r
    WHERE r.norm_utrnno = b.norm_utrnno
      AND r.norm_auth_code = b.norm_auth_code
);


-- ============================================================================
-- SECTION 3 - PRIMARY KEY MATCH
-- Key: UTRNNO + RRN + AuthCode + AccountNo   (Amount compared, not keyed on)
-- ============================================================================

CREATE OR REPLACE VIEW v_primary_match AS
SELECT
    c.norm_utrnno       AS utrnno,
    c.norm_rrn          AS rrn,
    c.norm_auth_code    AS auth_code,
    c.norm_account_no   AS account_number,
    c.amount            AS cbs_amount,      -- deducted (CBS)
    b.sttl_amount       AS bo_amount,       -- claimed (VISA/CMS)
    CASE
        WHEN c.amount = b.sttl_amount
            THEN 'FULLY_MATCHED'
        WHEN b.sttl_amount > c.amount
             AND (b.sttl_amount - c.amount) / NULLIF(c.amount, 0) > 0.10
            THEN 'AMOUNT_DEVIATION_10PCT'
        WHEN b.sttl_amount > c.amount
            THEN 'AMOUNT_DEVIATION_WITHIN_10PCT_REVIEW'
        WHEN c.amount > b.sttl_amount
            THEN 'EXCESS_DEDUCTION_REFUND_DUE'
    END AS match_status
FROM v_issuing_cbs_norm c
INNER JOIN v_issuing_bo_active b
    ON c.norm_utrnno    = b.norm_utrnno
   AND c.norm_auth_code = b.norm_auth_code
   AND c.norm_rrn       = b.norm_rrn
WHERE c.norm_utrnno <> '' AND c.norm_rrn <> ''
  AND b.norm_utrnno <> '' AND b.norm_rrn <> '';
  

-- Route: FULLY_MATCHED -> matched table
INSERT INTO issuing_matched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_key_used, match_status)
SELECT utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, 'PRIMARY', match_status
FROM v_primary_match
WHERE match_status = 'FULLY_MATCHED';

-- Route: amount-deviation / excess-deduction -> unmatched table (needs officer action)
INSERT INTO issuing_unmatched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_status)
SELECT utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_status
FROM v_primary_match
WHERE match_status IN ('AMOUNT_DEVIATION_10PCT', 'AMOUNT_DEVIATION_WITHIN_10PCT_REVIEW', 'EXCESS_DEDUCTION_REFUND_DUE');


-- ============================================================================
-- SECTION 4 - FALLBACK MATCH  (UTRNNO/RRN missing -> AuthCode + AccountNo, summed)
-- Covers: No-Authorization, Partial Settlement, Multi-Clearing
-- ============================================================================

-- BO rows that failed primary match AND have missing UTRNNO/RRN
CREATE OR REPLACE VIEW v_bo_unresolved_for_fallback AS
SELECT b.*
FROM v_issuing_bo_active b
WHERE (b.norm_utrnno = '' OR b.norm_rrn = '')
   OR NOT EXISTS (
        SELECT 1 FROM v_primary_match pm
        WHERE pm.utrnno = b.norm_utrnno AND pm.auth_code = b.norm_auth_code AND pm.rrn = b.norm_rrn
   );

CREATE OR REPLACE VIEW v_fallback_bo_grouped AS
SELECT
    norm_auth_code                                 AS auth_code,
    norm_account_no                                AS account_number,
    COUNT(*)                                        AS claim_row_count,
    SUM(sttl_amount)                                AS claimed_total,
    GROUP_CONCAT(DISTINCT norm_utrnno SEPARATOR ', ') AS utrnnos,
    GROUP_CONCAT(DISTINCT norm_rrn SEPARATOR ', ')    AS rrns
FROM v_bo_unresolved_for_fallback
WHERE norm_auth_code <> ''
GROUP BY norm_auth_code, norm_account_no;

CREATE OR REPLACE VIEW v_fallback_cbs_grouped AS
SELECT
    norm_auth_code    AS auth_code,
    norm_account_no   AS account_number,
    SUM(amount)       AS authorized_total
FROM v_issuing_cbs_norm
GROUP BY norm_auth_code, norm_account_no;

CREATE OR REPLACE VIEW v_fallback_match AS
SELECT
    g.auth_code,
    g.account_number,
    g.claim_row_count,
    g.claimed_total,
    g.utrnnos,
    g.rrns,
    cb.authorized_total,
    CASE
        WHEN cb.authorized_total IS NULL          THEN 'NO_AUTHORIZATION_FOUND'
        WHEN g.claimed_total = cb.authorized_total AND g.claim_row_count = 1 THEN 'FULLY_MATCHED'
        WHEN g.claimed_total = cb.authorized_total AND g.claim_row_count > 1 THEN 'MATCHED_MULTI_CLEARING'
        WHEN g.claimed_total < cb.authorized_total AND g.claim_row_count = 1 THEN 'MATCHED_PARTIAL_SETTLEMENT'
        WHEN g.claimed_total < cb.authorized_total AND g.claim_row_count > 1 THEN 'MULTI_CLEARING_PARTIAL'
        WHEN g.claimed_total > cb.authorized_total                          THEN 'MULTI_CLEARING_EXCEEDS_AUTH'
    END AS match_status
FROM v_fallback_bo_grouped g
LEFT JOIN v_fallback_cbs_grouped cb
    ON cb.auth_code = g.auth_code
   AND cb.account_number = g.account_number;

-- Route: clean fallback matches -> matched table
INSERT INTO issuing_matched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_key_used, match_status)
SELECT
    utrnnos, rrns, auth_code, account_number, authorized_total, claimed_total,
    'FALLBACK_AUTH_ACCT', match_status
FROM v_fallback_match
WHERE match_status IN ('FULLY_MATCHED', 'MATCHED_MULTI_CLEARING', 'MATCHED_PARTIAL_SETTLEMENT');

-- Route: exceptions -> unmatched table
INSERT INTO issuing_unmatched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_status)
SELECT
    utrnnos, rrns, auth_code, account_number, authorized_total, claimed_total, match_status
FROM v_fallback_match
WHERE match_status IN ('NO_AUTHORIZATION_FOUND', 'MULTI_CLEARING_PARTIAL', 'MULTI_CLEARING_EXCEEDS_AUTH');


-- ============================================================================
-- SECTION 5 - MISSING_IN_CBS
-- BO (claim) rows with no CBS counterpart at all, after excluding reversals
-- and rows already resolved via primary or fallback matching above.
-- ============================================================================

INSERT INTO issuing_unmatched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_status)
SELECT
    b.norm_utrnno, b.norm_rrn, b.norm_auth_code, b.norm_account_no,
    NULL, b.sttl_amount, 'MISSING_IN_CBS'
FROM v_issuing_bo_active b
WHERE NOT EXISTS (
        SELECT 1 FROM v_issuing_cbs_norm c
        WHERE c.norm_utrnno = b.norm_utrnno
          AND c.norm_auth_code = b.norm_auth_code
          AND c.norm_rrn = b.norm_rrn
      )
  AND NOT EXISTS (
        SELECT 1 FROM v_fallback_match fm
        WHERE fm.auth_code = b.norm_auth_code
          AND fm.account_number = b.norm_account_no
          AND fm.match_status <> 'NO_AUTHORIZATION_FOUND'
      );


-- ============================================================================
-- SECTION 6 - MISSING_IN_BO
-- CBS rows with no BO (claim) counterpart at all.
-- ============================================================================

INSERT INTO issuing_unmatched_transactions
    (utrnno, rrn, auth_code, account_number, cbs_amount, bo_amount, match_status)
SELECT
    c.norm_utrnno, c.norm_rrn, c.norm_auth_code, c.norm_account_no,
    c.amount, NULL, 'MISSING_IN_BO'
FROM v_issuing_cbs_norm c
WHERE NOT EXISTS (
        SELECT 1 FROM v_issuing_bo_active b
        WHERE b.norm_utrnno = c.norm_utrnno
          AND b.norm_auth_code = c.norm_auth_code
          AND b.norm_rrn = c.norm_rrn
      )
  AND NOT EXISTS (
        SELECT 1 FROM v_fallback_match fm
        WHERE fm.auth_code = c.norm_auth_code
          AND fm.account_number = c.norm_account_no
      );


-- ============================================================================
-- SECTION 7 - SUMMARY / VALIDATION QUERIES
-- ============================================================================

-- Overall counts per status (run after all inserts above complete)
SELECT match_status, COUNT(*) AS record_count, SUM(COALESCE(cbs_amount, bo_amount)) AS total_amount
FROM issuing_matched_transactions
GROUP BY match_status
UNION ALL
SELECT match_status, COUNT(*), SUM(COALESCE(cbs_amount, bo_amount))
FROM issuing_unmatched_transactions
GROUP BY match_status
ORDER BY match_status;

-- Reversal log summary
SELECT COUNT(*) AS reversal_pair_rows, COUNT(DISTINCT utrnno) AS distinct_reversed_txns
FROM issuing_reversal_log;

-- Sanity check: every CBS row and every active BO row should appear exactly
-- once across matched + unmatched + reversal_log. Row counts should reconcile:
SELECT
    (SELECT COUNT(*) FROM v_issuing_cbs_norm)                       AS total_cbs_rows,
    (SELECT COUNT(*) FROM issuing_matched_transactions)             AS matched_rows,
    (SELECT COUNT(*) FROM issuing_unmatched_transactions
        WHERE match_status IN ('MISSING_IN_BO','AMOUNT_DEVIATION_10PCT',
                                'AMOUNT_DEVIATION_WITHIN_10PCT_REVIEW','EXCESS_DEDUCTION_REFUND_DUE')) AS cbs_side_unmatched;