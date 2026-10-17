# Catalog of Resolved Bugs & Root-Cause Preventions

## BUG-001: Concurrent Application Registration Race Condition
- **Symptom:** Concurrent candidate applications caused duplicate inserts and unhandled 500 errors.
- **Root Cause:** Parallel requests bypassed pre-check queries before committing.
- **Fix:** Handled `DbUpdateException` concurrency recovery in `CareersController.Apply`.
- **Prevention:** Always anticipate database uniqueness constraint violations under concurrency and handle gracefully.

## BUG-002: Direct Resume Download Exposing Unauthenticated Files
- **Symptom:** Static files in `/uploads/resumes/` were directly accessible by anyone knowing the URL.
- **Root Cause:** Resumes were placed in public `wwwroot`.
- **Fix:** Moved storage to `App_Data/Storage/Resumes`, blocked `/uploads/resumes/` static requests, routed downloads through authenticated `CandidatesController.DownloadResume`.
- **Prevention:** Sensitive PII files must never be placed inside the web root.

## BUG-003: Subagent / Command Execution Polling Loops
- **Symptom:** Long polling loops querying task status causing excessive token burn and latency.
- **Root Cause:** Polling instead of letting the platform reactive wakeup resume execution.
- **Fix:** Enforced reactive wakeup standard per guidelines: launch background task, stop calling tools, wait for system message.
