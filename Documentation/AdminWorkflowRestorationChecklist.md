# Admin Workflow Restoration Checklist

This checklist maps the documented administrative workflows to the current corporate application so restoration work can be driven by actual gaps instead of assumptions.

## Current Management Surface

Current management page:

- `/manage.html`

Current visible admin sections:

- Admin Login
- Quiz Package Import
- Import Files
- Accounts And Roles
- SMTP Settings
- Pre-Employment Quiz Setup
- Quizzes
- Question Editor
- Quiz Creator
- Import History
- User Metrics
- Pre-Employment Attempts

## Documented Admin Workflow Coverage

| Workflow | Current Coverage | Notes |
|---|---|---|
| Admin login | Present | Implemented in `AuthController` and surfaced in `manage.html` |
| Upload quiz files | Present | Supports `.csv`, `.txt`, and `.zip` imports |
| Uploaded files review | Present | Import files section is available |
| Quiz list management | Present | Includes refresh, archive, restore, delete, and quiz launch actions |
| Import history | Present | History section exists and export support is available |
| User management | Present | Search, create user, role and profile updates are present |
| SMTP settings | Present | Save, reload, and send-test behavior is present |
| Usage reporting | Present | Metrics and export behavior are present |
| Pre-employment setup | Present | Configurable quiz selection, question counts, timer, and passing score |
| Pre-employment attempts | Present | Candidate submissions are viewable |
| Question maintenance | Present | Question Editor supports paged question updates |
| Custom quiz creation | Present | Quiz Creator supports assembling quizzes from `Basic` quiz questions |

## Likely Remaining Restoration Work

The broad admin categories from the usage guide are already represented in the current application. Remaining restoration work is therefore most likely to be one of these:

- behavior mismatches within an existing section
- missing buttons, exports, or validation logic inside an existing workflow
- data migration or legacy content that the old environment had but this environment does not
- support or operational utilities that were never captured in the public usage guide

## Recommended Next Restoration Pattern

Use this checklist when validating older-environment parity:

1. Open the old environment or reference screenshots.
2. Compare section-by-section against `/manage.html`.
3. Record the first concrete mismatch.
4. Restore that workflow end-to-end before moving to the next one.

## First Validation Targets

These are the best next admin workflows to validate live because they tend to hide environment-specific drift:

- import package processing with images
- SMTP save and test email
- pre-employment configuration save and runner launch
- import history export
- user metrics export
- archive and restore quiz actions
