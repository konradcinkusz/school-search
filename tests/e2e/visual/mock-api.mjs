#!/usr/bin/env node
// Dependency-free mock of the catalog, reviews, billing and agent services for the visual harness.
//
// Why it exists: the real catalog API is .NET and the machine running the visual harness has no .NET SDK.
// This mock answers the same routes with the same camelCase JSON shapes the web app reads (web/app/src/lib/*.ts),
// backed by a small copy of the synthetic demo seed (src/SchoolSearch.Api/Demo/DemoSeed.cs and DemoPreschoolSeed.cs).
// Every row carries the source "DEMO SEED (synthetic)". Nothing here describes a real school.
//
// Ports: catalog MOCK_PORT (default 8080); reviews MOCK_REVIEWS_PORT (8082), billing MOCK_BILLING_PORT (8083),
// agent MOCK_AGENT_PORT (8084). Set MOCK_LOG=1 to log every request to stderr.

import { createServer } from "node:http";
import { randomBytes, randomUUID } from "node:crypto";
import { pathToFileURL } from "node:url";

// ---------------------------------------------------------------------------
// Constants mirrored from the C# seed and scoring policy
// ---------------------------------------------------------------------------

const DEMO_SOURCE = "DEMO SEED (synthetic)";
const DEMO_SOURCE_URL = "https://example.invalid/demo";
const DEMO_METHOD = "synthetic";
const AS_OF = "2026-01-01";
const METHODOLOGY_VERSION = "1.0.0";
const PUBLICATION_THRESHOLD = 9; // ScoringPolicy.PublicationThreshold
const LIMITED_BAND_FLOOR = 5; // ScoringPolicy.LimitedBandFloor (ADR-0009): below this, insufficient_cohort
const COVERAGE_FLOOR = 0.8; // ScoringPolicy.CoverageFloor
const INTERVAL_Z = 1.96; // ScoringPolicy.IntervalZ
const EXAM_YEARS = [2024, 2025];
const DEFAULT_EXAM_YEAR = 2025;
const SUBJECTS = ["polish", "mathematics", "foreign_language"];
const SCHOOL_CLASS_LEVEL = "VIII";
// Mirrors PreschoolReadModel.Notice (src/SchoolSearch.Api/Preschools/PreschoolReadModel.cs).
const NOTICE_PRESCHOOL =
  "Przedszkola nie mają wyniku jakości. Pokazujemy praktyczne informacje: lokalizację, opłaty, godziny i wolne miejsca, każdą ze źródłem i datą.";
const REVIEWS_NOTICE =
  "Opinie pochodzą od zalogowanych użytkowników z potwierdzonym adresem e-mail. Nie weryfikujemy, czy autor jest rodzicem ucznia tej szkoły. Opinie nie wpływają na wynik szkoły.";

const GMINAS = [
  { teryt: "9999999", name: "Przykładowo", coverage: "street_list" },
  { teryt: "9999998", name: "Przykładowo-Wschód", coverage: "none" },
];

const COVERAGE = {
  "9999999": {
    state: "street_list",
    resolutionUrl: "https://example.invalid/demo-uchwala",
    resolutionDate: "2025-09-01",
  },
  "9999998": { state: "none", resolutionUrl: null, resolutionDate: null },
};

const ex = (sat, polish, mathematics, foreign_language) => ({ sat, polish, mathematics, foreign_language });

// Mirrors DemoSeed.Schools. e2024 / e2025 are null when the school has no exam data for that year.
const SCHOOLS = [
  school(900001, "demo-sp-1", "Szkoła Podstawowa nr 1 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 120, 52.001, 19.001, ex(118, 72.5, 61, 80), ex(117, 71, 63, 78.5)),
  school(900002, "demo-sp-2", "Szkoła Podstawowa nr 2 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 9, 52.002, 19.002, ex(9, 64, 58, 70), ex(9, 66, 60, 72)),
  school(900003, "demo-sp-3", "Szkoła Podstawowa nr 3 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 8, 52.003, 19.003, ex(8, 50, 48, 55), ex(8, 52, 49, 56)),
  school(900004, "demo-sp-4", "Szkoła Podstawowa nr 4 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 60, 52.004, 19.004, ex(20, 85, 82, 88), ex(21, 86, 83, 89)),
  school(900005, "demo-sp-5", "Szkoła Podstawowa nr 5 w Przykładowie (niepubliczna, dane demonstracyjne)", "9999999", false, false, 45, 52.005, 19.005, ex(40, 58, 55, 60), ex(41, 57, 54, 61)),
  school(900006, "demo-sp-6", "Szkoła Podstawowa nr 6 w Przykładowie (bez danych egzaminacyjnych, dane demonstracyjne)", "9999999", true, false, 50, 52.006, 19.006, null, null),
  school(900007, "demo-sp-7", "Szkoła Podstawowa nr 7 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 100, 52.007, 19.007, ex(96, 70, 68, 74), ex(95, 71, 69, 75)),
  school(900008, "demo-sp-8", "Szkoła Podstawowa nr 8 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 75, 52.008, 19.008, ex(70, 64, 60, 66), ex(72, 63, 61, 65)),
  school(900009, "demo-sp-9", "Szkoła Podstawowa nr 9 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 50, 52.009, 19.009, ex(49, 55, 52, 58), ex(48, 56, 53, 59)),
  school(900010, "demo-sp-10", "Szkoła Podstawowa specjalna nr 10 w Przykładowie (dane demonstracyjne)", "9999999", true, true, 25, 52.01, 19.01, ex(24, 48, 45, 50), ex(24, 47, 46, 51)),
  school(900011, "demo-sp-11", "Szkoła Podstawowa nr 11 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 90, 52.011, 19.011, ex(88, 58, 55, 62), ex(87, 60, 57, 63)),
  school(900012, "demo-sp-12", "Szkoła Podstawowa nr 12 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 40, 52.012, 19.012, ex(39, 61, 59, 63), ex(38, 62, 60, 64)),
  school(900013, "demo-sp-13", "Szkoła Podstawowa nr 13 w Przykładowie (dane demonstracyjne)", "9999999", true, false, 33, 52.013, 19.013, ex(31, 66, 64, 69), ex(31, 67, 65, 70)),
  school(900014, "demo-sp-14", "Szkoła Podstawowa w Przykładowie-Wschód (dane demonstracyjne)", "9999998", true, false, 30, 52.1, 19.1, ex(29, 60, 58, 61), ex(29, 59, 57, 62)),
];

// Mirrors DemoSeed.Streets: normalised street, number range (null = any), school id.
const STREETS = [
  { gmina: "9999999", street: "przykladowa", from: 1, to: 20, schoolId: 900001 },
  { gmina: "9999999", street: "przykladowa", from: 21, to: 40, schoolId: 900002 },
  { gmina: "9999999", street: "testowa", from: null, to: null, schoolId: 900007 },
];

// Mirrors DemoPreschoolSeed.Preschools.
const PRESCHOOLS = [
  preschool(910001, "przedszkole-demonstracyjne-nr-1", "Przedszkole Demonstracyjne nr 1 (dane demonstracyjne)", true, "ul. Demonstracyjna 1", "99-900", 52.001, 19.001,
    { tuitionMin: 350, tuitionMax: 480, meal: 120, opens: "06:30", closes: "17:00", free: 3, confidence: "reported" }),
  preschool(910002, "przedszkole-demonstracyjne-nr-2", "Przedszkole Demonstracyjne nr 2 (dane demonstracyjne)", true, "ul. Demonstracyjna 2", "99-900", 52.002, 19.002,
    { tuitionMin: 0, tuitionMax: 250, meal: 110, opens: "07:00", closes: "16:30", free: 0, confidence: "reported" }),
  preschool(910003, "przedszkole-demonstracyjne-nr-3", "Przedszkole Demonstracyjne nr 3 (niepubliczne, dane demonstracyjne)", false, "ul. Przykładowa 3", "99-901", 52.003, 19.003,
    { tuitionMin: 900, tuitionMax: 1200, meal: null, opens: "07:00", closes: "18:00", free: 5, confidence: "unverified" }),
  preschool(910004, "przedszkole-demonstracyjne-nr-4", "Przedszkole Demonstracyjne nr 4 (niepubliczne, dane demonstracyjne)", false, "ul. Przykładowa 4", "99-901", 52.004, 19.004, null),
  preschool(910005, "przedszkole-demonstracyjne-nr-5", "Przedszkole Demonstracyjne nr 5 (dane demonstracyjne)", true, "ul. Demonstracyjna 5", "99-900", 52.005, 19.005,
    { tuitionMin: null, tuitionMax: null, meal: null, opens: "07:30", closes: null, free: null, confidence: "unverified" }),
];

const DEMO_TOKEN = "demoTokenVisual0000000001";
// Mirrors CorrectionRequestValidator (src/SchoolSearch.Api/Corrections/CorrectionRequestValidator.cs).
const CORRECTION_FIELDS = new Set(["name", "governing_body", "cohort", "address", "fee", "attribute", "score", "methodology", "other"]);
const CORRECTION_PRESCHOOL_FIELDS = new Set(["tuition_min", "tuition_max", "hours", "free_places", "address", "other"]);
const CORRECTION_MIN_MESSAGE = 10;
const CORRECTION_MAX_MESSAGE = 2000;
const CORRECTION_MAX_RAW_MESSAGE = 8000;
const CORRECTION_MAX_EMAIL = 200;
const EMAIL_SHAPE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const corrections = new Map([
  [
    DEMO_TOKEN,
    { status: "in_review", statusReason: null, schoolSlug: "demo-sp-1", field: "score", kind: "school", createdAt: "2026-10-01T10:00:00Z" },
  ],
]);
// Reviews service: notices received over POST /api/v1/notices (DsaEndpoints.SubmitNotice). Reviews are never stored
// in the mock, so a reviewId always answers 404, as the real service does for an unknown review.
const NOTICE_CATEGORIES = new Set(["illegal_content", "personal_data", "defamation", "other"]);
const notices = new Map();

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------

function school(id, slug, name, teryt, isPublic, isSpecial, pupils, latitude, longitude, e2024, e2025) {
  return { id, slug, name, teryt, isPublic, isSpecial, pupils, latitude, longitude, exams: { 2024: e2024, 2025: e2025 } };
}

function preschool(id, slug, name, isPublic, street, postalCode, latitude, longitude, facts) {
  return { id, slug, name, isPublic, street, postalCode, latitude, longitude, facts };
}

const round1 = (x) => Math.round(x * 10) / 10;
const round2 = (x) => Math.round(x * 100) / 100;
const fold = (s) => s.toLowerCase().normalize("NFD").replace(/\p{M}/gu, "").replace(/ł/g, "l");
const traceId = () => `mock-${randomBytes(6).toString("hex")}`;
const isSlug = (s) => s.length <= 120 && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(s);
const UUID_SHAPE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function intParam(raw, def, min, max) {
  if (raw === null || raw === undefined || raw === "") return def;
  const n = Number(raw);
  if (!Number.isInteger(n)) return def;
  return Math.min(max, Math.max(min, n));
}

function numParam(raw) {
  if (raw === null || raw === undefined || raw === "") return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

function haversineKm(lat1, lon1, lat2, lon2) {
  const R = 6371;
  const rad = (d) => (d * Math.PI) / 180;
  const dLat = rad(lat2 - lat1);
  const dLon = rad(lon2 - lon1);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

function send(res, status, body) {
  const payload = body === undefined ? "" : JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "cache-control": "no-store",
    "content-length": Buffer.byteLength(payload),
  });
  res.end(payload);
}

function fail(res, status, code, message) {
  send(res, status, { error: { code, message, traceId: traceId() } });
}

function readBody(req, limit = 65536) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > limit) {
        reject(new Error("body too large"));
        req.destroy();
      } else {
        chunks.push(chunk);
      }
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function decodeSegment(raw) {
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

// ---------------------------------------------------------------------------
// Scoring (mirrors ScoringEngine behaviour for the demo rows)
// ---------------------------------------------------------------------------

function scoreFor(s, year) {
  const exam = s.exams[year];
  if (!exam) return { status: "no_exam_data", block: null };
  const base = {
    methodologyVersion: METHODOLOGY_VERSION,
    examYear: year,
    asOf: AS_OF,
    source: DEMO_SOURCE,
    cohortSize: exam.sat,
  };
  // Below the CKE floor (5): nothing about the exam is shown.
  if (exam.sat < LIMITED_BAND_FLOOR) {
    return {
      status: "insufficient_cohort",
      block: { ...base, score: null, reason: "insufficient_cohort", intervalLow: null, intervalHigh: null },
    };
  }
  // 5 to 8 pupils (ADR-0009): limited profile. Cohort size only; no score, no interval, no rank.
  if (exam.sat < PUBLICATION_THRESHOLD) {
    return {
      status: "limited",
      block: { ...base, score: null, reason: "limited", intervalLow: null, intervalHigh: null },
    };
  }
  const mean = (exam.polish + exam.mathematics + exam.foreign_language) / 3;
  const coverage = Math.min(1, exam.sat / s.pupils);
  const factor = coverage >= COVERAGE_FLOOR ? 1 : coverage / COVERAGE_FLOOR;
  const score = round1(mean * factor);
  const half = (INTERVAL_Z * 20) / Math.sqrt(exam.sat);
  return {
    status: "published",
    block: {
      ...base,
      score,
      reason: null,
      intervalLow: round1(Math.max(0, score - half)),
      intervalHigh: round1(Math.min(100, score + half)),
    },
  };
}

function listItem(s, year, distanceKm) {
  const { status, block } = scoreFor(s, year);
  const gmina = GMINAS.find((g) => g.teryt === s.teryt);
  return {
    slug: s.slug,
    name: s.name,
    gminaTeryt: s.teryt,
    gminaName: gmina ? gmina.name : null,
    isPublic: s.isPublic,
    isSpecial: s.isSpecial,
    latitude: s.latitude,
    longitude: s.longitude,
    distanceKm,
    scoreStatus: status,
    score: block,
  };
}

function trendPoint(s, year) {
  const exam = s.exams[year];
  const { status, block } = scoreFor(s, year);
  return {
    examYear: year,
    scoreStatus: status,
    score: block,
    coveragePercent: exam ? round1(Math.min(100, (exam.sat / s.pupils) * 100)) : null,
    subjects: exam
      ? SUBJECTS.map((subject) => ({ subject, meanPercent: exam[subject], pupilsSat: exam.sat }))
      : [],
  };
}

function schoolProfile(s) {
  const base = listItem(s, DEFAULT_EXAM_YEAR, null);
  const provenance = [
    { field: "score", source: DEMO_SOURCE, sourceUrl: DEMO_SOURCE_URL, asOf: AS_OF, method: DEMO_METHOD },
    { field: "cohorts", source: DEMO_SOURCE, sourceUrl: DEMO_SOURCE_URL, asOf: AS_OF, method: DEMO_METHOD },
    { field: "coordinates", source: DEMO_SOURCE, sourceUrl: DEMO_SOURCE_URL, asOf: AS_OF, method: DEMO_METHOD },
  ];
  return {
    ...base,
    governingBody: s.isPublic
      ? { typeCode: 1, typeLabel: "Jednostka samorządu terytorialnego (gmina)", labelVerified: true, isNaturalPerson: false }
      : { typeCode: 4, typeLabel: "Osoba prawna niebędąca jednostką samorządu", labelVerified: true, isNaturalPerson: false },
    faith: { derived: false, declared: null, note: "Dane demonstracyjne: brak informacji o profilu wyznaniowym." },
    cohorts: [2023, 2024].map((schoolYear) => ({
      schoolYear,
      classLevel: SCHOOL_CLASS_LEVEL,
      pupils: s.pupils,
      oddzialy: s.pupils >= 50 ? 3 : 1,
    })),
    trend: EXAM_YEARS.map((year) => trendPoint(s, year)),
    provenance,
    methodologyVersion: METHODOLOGY_VERSION,
  };
}

function findSchool(slug) {
  return SCHOOLS.find((s) => s.slug === slug) ?? null;
}

// ---------------------------------------------------------------------------
// Preschools
// ---------------------------------------------------------------------------

function preschoolFacts(p) {
  if (!p.facts) return null;
  const f = p.facts;
  return {
    monthlyTuition: { minPln: f.tuitionMin, maxPln: f.tuitionMax },
    monthlyMealPln: f.meal,
    hours: { opens: f.opens, closes: f.closes },
    freePlaces: { count: f.free, asOf: f.free === null ? null : AS_OF },
    source: {
      source: DEMO_SOURCE,
      sourceUrl: DEMO_SOURCE_URL,
      asOf: AS_OF,
      method: DEMO_METHOD,
      confidence: f.confidence,
    },
  };
}

function preschoolListItem(p, distanceKm) {
  return {
    slug: p.slug,
    name: p.name,
    gminaTeryt: "9999999",
    gminaName: "Przykładowo",
    isPublic: p.isPublic,
    latitude: p.latitude,
    longitude: p.longitude,
    distanceKm,
    monthlyTuition: {
      minPln: p.facts ? p.facts.tuitionMin : null,
      maxPln: p.facts ? p.facts.tuitionMax : null,
    },
    freePlacesCount: p.facts ? p.facts.free : null,
    facts: preschoolFacts(p),
  };
}

// GET /api/v1/map/markers over the demo data. Mirrors MapEndpoints.cs: bbox required, kind, q (>= 2 chars),
// gmina, publicOnly, maxTuition / hasFreePlaces (preschools only; 400 with kind=school), limit 1..500 (default 300),
// ordered by slug then kind, no score/rank fields.
function mapMarkersHandler(sp, res) {
  const bad = (message) => fail(res, 400, "validation_failed", message);
  const coord = (name, min, max) => {
    const raw = sp.get(name);
    if (raw === null || raw.trim() === "") return null;
    const n = Number(raw.trim().replace(",", "."));
    return Number.isFinite(n) && n >= min && n <= max ? n : null;
  };
  const south = coord("south", -90, 90);
  const north = coord("north", -90, 90);
  const west = coord("west", -180, 180);
  const east = coord("east", -180, 180);
  if (south === null || north === null) return bad("Parametry south i north są wymagane: szerokość geograficzna od -90 do 90.");
  if (west === null || east === null) return bad("Parametry west i east są wymagane: długość geograficzna od -180 do 180.");
  if (south >= north) return bad("Parametr south musi być mniejszy niż north.");
  if (west >= east) return bad("Parametr west musi być mniejszy niż east.");

  const kind = (sp.get("kind") ?? "").trim().toLowerCase() || "all";
  if (!["school", "preschool", "all"].includes(kind)) return bad("Parametr kind przyjmuje wartości school, preschool albo all.");
  const gmina = (sp.get("gmina") ?? "").trim();
  if (gmina && !/^\d{7}$/.test(gmina)) return bad("Parametr gmina musi być kodem TERYT gminy (7 cyfr).");
  let q = (sp.get("q") ?? "").trim();
  if (q.length > 100) return bad("Parametr q może mieć najwyżej 100 znaków.");
  if (q.length < 2) q = "";
  const flag = (name) => {
    const raw = (sp.get(name) ?? "").trim().toLowerCase();
    if (raw === "") return false;
    if (raw === "true") return true;
    if (raw === "false") return false;
    return null;
  };
  const publicOnly = flag("publicOnly");
  if (publicOnly === null) return bad("Parametr publicOnly przyjmuje wartości true albo false.");
  const hasFree = flag("hasFreePlaces");
  if (hasFree === null) return bad("Parametr hasFreePlaces przyjmuje wartości true albo false.");
  let maxTuition = null;
  const maxRaw = (sp.get("maxTuition") ?? "").trim();
  if (maxRaw !== "") {
    if (!/^\d+$/.test(maxRaw)) return bad("Parametr maxTuition musi być kwotą w złotych, liczbą całkowitą nieujemną.");
    maxTuition = Number(maxRaw);
  }
  let limit = 300;
  const limitRaw = (sp.get("limit") ?? "").trim();
  if (limitRaw !== "") {
    if (!/^[+-]?\d+$/.test(limitRaw)) return bad("Parametr limit musi być liczbą całkowitą.");
    limit = Math.min(500, Math.max(1, Number(limitRaw)));
  }
  const preschoolOnly = maxTuition !== null || hasFree;
  if (kind === "school" && preschoolOnly) return bad("maxTuition and hasFreePlaces apply to preschools only.");

  const inBox = (x) => x.latitude >= south && x.latitude <= north && x.longitude >= west && x.longitude <= east;
  const nameOk = (x) => q === "" || fold(x.name).includes(fold(q));
  const markers = [];
  if (kind !== "preschool" && !preschoolOnly) {
    for (const s of SCHOOLS) {
      if (!inBox(s) || !nameOk(s)) continue;
      if (gmina && s.teryt !== gmina) continue;
      if (publicOnly && !s.isPublic) continue;
      const g = GMINAS.find((x) => x.teryt === s.teryt);
      markers.push({
        kind: "school", slug: s.slug, name: s.name, latitude: s.latitude, longitude: s.longitude,
        isPublic: s.isPublic, gminaName: g ? g.name : null, monthlyTuition: null, freePlacesCount: null,
      });
    }
  }
  if (kind !== "school") {
    for (const x of PRESCHOOLS) {
      if (!inBox(x) || !nameOk(x)) continue;
      if (gmina && gmina !== "9999999") continue;
      if (publicOnly && !x.isPublic) continue;
      if (hasFree && !(x.facts && x.facts.free !== null && x.facts.free > 0)) continue;
      if (maxTuition !== null && !(x.facts && x.facts.tuitionMin !== null && x.facts.tuitionMin <= maxTuition)) continue;
      markers.push({
        kind: "preschool", slug: x.slug, name: x.name, latitude: x.latitude, longitude: x.longitude,
        isPublic: x.isPublic, gminaName: "Przykładowo",
        monthlyTuition: { minPln: x.facts ? x.facts.tuitionMin : null, maxPln: x.facts ? x.facts.tuitionMax : null },
        freePlacesCount: x.facts ? x.facts.free : null,
      });
    }
  }
  markers.sort((a, b) => (a.slug < b.slug ? -1 : a.slug > b.slug ? 1 : a.kind < b.kind ? -1 : a.kind > b.kind ? 1 : 0));
  const body = { items: markers.slice(0, limit), total: markers.length, truncated: markers.length > limit, limit };
  const payload = JSON.stringify(body);
  res.writeHead(200, {
    "content-type": "application/json; charset=utf-8",
    "cache-control": "public, max-age=60",
    "content-length": Buffer.byteLength(payload),
  });
  res.end(payload);
}

function preschoolProfile(p) {
  return {
    slug: p.slug,
    name: p.name,
    gminaTeryt: "9999999",
    gminaName: "Przykładowo",
    isPublic: p.isPublic,
    street: p.street,
    postalCode: p.postalCode,
    city: "Przykładowo",
    latitude: p.latitude,
    longitude: p.longitude,
    governingBody: {
      typeCode: p.isPublic ? 1 : 4,
      typeLabel: p.isPublic ? "Jednostka samorządu terytorialnego (gmina)" : "Osoba prawna niebędąca jednostką samorządu",
      labelVerified: true,
      isNaturalPerson: false,
    },
    facts: preschoolFacts(p),
    notice: NOTICE_PRESCHOOL,
  };
}

// ---------------------------------------------------------------------------
// Catalog handlers (port MOCK_PORT)
// ---------------------------------------------------------------------------

function listSchools(sp, res) {
  const q = sp.get("q");
  const gmina = sp.get("gmina");
  const isPublic = sp.get("isPublic");
  const lat = numParam(sp.get("lat"));
  const lon = numParam(sp.get("lon"));
  const radiusKm = numParam(sp.get("radiusKm"));
  const sort = sp.get("sort");
  const page = intParam(sp.get("page"), 1, 1, 100000);
  const pageSize = intParam(sp.get("pageSize"), 20, 1, 100);

  let rows = SCHOOLS.map((s) => ({
    s,
    dist: lat !== null && lon !== null ? round2(haversineKm(lat, lon, s.latitude, s.longitude)) : null,
  }));
  if (q) rows = rows.filter((r) => fold(r.s.name).includes(fold(q)));
  if (gmina) rows = rows.filter((r) => r.s.teryt === gmina);
  if (isPublic === "true" || isPublic === "false") rows = rows.filter((r) => r.s.isPublic === (isPublic === "true"));
  if (radiusKm !== null) rows = rows.filter((r) => r.dist !== null && r.dist <= radiusKm);

  if (sort === "score") {
    const scoreOrNeg = (r) => {
      const { block } = scoreFor(r.s, DEFAULT_EXAM_YEAR);
      return block && block.score !== null ? block.score : -1;
    };
    rows.sort((a, b) => scoreOrNeg(b) - scoreOrNeg(a) || a.s.name.localeCompare(b.s.name, "pl"));
  } else if (sort !== "name" && rows.every((r) => r.dist !== null)) {
    rows.sort((a, b) => a.dist - b.dist);
  } else {
    rows.sort((a, b) => a.s.name.localeCompare(b.s.name, "pl"));
  }

  const total = rows.length;
  const items = rows
    .slice((page - 1) * pageSize, page * pageSize)
    .map((r) => listItem(r.s, DEFAULT_EXAM_YEAR, r.dist));
  send(res, 200, { items, page, pageSize, total });
}

function rankingHandler(sp, res) {
  const scope = sp.get("scope") ?? "country";
  if (scope !== "country" && scope !== "gmina") return fail(res, 400, "bad_request", "Nieznany zasięg rankingu.");

  let gminaTeryt = null;
  let pool = SCHOOLS;
  if (scope === "gmina") {
    gminaTeryt = sp.get("gmina");
    if (!gminaTeryt) return fail(res, 400, "bad_request", "Zasięg gmina wymaga parametru gmina.");
    if (!GMINAS.some((g) => g.teryt === gminaTeryt)) {
      return fail(res, 404, "not_found", "Nie ma takiej gminy w katalogu.");
    }
    pool = SCHOOLS.filter((s) => s.teryt === gminaTeryt);
  }

  let year = DEFAULT_EXAM_YEAR;
  const yearRaw = sp.get("year");
  if (yearRaw !== null) {
    const y = Number(yearRaw);
    if (!EXAM_YEARS.includes(y)) return fail(res, 404, "not_found", "Brak rankingu dla tego roku.");
    year = y;
  }

  const page = intParam(sp.get("page"), 1, 1, 100000);
  const pageSize = intParam(sp.get("pageSize"), 50, 1, 100);
  const ranked = pool
    .map((s) => ({ s, sc: scoreFor(s, year) }))
    .filter((x) => x.sc.status === "published")
    .sort((a, b) => b.sc.block.score - a.sc.block.score || a.s.name.localeCompare(b.s.name, "pl"));

  const offset = (page - 1) * pageSize;
  const items = ranked.slice(offset, offset + pageSize).map((x, i) => ({
    rank: offset + i + 1,
    slug: x.s.slug,
    name: x.s.name,
    gminaName: GMINAS.find((g) => g.teryt === x.s.teryt)?.name ?? null,
    score: x.sc.block,
  }));

  send(res, 200, {
    scope,
    gminaTeryt,
    examYear: year,
    methodologyVersion: METHODOLOGY_VERSION,
    items,
    unrankedCount: pool.length - ranked.length,
    total: ranked.length,
    page,
    pageSize,
  });
}

function compareHandler(sp, res) {
  const slugs = (sp.get("slugs") ?? "").split(",").map((s) => s.trim()).filter((s) => s !== "");
  if (slugs.length < 2 || slugs.length > 4 || new Set(slugs).size !== slugs.length || !slugs.every(isSlug)) {
    return fail(res, 400, "bad_request", "Podaj od 2 do 4 różnych identyfikatorów szkół.");
  }
  const items = [];
  for (const slug of slugs) {
    const s = findSchool(slug);
    if (!s) return fail(res, 404, "not_found", `Nie znaleziono szkoły: ${slug}.`);
    items.push(schoolProfile(s));
  }
  send(res, 200, { items });
}

function catchmentHandler(sp, res) {
  const gmina = sp.get("gmina") ?? "";
  const streetRaw = sp.get("street") ?? "";
  const numberRaw = sp.get("number");
  if (!GMINAS.some((g) => g.teryt === gmina)) return fail(res, 404, "not_found", "Nie ma takiej gminy w katalogu.");
  if (streetRaw.trim() === "") return fail(res, 400, "bad_request", "Podaj nazwę ulicy.");

  const street = fold(streetRaw.trim()).replace(/^ul\.?\s*/, "");
  const number = numberRaw ? parseInt(numberRaw, 10) : NaN;
  const coverage = COVERAGE[gmina];

  let match = "no_data";
  const schools = [];
  if (coverage.state !== "none") {
    // Same rule as RowCovers in CatchmentEndpoints.cs: a row with a range needs a number inside it.
    const streetRows = STREETS.filter((r) => r.gmina === gmina && r.street === street);
    const rows = streetRows.filter(
      (r) => r.from === null || (!Number.isNaN(number) && number >= r.from && number <= r.to),
    );
    const seen = new Set();
    for (const r of rows) {
      if (seen.has(r.schoolId)) continue;
      seen.add(r.schoolId);
      const s = SCHOOLS.find((x) => x.id === r.schoolId);
      if (s) schools.push(listItem(s, DEFAULT_EXAM_YEAR, null));
    }
    // Listed street, no covering rule for this number: no_number_match. Unknown street: no_match.
    match = schools.length > 0 ? "street_list_match" : rows.length === 0 && streetRows.length > 0 ? "no_number_match" : "no_match";
  }

  send(res, 200, {
    gminaTeryt: gmina,
    coverage,
    match,
    schools,
    disclaimer:
      "Dane demonstracyjne: obwód z listy ulic syntetycznej gminy. Nie jest to informacja o rzeczywistym obwodzie szkolnym.",
  });
}

// Same checks, same order and same messages as CorrectionRequestValidator.Validate. Returns { value } or { error }.
function validateCorrection(body) {
  const slug = typeof body.slug === "string" ? body.slug.trim() : "";
  if (slug === "") return { error: "slug is required." };

  const rawKind = typeof body.kind === "string" ? body.kind.trim().toLowerCase() : "";
  const kind = rawKind === "" ? "school" : rawKind;
  if (kind !== "school" && kind !== "preschool") return { error: "kind is not one of the allowed values." };

  const field = typeof body.field === "string" ? body.field.trim().toLowerCase() : null;
  const allowed = kind === "preschool" ? CORRECTION_PRESCHOOL_FIELDS : CORRECTION_FIELDS;
  if (field === null || !allowed.has(field)) return { error: "field is not one of the allowed values." };

  if (typeof body.message !== "string") return { error: "message is required." };
  const rangeError = `message must be between ${CORRECTION_MIN_MESSAGE} and ${CORRECTION_MAX_MESSAGE} characters.`;
  if (body.message.length > CORRECTION_MAX_RAW_MESSAGE) return { error: rangeError };
  // NormalizeMessage: drop control characters except tab, CR and LF, then trim.
  const message = [...body.message].filter((c) => !/\p{Cc}/u.test(c) || c === "\t" || c === "\n" || c === "\r").join("").trim();
  if (message.length < CORRECTION_MIN_MESSAGE || message.length > CORRECTION_MAX_MESSAGE) return { error: rangeError };

  let contactEmail = null;
  if (typeof body.contactEmail === "string" && body.contactEmail.trim() !== "") {
    contactEmail = body.contactEmail.trim();
    if (contactEmail.length > CORRECTION_MAX_EMAIL || !EMAIL_SHAPE.test(contactEmail)) {
      return { error: "contactEmail is not a valid address." };
    }
  }
  return { value: { slug, kind, field, message, contactEmail } };
}

async function catalogPost(req, res, p) {
  if (p === "/api/v1/corrections") {
    let body;
    try {
      body = JSON.parse(await readBody(req));
    } catch {
      return fail(res, 400, "validation_failed", "Nieprawidłowy JSON.");
    }
    const validation = validateCorrection(body && typeof body === "object" ? body : {});
    if (!validation.value) return fail(res, 400, "validation_failed", validation.error);
    const { slug, kind, field } = validation.value;

    // Unknown target gets 404, as CorrectionEndpoints.TargetNotFound does.
    if (kind === "preschool") {
      if (!PRESCHOOLS.some((x) => x.slug === slug)) return fail(res, 404, "not_found", "Preschool not found.");
    } else if (!findSchool(slug)) {
      return fail(res, 404, "not_found", "School not found.");
    }

    const token = randomBytes(16).toString("base64url");
    corrections.set(token, {
      status: "new",
      statusReason: null,
      schoolSlug: slug,
      field,
      kind,
      createdAt: new Date().toISOString(),
    });
    res.setHeader("location", `/api/v1/corrections/${token}`);
    return send(res, 201, { token, status: "new" });
  }
  if (/^\/api\/v1\/schools\/[^/]+\/explain$/.test(p) || p === "/api/v1/ai/search-intent" || p === "/api/v1/ai/methodology-answer") {
    await readBody(req).catch(() => undefined);
    return fail(res, 503, "ai_unavailable", "Funkcja AI jest teraz niedostępna (brak klucza w środowisku demonstracyjnym).");
  }
  return fail(res, 404, "not_found", "Nie znaleziono.");
}

async function catalogHandler(req, res, url) {
  const p = url.pathname;
  if (p === "/alive") {
    return send(res, 200, { status: "ok", service: "mock-catalog", data: DEMO_SOURCE });
  }
  if (req.method === "POST") return catalogPost(req, res, p);
  if (req.method !== "GET") return fail(res, 405, "method_not_allowed", "Metoda niedozwolona.");

  const sp = url.searchParams;
  if (p === "/api/v1/meta") {
    return send(res, 200, { service: "mock-catalog", methodologyVersion: METHODOLOGY_VERSION, source: DEMO_SOURCE });
  }
  if (p === "/api/v1/gminas") {
    return send(
      res,
      200,
      GMINAS.map((g) => ({
        teryt: g.teryt,
        name: g.name,
        schoolCount: SCHOOLS.filter((s) => s.teryt === g.teryt).length,
        coverageState: g.coverage,
      })),
    );
  }
  if (p === "/api/v1/schools") return listSchools(sp, res);

  let m = p.match(/^\/api\/v1\/schools\/by-id\/(\d+)$/);
  if (m) {
    const s = SCHOOLS.find((x) => x.id === Number(m[1]));
    return s ? send(res, 200, schoolProfile(s)) : fail(res, 404, "not_found", "Nie znaleziono szkoły.");
  }

  m = p.match(/^\/api\/v1\/schools\/([^/]+)$/);
  if (m) {
    const slug = decodeSegment(m[1]);
    const s = isSlug(slug) ? findSchool(slug) : null;
    return s ? send(res, 200, schoolProfile(s)) : fail(res, 404, "not_found", "Nie znaleziono szkoły.");
  }

  if (p === "/api/v1/ranking") return rankingHandler(sp, res);
  if (p === "/api/v1/compare") return compareHandler(sp, res);
  if (p === "/api/v1/methodology") {
    return send(res, 200, {
      version: METHODOLOGY_VERSION,
      publicationThreshold: PUBLICATION_THRESHOLD,
      coverageFloor: COVERAGE_FLOOR,
      weights: "równe wagi trzech przedmiotów: język polski, matematyka, język obcy",
      intervalZ: INTERVAL_Z,
      description:
        "Wynik to średnia wyników trzech przedmiotów w danym roku, pomniejszona, gdy pokrycie kohorty jest niskie. Wynik publikujemy od 9 zdających. Przedział niepewności liczony jest z liczebności kohorty. Dane demonstracyjne.",
      docsUrl: "https://example.invalid/metodologia-1.0.0",
    });
  }
  if (p === "/api/v1/catchment") return catchmentHandler(sp, res);

  m = p.match(/^\/api\/v1\/corrections\/([^/]+)$/);
  if (m) {
    const token = decodeSegment(m[1]);
    const rec = corrections.get(token);
    if (!rec) return fail(res, 404, "not_found", "Nie znaleziono zgłoszenia.");
    // CorrectionStatusResponse: no message, no contact email, no internal id.
    return send(res, 200, {
      token,
      status: rec.status,
      statusReason: rec.statusReason,
      createdAt: rec.createdAt,
      schoolSlug: rec.schoolSlug,
      field: rec.field,
      kind: rec.kind,
    });
  }

  if (p === "/api/v1/map/markers") return mapMarkersHandler(sp, res);

  if (p === "/api/v1/preschools") {
    const gmina = sp.get("gmina");
    const q = sp.get("q");
    const lat = numParam(sp.get("lat"));
    const lon = numParam(sp.get("lon"));
    const radiusKm = numParam(sp.get("radiusKm"));
    const publicOnly = sp.get("publicOnly") === "true";
    const hasFree = sp.get("hasFreePlaces") === "true";
    const maxTuition = numParam(sp.get("maxTuition"));
    const page = intParam(sp.get("page"), 1, 1, 100000);
    const pageSize = intParam(sp.get("pageSize"), 20, 1, 100);

    let rows = PRESCHOOLS.map((x) => ({
      p: x,
      dist: lat !== null && lon !== null ? round2(haversineKm(lat, lon, x.latitude, x.longitude)) : null,
    }));
    if (gmina) rows = gmina === "9999999" ? rows : [];
    if (q) rows = rows.filter((r) => fold(r.p.name).includes(fold(q)));
    if (publicOnly) rows = rows.filter((r) => r.p.isPublic);
    if (hasFree) rows = rows.filter((r) => r.p.facts && r.p.facts.free !== null && r.p.facts.free > 0);
    if (maxTuition !== null) {
      rows = rows.filter((r) => {
        const min = r.p.facts ? (r.p.facts.tuitionMin ?? r.p.facts.tuitionMax) : null;
        return min !== null && min <= maxTuition;
      });
    }
    if (radiusKm !== null) rows = rows.filter((r) => r.dist !== null && r.dist <= radiusKm);
    if (lat !== null && lon !== null) rows.sort((a, b) => a.dist - b.dist);
    else rows.sort((a, b) => a.p.name.localeCompare(b.p.name, "pl"));

    const total = rows.length;
    const items = rows.slice((page - 1) * pageSize, page * pageSize).map((r) => preschoolListItem(r.p, r.dist));
    return send(res, 200, { items, total, page, pageSize });
  }

  m = p.match(/^\/api\/v1\/preschools\/([^/]+)$/);
  if (m) {
    const slug = decodeSegment(m[1]);
    const found = isSlug(slug) ? PRESCHOOLS.find((x) => x.slug === slug) : null;
    return found ? send(res, 200, preschoolProfile(found)) : fail(res, 404, "not_found", "Nie znaleziono przedszkola.");
  }

  return fail(res, 404, "not_found", "Nie znaleziono.");
}

// ---------------------------------------------------------------------------
// Satellite listeners: reviews, billing, agent
// ---------------------------------------------------------------------------

// Mirrors DsaValidation.ValidateNotice and DsaEndpoints.SubmitNotice. Error text is the real service's text.
function cleanText(raw) {
  if (typeof raw !== "string") return null;
  const text = [...raw].filter((c) => !/\p{Cc}/u.test(c) || c === "\t" || c === "\n" || c === "\r").join("").trim();
  return text === "" ? null : text;
}

function validateNotice(body) {
  const reference = cleanText(body.contentReference);
  const reviewId = body.reviewId ?? null;
  if (reference === null && reviewId === null) return { error: "contentReference or reviewId is required." };
  if (reference !== null && reference.length > 500) return { error: "contentReference must be at most 500 characters." };

  const explanation = cleanText(body.explanation);
  if (explanation === null || explanation.length < 20 || explanation.length > 3000) {
    return { error: "explanation must be between 20 and 3000 characters." };
  }
  const category = typeof body.category === "string" ? body.category.trim() : null;
  if (category === null || !NOTICE_CATEGORIES.has(category)) return { error: "category is not one of the allowed values." };
  if (body.goodFaithConfirmed !== true) return { error: "goodFaithConfirmed must be true." };

  const name = cleanText(body.submitterName);
  if (name !== null && name.length > 120) return { error: "submitterName must be at most 120 characters." };
  const email = cleanText(body.submitterEmail);
  if (email !== null && (email.length > 200 || !EMAIL_SHAPE.test(email))) {
    return { error: "submitterEmail is not a valid address." };
  }
  return {
    value: {
      reviewId,
      contentReference: reference ?? `review:${reviewId}`,
      explanation,
      category,
      submitterName: name,
      submitterEmail: email,
    },
  };
}

async function noticePost(req, res) {
  let body;
  try {
    body = JSON.parse(await readBody(req));
  } catch {
    return fail(res, 400, "invalid_json", "Request body is not valid JSON.");
  }
  if (body === null || typeof body !== "object") {
    return fail(res, 400, "validation_failed", "Request body is required.");
  }
  const validation = validateNotice(body);
  if (!validation.value) return fail(res, 400, "validation_failed", validation.error);
  if (validation.value.reviewId !== null) return fail(res, 404, "not_found", "Review not found.");

  const id = randomUUID();
  const ackToken = randomBytes(16).toString("base64url");
  const receivedAt = new Date().toISOString();
  notices.set(id, { ackToken, category: validation.value.category, contentReference: validation.value.contentReference, receivedAt });
  res.setHeader("location", `/api/v1/notices/${id}`);
  // NoticeReceipt: the acknowledgement token is shown once and is the only handle to the notice.
  return send(res, 201, { id, ackToken, receivedAt });
}

function noticeGet(res, id, token) {
  const rec = UUID_SHAPE.test(id) ? notices.get(id) : undefined;
  // Unknown ids and wrong tokens get the same 404 (DsaEndpoints.GetNotice).
  if (!rec || !token || token !== rec.ackToken) return fail(res, 404, "not_found", "Notice not found.");
  return send(res, 200, {
    id,
    status: "received",
    category: rec.category,
    contentReference: rec.contentReference,
    reviewId: null,
    receivedAt: rec.receivedAt,
    decision: null,
  });
}

async function reviewsHandler(req, res, url) {
  const p = url.pathname;
  if (p === "/alive") return send(res, 200, { status: "ok", service: "mock-reviews", data: DEMO_SOURCE });
  if (req.method !== "GET" && req.method !== "POST") return fail(res, 405, "method_not_allowed", "Metoda niedozwolona.");
  if (p === "/api/v1/notices" && req.method === "POST") return noticePost(req, res);
  let nm = p.match(/^\/api\/v1\/notices\/([^/]+)$/);
  if (nm && req.method === "GET") return noticeGet(res, decodeSegment(nm[1]), url.searchParams.get("token"));
  if (p === "/api/v1/legal/terms") return send(res, 200, { version: "2026-01-01" });
  if (p === "/api/v1/legal/contact") {
    // The real service answers 503 contact_not_configured until its contact details are set.
    return fail(res, 503, "contact_not_configured", "Dane kontaktowe nie są jeszcze skonfigurowane.");
  }
  if (p === "/api/v1/reviews/mine") return fail(res, 401, "unauthorized", "Wymagane zalogowanie.");
  let m = p.match(/^\/api\/v1\/schools\/([^/]+)\/reviews$/);
  if (m) {
    const slug = decodeSegment(m[1]);
    if (!isSlug(slug)) return fail(res, 404, "not_found", "Nie znaleziono szkoły.");
    if (req.method === "POST") return fail(res, 401, "unauthorized", "Wymagane zalogowanie.");
    return send(res, 200, {
      userGenerated: true,
      notice: REVIEWS_NOTICE,
      items: [],
      total: 0,
      page: intParam(url.searchParams.get("page"), 1, 1, 100000),
      pageSize: intParam(url.searchParams.get("pageSize"), 20, 1, 100),
    });
  }
  return fail(res, 404, "not_found", "Nie znaleziono.");
}

async function billingHandler(req, res, url) {
  const p = url.pathname;
  if (p === "/alive") return send(res, 200, { status: "ok", service: "mock-billing", data: DEMO_SOURCE });
  if (req.method !== "GET" && req.method !== "POST") return fail(res, 405, "method_not_allowed", "Metoda niedozwolona.");
  if (p === "/api/v1/plans") {
    // Empty list: the public page shows "Cennik w przygotowaniu." (no plans configured yet).
    return send(res, 200, { plans: [], note: "Cennik w przygotowaniu.", mode: "mock" });
  }
  if (p === "/api/v1/legal/enquiry-consent") {
    return send(res, 200, {
      version: "projekt-2026-01",
      text: "PROJEKT (dane demonstracyjne): wyrażam zgodę na przekazanie moich danych kontaktowych szkole w celu odpowiedzi na zapytanie.",
    });
  }
  const m = p.match(/^\/api\/v1\/schools\/([^/]+)\/premium$/);
  if (m) {
    const slug = decodeSegment(m[1]);
    if (!isSlug(slug)) return fail(res, 404, "not_found", "Nie znaleziono szkoły.");
    return send(res, 200, {
      sponsored: true,
      tier: "none",
      editorsVerified: false,
      profile: null,
      label: "Treść sponsorowana",
      enquiriesOpen: false,
    });
  }
  return fail(res, 404, "not_found", "Nie znaleziono.");
}

async function agentHandler(req, res, url) {
  if (url.pathname === "/alive") return send(res, 200, { status: "ok", service: "mock-agent", data: DEMO_SOURCE });
  if (req.method === "POST" && url.pathname === "/agent/chat") {
    await readBody(req).catch(() => undefined);
    return send(res, 503, {
      error: { code: "ai_unavailable", message: "Doradca AI jest wyłączony albo nie ma skonfigurowanego klucza." },
    });
  }
  return fail(res, 404, "not_found", "Nie znaleziono.");
}

// ---------------------------------------------------------------------------
// Server bootstrap
// ---------------------------------------------------------------------------

const LOG = process.env.MOCK_LOG === "1";

function listen(name, port, handler) {
  const server = createServer(async (req, res) => {
    const url = new URL(req.url ?? "/", "http://localhost");
    if (LOG) process.stderr.write(`[${name}] ${req.method} ${url.pathname}${url.search}\n`);
    try {
      await handler(req, res, url);
    } catch (err) {
      if (LOG) process.stderr.write(`[${name}] error: ${err instanceof Error ? err.message : String(err)}\n`);
      if (!res.headersSent) fail(res, 500, "internal", "Błąd makiety.");
    }
  });
  return new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(port, () => resolve(server));
  });
}

export async function startMock({
  catalogPort = Number(process.env.MOCK_PORT ?? 8080),
  reviewsPort = Number(process.env.MOCK_REVIEWS_PORT ?? 8082),
  billingPort = Number(process.env.MOCK_BILLING_PORT ?? 8083),
  agentPort = Number(process.env.MOCK_AGENT_PORT ?? 8084),
} = {}) {
  const servers = [
    await listen("catalog", catalogPort, catalogHandler),
    await listen("reviews", reviewsPort, reviewsHandler),
    await listen("billing", billingPort, billingHandler),
    await listen("agent", agentPort, agentHandler),
  ];
  return {
    servers,
    close: () => Promise.all(servers.map((s) => new Promise((resolve) => s.close(() => resolve())))),
    ports: { catalogPort, reviewsPort, billingPort, agentPort },
  };
}

const isMain = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const mock = await startMock();
  const { catalogPort, reviewsPort, billingPort, agentPort } = mock.ports;
  process.stdout.write(
    `mock-api listening: catalog :${catalogPort}, reviews :${reviewsPort}, billing :${billingPort}, agent :${agentPort} (${DEMO_SOURCE})\n`,
  );
  const shutdown = () => {
    mock.close().then(() => process.exit(0));
  };
  process.on("SIGTERM", shutdown);
  process.on("SIGINT", shutdown);
}
