# Release Browser And Admin Checklist

Date:

- 2026-04-09

Environment:

- Local development host
- Base URL: `http://localhost:5085`
- Admin account validated: `admin@quizapi.local`

## Public Page Checks

| Check | Result |
|---|---|
| `/` loads | Pass |
| `/register.html` loads | Pass |
| `/user.html` loads | Pass |
| `/manage.html` loads | Pass |
| `/quiz.html` loads | Pass |
| `/preemployment.html` loads | Pass |
| `/api.html` loads | Pass |
| `/health` returns `200` | Pass |

## User Flow Checks

| Check | Result |
|---|---|
| User registration | Pass |
| User login | Pass |
| Profile load | Pass |
| Quiz list load | Pass |
| Single quiz generation | Pass |
| Quiz submission | Pass |
| Quiz history update after submission | Pass |
| Saved progress restore | Pass |
| Pre-employment generation | Pass |
| Pre-employment submission | Pass |

## Admin Flow Checks

| Check | Result |
|---|---|
| Admin login | Pass |
| Quiz list with admin access | Pass |
| Categories load | Pass |
| Import files load | Pass |
| Import history load | Pass |
| Import history CSV export | Pass |
| User activity CSV export | Pass |
| SMTP settings load | Pass |
| Pre-employment config load | Pass |
| Pre-employment submissions load | Pass |
| Usage report load | Pass |
| Question Editor data load | Pass |
| Quiz Creator source quiz list | Pass |
| Quiz Creator source question paging | Pass |

## Observations

- The current auth rate limiter can temporarily interfere with repeated support or QA login attempts from the same IP address.
- Admin flows are functional once authenticated.
- Public and user-facing routes are serving correctly from the site root.

## Open Follow-Up

- Harden auth rate limiting for support and admin-heavy testing workflows.
- Finish database and schema resilience tooling for environment moves.
