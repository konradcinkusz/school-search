# Proposal: a standard for exposing a metric service through MCP

- **Status:** DRAFT. Written in `school-search` for the owner to file. **Not filed.** Nothing in `konradcinkusz/architecture-standards`
  has been changed by this document. The owner decides whether, where and how it is filed.
- **Date:** 2026-10-11
- **Proposed target:** `konradcinkusz/architecture-standards`, as a new guide (for example `docs/guides/MCP-SURFACE.md`) and a checklist
  item in the reference architecture's guide list. The file path is a suggestion for the owner.
- **Origin:** derived from `school-search` ADR-0006 (MCP is its own deployable) and the MCP section of its brief. The text is written
  to be pasted into the standards repository as it stands, once the owner has edited it.

## 1. Problem

A service that publishes metrics about organisations (here: school exam results) is increasingly asked to be queried by
AI agents through the Model Context Protocol (MCP). An agent is a new kind of client. It reads numbers fluently, it
repeats them without their context, and it does not see the page where a caveat was printed. Three things go wrong by default:

1. **The number travels without its confidence.** A score of +1,8 is repeated; its interval, cohort size and methodology
   version are dropped. The agent then compares two numbers that cannot be compared (METRIC-ETHICS §3).
2. **The suppression rule does not survive the protocol.** The web shows "too few pupils, no score". A thin MCP wrapper that maps a
   null score to `0` or to a blank field makes the most harmful failure in the product appear as a bad school.
3. **A second surface quietly duplicates business logic.** An MCP server that re-implements scoring, filtering or ranking
   creates a second place where the rules can be wrong. Both places must agree, and nothing forces them to.

The standards have no rule for any of this. Today a team building an MCP server has to rediscover it.

## 2. Decisions derived from the school-search case

These are the decisions `school-search` took. They are offered as the candidate content of the standard. Each one has a reason.

1. **Own service.** The MCP surface is a separate deployable with its own credential, rate limit and release cadence (ADR-0006).
   Reason: a protocol adapter must not sit inside the API, where its trust level and its inputs differ.
2. **The MCP service owns no database.** It holds no data of its own. Reason: a second copy of the data is a second place for
   suppression and provenance to go wrong.
3. **Reads go through the published contract only.** The MCP service calls the same public JSON contract as the web, over the
   public URL. It does not open a database connection, and it does not link the API's assemblies. Reason: the agent sees exactly
   what the web sees.
4. **Responses carry confidence.** Every score, in every tool response, arrives with its interval, cohort size, methodology version,
   exam year and as-of date in the same object. The server passes the API JSON through without removing these fields.
5. **Suppression survives the protocol.** A suppressed value is a state (`insufficient_cohort`), never a null that a client may
   render as zero. The tool description tells the model so. The suppression is enforced once, in the API, and inherited.
6. **User-generated content is labelled and separated.** Free text written by users (reviews, corrections) is never in the
   same field as the metric, and it is labelled as user-generated. Reason: an agent must not treat a review as a fact about the school.
7. **Authentication and quota.** The MCP endpoint is anonymous in v1 and is rate limited per client IP (60 per minute in
   school-search). The service holds its own key for its outbound calls, and the key never appears in a response or a log.
   Reason: an agent has no account; the quota is the only control available.
8. **Read-only tool surface.** No tool creates, updates or deletes. The tool list is checked by a test: no name matches
   `delete`, `update`, `write` or `create`.
9. **No ordering control that an agent can flip.** No tool takes a sort direction. A composite score is never listed ascending.
10. **Protocol-level test before exposure.** The tool list, the field names of every score, the suppression path, and an upstream
    failure all have a CI test that talks MCP over HTTP. The service is not exposed until the test is green.
11. **The tool description is part of the contract.** Each tool description states how the number must be quoted (score with interval,
    cohort size, year and version), and what a null score means. A test checks that the description contains these terms.

## 3. Checklist items (for the standard)

Each item is written so that a reviewer can check it. Add them to the reference architecture checklist once filed.

- [ ] The MCP surface is a separate deployable, or the reason it is not is recorded in an ADR.
- [ ] The MCP service owns no database and reads only through the published public contract.
- [ ] Every score in every tool response includes its interval, cohort size, methodology version, exam year and as-of date in the same object.
- [ ] A suppressed value is returned as a named state, and no tool or resource returns it as zero or blank. A test asserts this with a fixture.
- [ ] User-generated text is in its own field, labelled as user-generated, and never mixed into a metric object.
- [ ] No tool name matches `delete`, `update`, `write` or `create`. A test lists the tools and asserts this.
- [ ] No tool accepts a sort direction for a composite metric.
- [ ] Tool descriptions state how the number must be quoted and what a null means. A test checks the wording.
- [ ] The endpoint is rate limited per client IP. The limit and the response on rejection are documented.
- [ ] A protocol-level test runs in CI against a stub upstream and covers initialisation, the tool list, a score call, a suppressed call, a not-found resource and an upstream failure.
- [ ] The MCP service's outbound credential is never returned to a caller or written to a log.
- [ ] The connection page for agents states the URL, the rate limit, the privacy statement and the cold-start behaviour.

## 4. Open questions for the owner

1. **Where does it live?** Should this be a guide under `docs/guides/`, a section of an existing guide, or an ADR template in the standards repository?
2. **Is the rate limit a standard value?** school-search uses 60 requests per minute per IP. The standard might name a default or leave it to each service.
3. **Authentication for agents.** v1 is anonymous. Should the standard require a key for MCP once accounts exist (Phase E in school-search), and what does an anonymous caller get then?
4. **Protocol version pinning.** Which MCP protocol version and SDK version does the standard pin, and who updates them? The SDK versions named in the design have not been compiled in CI yet.
5. **Scope of "metric".** Does the standard apply only to services that publish scores, or to any service exposing aggregated figures about organisations?
6. **Relation to METRIC-ETHICS.** Should checklist items 4 and 5 be added to the METRIC-ETHICS guide itself, or kept in an MCP guide that cites it?
7. **Who checks the descriptions?** Item 8 is a wording test. Is a test enough, or does the standard require a human review of tool descriptions when they change?
8. **Naming of the reference service.** The worked example is `school-search`. Should the standard cite it by name, or stay generic with the example in a footnote?

## 5. What is not claimed

- Nothing here has been filed, reviewed or accepted by anyone other than the author of `school-search` PR 4.
- The protocol-level test that item 10 describes is written for `school-search` and has not run in CI yet.
- The standards repository was read for METRIC-ETHICS only (`docs/guides/METRIC-ETHICS.md`, SHA f80d3d2c). The other guide names
  in this document are as referenced by the project's specs, not as read by the author.
