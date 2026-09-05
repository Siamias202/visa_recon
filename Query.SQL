---- Find Reverse Transaction----

SELECT
    UTRNNO,
    AUTH_CODE,
    REVERSAL_FLAG
FROM issuing_bo_transaction
WHERE (UTRNNO, AUTH_CODE) IN (
    SELECT
        UTRNNO,
        AUTH_CODE
    FROM issuing_bo_transaction
    GROUP BY UTRNNO, AUTH_CODE
    HAVING COUNT(DISTINCT REVERSAL_FLAG) > 1
)
ORDER BY
    UTRNNO,
    AUTH_CODE,
    REVERSAL_FLAG;

---- RRN + AUTH_CODE +UTRNNO + AMOUNT----

SELECT
    c.*,
    b.*
FROM issuing_cbs_transactions AS c
INNER JOIN issuing_bo_transaction AS b
    ON TRIM(c.unique_reference_no)
       = TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0$', ''))
    
    AND TRIM(c.auth_code)
        = TRIM(REGEXP_REPLACE(CAST(b.auth_code AS CHAR), '\\.0$', ''))
    
    AND TRIM(c.rrn)
        = TRIM(REGEXP_REPLACE(CAST(b.rrn AS CHAR), '\\.0$', ''))
    
    AND c.amount = b.sttl_amount;

    
    
SELECT COUNT(*) AS matched_records
FROM issuing_cbs_transactions AS c
INNER JOIN issuing_bo_transaction AS b
    ON TRIM(c.unique_reference_no)
       = TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0$', ''))
    AND TRIM(c.auth_code)
       = TRIM(REGEXP_REPLACE(CAST(b.auth_code AS CHAR), '\\.0$', ''))
    AND TRIM(c.rrn)
       = TRIM(REGEXP_REPLACE(CAST(b.rrn AS CHAR), '\\.0$', ''))
    AND c.amount = b.sttl_amount;


---MISSING_IN_CBS-----

SELECT
    b.*,
    'MISSING_IN_CBS' AS reconciliation_status
FROM issuing_bo_transaction AS b
LEFT JOIN issuing_cbs_transactions AS c
    ON TRIM(c.unique_reference_no)
       = TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0$', ''))
    AND TRIM(c.auth_code)
       = TRIM(REGEXP_REPLACE(CAST(b.auth_code AS CHAR), '\\.0$', ''))
    AND TRIM(c.rrn)
       = TRIM(REGEXP_REPLACE(CAST(b.rrn AS CHAR), '\\.0$', ''))
    AND c.amount = b.sttl_amount
WHERE c.unique_reference_no IS NULL;


---MISSING_IN_BO-----

SELECT
    c.*,
    'MISSING_IN_BO' AS reconciliation_status
FROM issuing_cbs_transactions AS c
LEFT JOIN issuing_bo_transaction AS b
    ON TRIM(c.unique_reference_no)
       = TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0$', ''))
    AND TRIM(c.auth_code)
       = TRIM(REGEXP_REPLACE(CAST(b.auth_code AS CHAR), '\\.0$', ''))
    AND TRIM(c.rrn)
       = TRIM(REGEXP_REPLACE(CAST(b.rrn AS CHAR), '\\.0$', ''))
    AND c.amount = b.sttl_amount
WHERE b.utrnno IS NULL;

---All in One------

SELECT
    c.unique_reference_no,
    c.auth_code,
    c.rrn,
    c.amount AS cbs_amount,
    b.utrnno,
    b.auth_code AS bo_auth_code,
    b.rrn AS bo_rrn,
    b.sttl_amount AS bo_amount,
    CASE
        WHEN c.unique_reference_no IS NOT NULL
             AND b.utrnno IS NOT NULL
            THEN 'MATCHED'
        WHEN c.unique_reference_no IS NULL
            THEN 'MISSING_IN_CBS'
        WHEN b.utrnno IS NULL
            THEN 'MISSING_IN_BO'
    END AS reconciliation_status
FROM issuing_cbs_transactions AS c
LEFT JOIN issuing_bo_transaction AS b
    ON TRIM(c.unique_reference_no)
       = TRIM(REGEXP_REPLACE(CAST(b.utrnno AS CHAR), '\\.0$', ''))
    AND TRIM(c.auth_code)
       = TRIM(REGEXP_REPLACE(CAST(b.auth_code AS CHAR), '\\.0$', ''))
    AND TRIM(c.rrn)
       = TRIM(REGEXP_REPLACE(CAST(b.rrn AS CHAR), '\\.0$', ''))
    AND c.amount = b.sttl_amount;



--- Check Multi Clearing---
WITH reversal_pairs AS (
    SELECT utrnno, auth_code
    FROM issuing_bo_transaction
    GROUP BY utrnno, auth_code
    HAVING COUNT(DISTINCT reversal_flag) > 1
       AND COUNT(*) = 2
),

bo_grouped AS (
    SELECT
        b.auth_code,
        b.account_number,
        COUNT(*)              AS claim_row_count,
        SUM(b.sttl_amount)    AS claimed_total,
        GROUP_CONCAT(DISTINCT b.utrnno SEPARATOR ', ') AS utrnnos,
        GROUP_CONCAT(DISTINCT b.rrn    SEPARATOR ', ') AS rrns
    FROM issuing_bo_transaction b
    WHERE NOT EXISTS (
        SELECT 1 FROM reversal_pairs r
        WHERE r.utrnno = b.utrnno
          AND r.auth_code = b.auth_code
    )
    GROUP BY b.auth_code, b.account_number
    HAVING COUNT(*) > 1                    -- more than 1 claim row = candidate
),

cbs_grouped AS (
    SELECT
        c.auth_code,
        CAST(c.account_no AS CHAR) AS account_number,
        COUNT(*)          AS cbs_row_count,
        SUM(c.amount)     AS authorized_total
    FROM issuing_cbs_transactions c
    GROUP BY c.auth_code, CAST(c.account_no AS CHAR)
)

SELECT
    g.auth_code,
    g.account_number,
    g.claim_row_count,
    g.claimed_total,
    g.utrnnos,
    g.rrns,
    cb.authorized_total,
    CASE
        WHEN cb.authorized_total IS NULL              THEN 'NO_CBS_AUTHORIZATION_FOUND'
        WHEN g.claimed_total = cb.authorized_total     THEN 'MATCHED_MULTI_CLEARING'
        WHEN g.claimed_total < cb.authorized_total     THEN 'MULTI_CLEARING_PARTIAL'
        WHEN g.claimed_total > cb.authorized_total     THEN 'MULTI_CLEARING_EXCEEDS_AUTH'
    END AS multi_clearing_status
FROM bo_grouped g
LEFT JOIN cbs_grouped cb
    ON TRIM(cb.auth_code)      = TRIM(g.auth_code)
   AND TRIM(cb.account_number) = TRIM(g.account_number)
ORDER BY g.auth_code, g.account_number;
