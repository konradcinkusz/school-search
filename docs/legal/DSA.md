# DSA machinery for reviews: what is implemented, what is not, what the owner must supply

Status: **draft for legal review.** Nothing in this file is legal advice. The legal texts it refers to
(`TERMS-DRAFT.pl.md`, `PRIVACY-DRAFT.pl.md`) are drafts marked "projekt do weryfikacji przez prawnika".
Article numbers refer to Regulation (EU) 2022/2065 (Digital Services Act). Whether school-search is an
intermediary service or an online platform in the DSA sense is an **OPEN** question for the lawyer. The
implementation below is built as if the review section were a hosted-content service, because that is the
conservative reading.

Implementation: `src/SchoolSearch.Reviews/Dsa/**` (E1b), endpoints under `/api/v1/legal`, `/api/v1/notices`,
`/api/v1/decisions` and `/api/v1/moderation`. Web pages: `/zglos-tresc`, `/regulamin`, `/prywatnosc`, `/kontakt`,
`/moderacja`.

## 1. Article by article

| Article | Obligation (short) | What the code does | State |
|---|---|---|---|
| 11 | Single point of contact for authorities | `GET /api/v1/legal/contact` returns `Dsa:ContactEmail` and/or `Dsa:ContactUrl`. Unset: **503 `contact_not_configured`**. No address is invented | Implemented; **owner must supply the address** |
| 12 | Single point of contact for recipients | The same endpoint and the `/kontakt` page | Implemented; **owner must supply the address** |
| 14 | Terms: restrictions, moderation, complaints in plain terms | Terms are a versioned document (`TermsVersion`, stored on each review). Draft texts in `TERMS-DRAFT.pl.md` | Drafted; **lawyer to approve the text and the version** |
| 15, 24 | Transparency reports | `GET /api/v1/legal/transparency` returns **computed counts only** (notices received and decided, actions by type, complaints). No personal data | Implemented as data. **OPEN: whether a periodic report is required, and its format** |
| 16 | Notice and action: a way to notify illegal content, with an explanation and a good-faith confirmation | `POST /api/v1/notices` (anonymous, rate-limited, 16 KB body limit). Requires `GoodFaithConfirmed = true` and an explanation of 20 to 3000 characters. Returns `{id, ackToken, receivedAt}`. The token is the only way to look the notice up | Implemented |
| 16 (confirmation of receipt) | Confirmation of receipt to the notifier where contact details are given | The receipt is the `ackToken` response. An e-mail confirmation is **not** sent (no mail provider wired) | Partly implemented. **OPEN: e-mail receipt** |
| 17 | Statement of reasons for every restriction or removal | `ModerationDecision` stores: action, legal or terms ground, facts, `Automated` (always false), redress information (art. 20), who decided (hashed moderator id), and when. The notifier reads it with `GET /api/v1/notices/{id}?token=`. The affected author reads it in `GET /api/v1/reviews/mine` | Implemented. Only the moderation endpoints create decisions, and no AI path can (E6) |
| 20 | Internal complaint handling against decisions | `POST /api/v1/decisions/{id}/complaints` (author or notice token). A moderator decides with `upheld` or `reversed` and the reasons. Decision and complaint are both stored | Implemented. **OPEN: timeline for complaint decisions** (the lawyer sets it, not the code) |
| 21 | Out-of-court dispute settlement | Not implemented | **Not implemented.** OPEN: whether it applies, and which body |
| 22 | Trusted flaggers | Not implemented | Not implemented. OPEN: whether it applies |
| 23 | Measures against misuse | Rate limits (notices 10 per hour per IP; reviews 10 writes per hour per subject). No automatic suspension | Partly implemented. Repeat-notifier handling is OPEN |
| 25 to 43 | Obligations for online platforms and very large platforms | Not implemented in Phase E | OPEN: applicability to school-search |

## 2. What the system never does

- **No automated decision.** `Automated` is always `false`. The AI triage (E6) only orders the moderator queue.
  It cannot publish, reject, remove or write a statement of reasons.
- **No aggregation of reviews into a score.** Reviews never feed the scoring engine (architecture test). The
  API and the UI show reviews in a separate, `userGenerated: true` envelope.
- **No personal data in the transparency report.** It returns counts only.
- **No publication of the reviewer's identity.** Only the label "Rodzic/opiekun (niezweryfikowany)" and the month.

## 3. What the owner must supply before the section goes live

1. The single point of contact (art. 11 and 12): `Dsa__ContactEmail` and, if used, `Dsa__ContactUrl`. Set them as
   Fly `[env]` values in `flyio/reviews.fly.toml` (they are public, not secrets). Until then the contact endpoint
   answers 503 on purpose.
2. The identity of the service provider (name, address, registration) for `TERMS-DRAFT.pl.md` and
   `PRIVACY-DRAFT.pl.md`. These are not in the repository and must not be guessed.
3. A decision on the e-mail receipt for notices (art. 16, confirmation of receipt) and on how a notifier who gave an e-mail address receives the
   decision (17 and 20).
4. The moderation staffing and the complaint timeline (art. 20). Both are operational facts, not code.
5. Legal approval of the terms, the privacy notice and this mapping, with the version recorded in
   `TERMS-DRAFT.pl.md`.

## 4. Known gaps and risks

- A notice for content on another site (`ContentReference` is a URL) is stored and decided by hand. There is no
  automatic check of the URL.
- The good-faith confirmation is a checkbox. It is recorded, not verified.
- Moderation is pre-moderation in v1 (new reviews start `pending`). Every review waits for a moderator. This favours
  the reviewed school's reputation only in the sense of safety; it also means slow publication. The queue
  is the operational limit.
- Decisions are only as good as the facts entered by the moderator. The statement of reasons records those facts.
