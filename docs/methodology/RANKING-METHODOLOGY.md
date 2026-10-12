# Ranking methodology

- **Methodology version:** `1.0.0` (`ScoringPolicy.MethodologyVersion`)
- **Status:** Specified in Phase B. The engine is in review; it is not compiled in the build environment
  that wrote this document. Not yet published anywhere. Replaces the `0.0-draft`.
- **Data vintage:** `<OPEN: set by the import from the SIO file date and the CKE exam year>`. Until an import
  runs, the API returns `dataVintage: null`.
- **Decision record:** [ADR-0009](../adr/0009-cohort-publication-threshold.md) (threshold 9, profile band 5 to 8).

Where a value is still open, it is marked **OPEN** and listed in §10. Nothing here is a measured statistic.

## 1. What the score is, and what it is not

The score says how far a primary school's average exam result in a given year sits from the national average
for the same subjects and year, in percentage points, after a penalty for pupils who did not sit the exam.
It is a descriptive proxy with a stated uncertainty. It is not a verdict on the school.

It is **not**:

- an official or governmental publication, and not an assessment by any public authority;
- a ranking of teachers, of pupils, or of any individual person. There is no teacher or pupil entity and no
  per-person view;
- a measure of the quality of teaching on its own. It does not adjust for the pupils a school admits;
- a value-added measure. This methodology does not estimate how much a school adds to individual pupils
  (no such measure is used, and none is available from the sources recorded in `docs/data/SOURCES.md`);
- a basis for choosing a school by itself. It is one input next to the school's context, its catchment and the
  family's own knowledge.

## 2. Anti-goals, and how they are enforced

- **No per-person view.** Unit of evaluation: the school and its cohort in one year.
  *Enforced by absence:* no teacher or pupil entity exists in the model.
- **No ascending sort on a composite score.** The ranking API only ranks descending (§4.5).
  *Enforced by absence:* there is no ascending or "bottom" ranking method.
- **No score below the threshold.** Suppression is `limited` (5 to 8 pupils who sat) or `insufficient_cohort` (below 5),
  never null or 0 (§5). *Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*
- **Scoring does not reference billing, entitlement or user types.** *Enforced by an architecture test.*
- **No nationality or foreign-pupil data** is read or produced.
- **No inferred heuristics about people** in any input or component.
- **Every value carries its provenance** (`source`, `sourceUrl`, `asOf`, `method`), and every score carries its
  cohort size, interval and `methodologyVersion` in the same object.

## 3. Inputs

| Input | Source | Status |
|---|---|---|
| School identity, type, gmina, primary-school filter | SIO register (CC0) | Per brief; column names are an **UNVERIFIED** default map (`SOURCES.md` §5) |
| Class VIII headcount for the school (denominator of coverage) | SIO class VIII counts | Per brief; **UNVERIFIED** column map |
| Pupils who sat each exam, mean percent per subject | CKE per-school export | **OPEN**: export format, join key and licence (`SOURCES.md` §3.3, ADR-0012 pending) |
| National mean per subject per year | National baseline file | **OPEN**: publisher and licence to confirm |
| Pupil standard deviation per subject per year (`PupilStdDevPoints`) | Not yet identified | **OPEN**: source not known. The engine requires it to be greater than 0, and it is the input the interval depends on. |

The engine takes these as plain values (`SchoolExamInput`, `NationalBaseline`). It does no I/O and reads no clock.

## 4. The algorithm

Subjects: Polish, Mathematics, Foreign language, each with equal weight. Notation below: `s` is a subject,
`k` is the number of subjects used.

### 4.1 Cohort and the publication check

```
cohort = max(PupilsSat over the subjects in the input)     (0 if there are none)
```

The band is then decided (`ScoringPolicy.LimitedBandFloor = 5`, `ScoringPolicy.PublicationThreshold = 9`):

- `headcount <= 0` or `cohort < 5`: `insufficient_cohort`, no value.
  ```
  ScoreDto(score: null, reason: "insufficient_cohort", cohortSize: cohort, interval: null, methodologyVersion: "1.0.0")
  ```
- `5 <= cohort <= 8` (and `headcount > 0`): `limited`, no value. Cohort size and subject averages only; no score,
  no interval, no rank. The score is not computed, so no number can leak from this branch.
  ```
  ScoreDto(score: null, reason: "limited", cohortSize: cohort, interval: null, methodologyVersion: "1.0.0")
  ```
- `cohort >= 9`: the score is computed (§4.2 to §4.4).

The subject checks in §4.6 run first: a subject set with no overlap throws, and so does a subject with zero pupils
who sat. Only then is the band decided. The cohort is the number of pupils who **sat** the exam, not
the class VIII headcount (see ADR-0009 for why, and for the open wording point).

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

### 4.2 Coverage and the blend

```
coverage = clamp(cohort / headcount, 0, 1)
factor   = 1                        if coverage >= 0.8   (CoverageFloor)
         = coverage / 0.8           otherwise
```

Coverage is the counter-metric. It is **blended into the score**, not shown beside it. A school cannot raise its
score by leaving out pupils: each pupil left out lowers `coverage`, and below 0.8 the factor drops linearly with it.

### 4.3 Deviation per subject and the score

For each subject present in both the input and the baseline:

```
effective_s = MeanPercent_s * factor
dev_s       = effective_s - NationalMean_s          (percentage points; 0 = national average)
Score       = (sum of dev_s over those subjects) / k          (equal weights)
```

### 4.4 Interval

```
se_s  = PupilStdDevPoints_s / sqrt(PupilsSat_s) * factor
se    = sqrt(sum of se_s^2) / k
low   = Score - 1.96 * se
high  = Score + 1.96 * se
```

The interval reflects **sampling noise of pupils** only. It does not capture bias from the mix of pupils, from
coverage that was not chance, or from errors in the input files. Read it as "how much this number could move
from pupil-level chance alone", and nothing more (§8).

### 4.5 Ranking

`RankingBuilder.RankDescending` ranks only schools whose score is not null:

- sorted **descending** by score; ties share a rank (competition ranking: 1, 1, 3);
- ties are ordered stably by slug;
- suppressed schools are not ranked. They are returned in a separate `unranked` list.

No ascending or bottom-of-list ordering exists (§2).

### 4.6 Subject matching and errors

- `Score` uses only subjects present in **both** the school input and the national baseline.
- If the subject sets do not overlap (after the §4.1 check passes), the engine throws `InvalidOperationException`.
  It does not hide a data bug as a suppressed score.
- `input.Year` must equal `baseline.Year`, otherwise `ArgumentException`.
- `PupilStdDevPoints` must be greater than 0 (the baseline type enforces it).

## 5. Threshold, suppression and the 5 to 8 band

| Cohort (pupils who sat) | `scoreStatus` | Output |
|---|---|---|
| 9 or more (and headcount above 0) | `published` | Score, interval, cohort size, `methodologyVersion`, provenance |
| 5 to 8 (and headcount above 0) | `limited` | Cohort size and subject averages (descriptive data), no score, no interval, no rank |
| 0 to 4, or headcount 0 or less | `insufficient_cohort` | Cohort size, no value, no rank |

The threshold `9` is in one place: `ScoringPolicy.PublicationThreshold`. The floor `5` is `ScoringPolicy.LimitedBandFloor`.

The **limited-confidence profile for 5 to 8 pupils** (ADR-0009) is produced by the engine (`ScoreDto.Reason`
`limited`) and shown by the web as a profile with the line "Dla tej grupy nie publikujemy wyniku ani miejsca w
zestawieniu." It is excluded from the ranking. Tests assert the boundary values 4, 5, 8 and 9. Whether this change
needs a methodology version bump or an ADR-0009 amendment is an owner decision, still open.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

Suppression is an explicit state: a null score with reason `insufficient_cohort` and the cohort size. A null or
zero value is never used to mean "no data".

## 6. Worked example (illustrative numbers, not data)

The numbers below are synthetic. They show how the rules combine. They are not drawn from any school, year or
publication. The national mean is 60 in each subject. The pupil standard deviation is set to 20 for illustration
only; the real value is OPEN (§3). The class VIII headcount is 20.

**Case A: everyone sits the exam.**

| Quantity | Value |
|---|---|
| Pupils who sat (each subject) | 20 |
| Coverage, factor | 20/20 = 1.00; factor 1 |
| Mean percent (each subject) | 70 |
| Deviation (each subject) | 70 − 60 = +10 |
| **Score** | **+10.0** |
| se (each subject) / se | 20/√20 = 4.47 per subject; √(3 × 4.47²)/3 = 2.58 |
| Interval (1.96 × se) | 10.0 ± 5.1, i.e. 4.9 to 15.1 |

**Case B: fewer pupils sit, and the average rises.**

| Quantity | Value |
|---|---|
| Pupils who sat (each subject) | 10 |
| Coverage, factor | 10/20 = 0.50; factor 0.50/0.80 = 0.625 |
| Mean percent (each subject) | 75 (up 5 points on Case A) |
| Effective mean | 75 × 0.625 = 46.9 |
| Deviation (each subject) | 46.9 − 60 = −13.1 |
| **Score** | **−13.1** |
| se / se | 20/√10 × 0.625 = 3.95 per subject; √(3 × 3.95²)/3 = 2.28 |
| Interval (1.96 × se) | −13.1 ± 4.5, i.e. −17.6 to −8.7 |

**Reading.** The raw average went up (70 to 75), but the score fell from +10.0 to −13.1 and ended below the
national average, because only half the cohort sat. The blend does what it is for: excluding pupils cannot
raise the score. The interval also widened, because fewer pupils sit and the factor shrinks the effective mean.

**Suppression example.** Ten pupils sat in each subject, so the score is published (cohort 10 is at least 9).
Had 8 pupils sat, the same school would return `limited` with `cohortSize: 8`, the subject averages and no score,
no interval and no rank, and it would not appear in the ranking. Had 4 pupils sat, it would return
`insufficient_cohort` with `cohortSize: 4` and no value.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

Display rounding (here one decimal) is a presentation rule for Phase C. The engine does not round.

## 7. What travels with every score

Every published score is one object containing:

- `score`, `interval` (low, high), `cohortSize` (pupils who sat), `methodologyVersion`;
- `source`, `sourceUrl`, `asOf` and `method` (the provenance rule);
- for a suppressed result: `score: null`, `reason: "limited"` (5 to 8 pupils) or `reason: "insufficient_cohort"` (below 5),
  `cohortSize`, `methodologyVersion`.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

A value is never returned without these. In MCP responses the same fields are returned, and user-generated
content is labelled `userGenerated` and kept separate (Phase E).

## 8. Known biases and limits, stated plainly

1. **A raw average tracks intake as much as teaching.** Schools with more advantaged or more selective intakes
   tend to get higher averages whatever they do. This methodology does not adjust for the pupils a school
   admits, because the inputs for such an adjustment are not part of this release. Treat a school's position
   as partly a statement about who it teaches.
2. **There is no public value-added measure for primary schools in this methodology.** So the score cannot
   separate a school's effect from its intake.
3. **The coverage penalty pulls means towards zero.** Multiplying the mean by the factor moves a low-coverage
   school towards 0 points, which is a national-average-relative figure only by construction. A low-coverage
   school is pulled down whatever its pupils achieved. This is intentional (it is the counter-metric), but it
   is a blunt rule and is recorded as a limit.
4. **Coverage is clamped at 1.** If the cohort that sat is larger than the headcount, the data disagree. The clamp
   hides that. The import must reject such rows and report them. Until it does, this is a known gap.
5. **The interval is pupil-level chance only** (§4.4). It does not cover bias, input errors or the national mean's
   own uncertainty.
6. **Small numbers are noisy even above the threshold.** Nine pupils is the lowest published cohort, and the
   interval is wide there. The threshold is a product and ethics judgement, not a statistical proof (ADR-0009).
7. **Averages mix subjects of different difficulty and different spread.** The equal weights and the single
   standard deviation per subject are simplifications.
8. **Data may be out of date or misjoined.** Every value shows its `asOf`. A school's CKE results are joined to
   its SIO record by an exact key only (ADR-0012, pending). Rows that do not match are rejected and reported, not
   guessed.

## 9. Versioning and change control

- Any change to inputs, weights, the threshold, the blend, the interval or the rounding rules is a new
  `methodologyVersion`, and an ADR is recorded before it ships.
- Old versions stay readable, so a published score can be reproduced from its version.
- The threshold lives in one place. Changing it changes the version.

## 10. Open items before publication

1. **Pupil standard deviation source** (`PupilStdDevPoints`, §3). OPEN. Without it there is no interval, and the
   engine will not publish a score.
2. **CKE per-school export format, join key and licence** (§3, ADR-0012). OPEN.
3. **National mean per subject per year**: publisher, licence and exact values. OPEN.
4. **Sat counts per school** from CKE (`SOURCES.md` §3.6). OPEN.
5. **Row-level reconciliation of the cohort counts** with the Phase B gate (ADR-0009). OPEN.
6. **The 5 to 8 limited-confidence profile**: built (engine `limited`, web profile). Its boundary tests and the
   limited-band e2e spec need a first CI run. Whether it needs a version bump or an ADR-0009 amendment is an owner
   decision, still open.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*
7. **Display rounding and the confidence wording** on the public page. Phase C.

## 11. How to read a score and its confidence together

A score is never shown alone. It always comes with its interval and the number of pupils behind it.

- **The score** is the distance, in percentage points, from the national average for the same subjects and year.
  `0` is the national average. `+5` means about five points above it, after the coverage adjustment. `−5` means
  five points below.
- **The interval** is the range the score could plausibly sit in, given how many pupils sat and how spread out
  their results are. A narrow interval means the number is steady. A wide interval means the same school, with a
  few different pupils, could have landed quite differently.
- **The cohort size** is the number of pupils who sat the exam. Nine is the lowest number that gets a score.
  Two schools with the same score but cohorts of 9 and 90 are different claims. The second is steadier.
- **Coverage** is the share of the year group that sat. A school with low coverage has its score pulled down on
  purpose.
- **Compare with care.** Two schools are only comparable if their intakes and their coverage are similar. Use the
  intervals: if two intervals overlap, the data do not show a clear difference.
- **"Limited profile"** (5 to 8 pupils who sat) means the cohort is too small for a score, an interval or a rank. The
  page shows the cohort size and the subject averages, marked as coming from a small group. It is not a zero and it
  says nothing bad about the school.
- **"Insufficient cohort"** (fewer than 5 pupils who sat) means there are not enough pupils for any exam figure to be
  shown. It is not a zero and it says nothing bad about the school.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

## 12. Podsumowanie dla czytelnika (po polsku)

**Co pokazuje wynik.** Wynik szkoły to odległość, w punktach procentowych, średniego wyniku jej uczniów z
egzaminu od średniej krajowej dla tych samych przedmiotów i roku. Wynik `0` oznacza średnią krajową. Wynik
`+5` oznacza, że szkoła wypada około pięć punktów powyżej średniej, po uwzględnieniu liczby uczniów, którzy
przystąpili do egzaminu.

**Przedział.** Obok wyniku zawsze podajemy przedział. Pokazuje on, w jakim zakresie wynik mógłby się znaleźć
przy innym zestawie uczniów. Wąski przedział oznacza wynik stabilny. Szeroki przedział oznacza, że wynik jest
mało pewny.

**Liczba uczniów.** Podajemy liczbę uczniów, którzy przystąpili do egzaminu. Wynik publikujemy tylko wtedy, gdy
było ich co najmniej dziewięciu. Przy 5–8 uczniach pokazujemy profil ograniczony: liczbę uczniów i średnie wyniki
z przedmiotów, bez wyniku, bez przedziału i bez miejsca w zestawieniu („Dla tej grupy nie publikujemy wyniku ani
miejsca w zestawieniu"). Przy mniej niż 5 uczniach pokazujemy informację: „za mało uczniów, aby podać wynik".
To nie jest zero i nie jest oceną szkoły.

*Status: wording updated to match the implementation; version bump / ADR amendment pending owner decision.*

**Udział w egzaminie.** Jeśli mało uczniów z rocznika przystąpiło do egzaminu, wynik jest obniżany celowo.
Szkoła nie może poprawić wyniku przez to, że pominie słabszych uczniów.

**Czego wynik nie mówi.**
- Nie ocenia nauczycieli ani uczniów. Nie ma tu danych o konkretnych osobach.
- Nie uwzględnia w pełni tego, jacy uczniowie trafiają do szkoły. Szkoły o różnym składzie uczniów nie są
  w pełni porównywalne.
- Nie jest publikacją urzędową ani oficjalną oceną szkoły.
- Nie zastępuje wizyty w szkole i rozmowy z nauczycielami.

Przy porównywaniu szkół zwróć uwagę na przedziały: jeśli się nakładają, dane nie pokazują wyraźnej różnicy.
