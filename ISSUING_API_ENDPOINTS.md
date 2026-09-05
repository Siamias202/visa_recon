# Issuing API endpoints

All JSON endpoints use `Content-Type: application/json`. Upload endpoints use
`multipart/form-data` with one or more form fields named `files`.

## CBS / GL

- `POST /api/GL/uploadGLFiles` — upload issuing GL CSV/XLSX files.
- `POST /api/GL/GetGLTransactionDetails` — page through GL rows. Each row now
  includes `id`, `uploadBatchId`, `uploadedAt`, `reconciliationCurrency`,
  `transactionCategory`, `reconciliationStatus`, `matchedAt`, and `matchRule`.

Example list body:

```json
{
  "page": 1,
  "pageSize": 20,
  "searchQuery": null,
  "sortBy": "posting_date",
  "sortDirection": "desc"
}
```

## Back Office

- `POST /api/BO/uploadBOFiles` — upload issuing BO CSV/XLSX files.
- `POST /api/BO/GetBOTransactionsList` — page through BO rows. Each row now
  includes the source transaction ID and reconciliation metadata needed for
  manual matching.

Example list body:

```json
{
  "page": 1,
  "pageSize": 20,
  "searchQuery": null,
  "sortBy": "transaction_date",
  "sortDirection": "desc"
}
```

## Automatic reconciliation and results

- `POST /api/Main/RunMatchAction` — process new rows and cross-match eligible
  historical unmatched rows. The response returns the new `runId` and primary,
  secondary, missing, and reversal counts.
- `POST /api/Main/GetMatchingResults` — retrieve one completed run. Use
  `runId: 0` for the latest completed run.
- `POST /api/Main/GetDailyMatches` — retrieve all active automatic and manual
  matches made on a reporting date. A null date means today. Source transaction
  dates do not limit this result.
- `POST /api/Main/GetMonthlyUnresolvedItems` — retrieve the current unresolved
  source state and age-bucket summary.
- `POST /api/Main/GetReversals` — retrieve reversal pairs for a run.

Run-results body:

```json
{
  "runId": 0,
  "reconciliationStatus": null,
  "page": 1,
  "pageSize": 20
}
```

Daily-matches body:

```json
{
  "reconciliationDate": "2026-09-05",
  "page": 1,
  "pageSize": 20
}
```

Monthly unresolved body:

```json
{
  "runId": 0,
  "asOfDate": "2026-09-05",
  "reconciliationStatus": null,
  "ageBucket": null,
  "page": 1,
  "pageSize": 20
}
```

Reversal body:

```json
{
  "runId": 1,
  "page": 1,
  "pageSize": 20
}
```

The existing `/api/Reporting/GetMatchingResults` and
`/api/Reporting/GetMonthlyUnresolvedItems` routes remain compatible aliases and
now use the same ID-based repository.

## Manual matching

Manual matching is a two-step audited action. The same user may request and
approve when that is permitted by the application workflow.

- `POST /api/issuing/manual-matches` — propose one exact CBS ID and BO ID.
- `POST /api/issuing/manual-matches/{requestId}/approve` — confirm the request,
  create a `MANUAL` reconciliation run and match, and update both source rows.
- `POST /api/issuing/manual-matches/{requestId}/reject` — reject a pending
  request without changing either transaction.
- `POST /api/issuing/manual-matches/search` — page through manual requests.

Create body:

```json
{
  "cbsTransactionId": 101,
  "boTransactionId": 202,
  "requestedBy": "user.name",
  "reason": "Confirmed against source documents",
  "evidenceReference": "CASE-12345"
}
```

Approve or reject body:

```json
{
  "reviewedBy": "user.name",
  "reviewNote": "Confirmed"
}
```

Search body:

```json
{
  "requestStatus": "PENDING",
  "page": 1,
  "pageSize": 20
}
```

The application currently classifies the five known GL accounts in code, so
`issuing_gl_account_mapping` is not required by these endpoints.
