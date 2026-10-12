// Visual collector: visits every public route at each viewport (projects phone, tablet, desktop), takes a full-page
// screenshot and records findings. The test is a collector: it fails only when a page cannot load or answers 5xx.
// Findings go to out/report.json (see README.md).
import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { mkdirSync, readFileSync, readdirSync, renameSync, writeFileSync, existsSync } from "node:fs";
import path from "node:path";

const OUT_DIR = path.join(__dirname, "out");
const RESULTS_DIR = path.join(OUT_DIR, "results");
const REPORT_PATH = path.join(OUT_DIR, "report.json");
const VIEWPORT_ORDER = ["phone", "tablet", "desktop"];

interface Route {
  readonly slug: string;
  readonly path: string;
  readonly note?: string;
}

// Public routes. Demo rows come from the mock (gmina 9999999, schools demo-sp-1..14, preschools 1..5).
const ROUTES: readonly Route[] = [
  { slug: "home", path: "/" },
  { slug: "home-adres", path: "/?gmina=9999999&ulica=Przykladowa&numer=5", note: "catchment result" },
  { slug: "szkola-demo-sp-1", path: "/szkoly/demo-sp-1", note: "published score" },
  { slug: "szkola-demo-sp-3", path: "/szkoly/demo-sp-3", note: "insufficient_cohort" },
  { slug: "szkola-demo-sp-6", path: "/szkoly/demo-sp-6", note: "no_exam_data" },
  { slug: "ranking-country", path: "/ranking/country" },
  { slug: "ranking-gmina-9999999", path: "/ranking/gmina:9999999" },
  { slug: "porownaj", path: "/porownaj?slugs=demo-sp-1,demo-sp-7" },
  { slug: "przedszkola", path: "/przedszkola" },
  { slug: "przedszkole-nr-1", path: "/przedszkola/przedszkole-demonstracyjne-nr-1" },
  { slug: "metodologia", path: "/metodologia" },
  { slug: "dla-szkol", path: "/dla-szkol" },
  { slug: "doradca", path: "/doradca" },
  { slug: "cennik", path: "/cennik" },
  { slug: "kontakt", path: "/kontakt" },
  { slug: "regulamin", path: "/regulamin" },
  { slug: "prywatnosc", path: "/prywatnosc" },
  { slug: "zglos-tresc", path: "/zglos-tresc" },
  { slug: "konto-logowanie", path: "/konto/logowanie" },
  { slug: "konto-rejestracja", path: "/konto/rejestracja" },
  { slug: "zgloszenie-demo", path: "/dla-szkol/zgloszenie/demoTokenVisual0000000001", note: "correction status (mock token)" },
  { slug: "404", path: "/nie-ma-takiej-strony-testowej", note: "not-found page (404 is expected)" },
];

interface Sample {
  readonly selector: string;
  readonly text: string;
  readonly width: number;
  readonly height: number;
  readonly inlineLink: boolean;
}

interface PageAudit {
  readonly title: string;
  readonly overflow: { scrollWidth: number; clientWidth: number; overflows: boolean };
  readonly smallTargets: { count: number; samples: Sample[] };
  readonly imagesWithoutAlt: { count: number; samples: string[] };
  readonly unnamedFields: { count: number; samples: { selector: string; type: string; reason: string }[] };
}

interface PageEntry {
  readonly viewport: string;
  readonly route: string;
  readonly slug: string;
  readonly note: string | null;
  readonly path: string;
  readonly status: number | null;
  readonly loadError: string | null;
  readonly durationMs: number;
  readonly screenshot: string;
  readonly consoleErrors: string[];
  readonly pageErrors: string[];
  readonly failedRequests: string[];
  readonly badResponses: string[];
  readonly audit: PageAudit | null;
}

// Runs inside the page. Must stay self-contained (it is serialised by Playwright).
function auditPage(): PageAudit {
  const MIN_TARGET = 44;
  const clip = (value: string, max = 160) => value.replace(/\s+/g, " ").trim().slice(0, max);

  const isVisible = (el: Element): boolean => {
    const rect = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return rect.width > 0 && rect.height > 0 && cs.visibility !== "hidden" && cs.display !== "none";
  };

  const describe = (el: Element): string => {
    const parts: string[] = [el.tagName.toLowerCase()];
    if (el.id) parts.push(`#${el.id}`);
    const cls = (el.getAttribute("class") ?? "").split(/\s+/).filter(Boolean).slice(0, 2);
    if (cls.length) parts.push(`.${cls.join(".")}`);
    return parts.join("");
  };

  const labelText = (el: Element): string => {
    const text = (el.textContent ?? "").trim();
    return clip(text || el.getAttribute("aria-label") || el.getAttribute("title") || el.getAttribute("value") || "");
  };

  // Overflow: the document is wider than the viewport.
  const root = document.documentElement;
  const overflow = {
    scrollWidth: root.scrollWidth,
    clientWidth: root.clientWidth,
    overflows: root.scrollWidth > root.clientWidth,
  };

  // Interactive elements smaller than 44 x 44 CSS px (visible ones only).
  const interactive = document.querySelectorAll<HTMLElement>(
    'a[href], button, input:not([type="hidden"]), select, textarea, summary, [role="button"], [role="link"], [role="checkbox"], [role="tab"], [tabindex]:not([tabindex="-1"])',
  );
  const smallSamples: Sample[] = [];
  let smallCount = 0;
  interactive.forEach((el) => {
    if (!isVisible(el)) return;
    const rect = el.getBoundingClientRect();
    const width = Math.round(rect.width * 10) / 10;
    const height = Math.round(rect.height * 10) / 10;
    if (width >= MIN_TARGET && height >= MIN_TARGET) return;
    smallCount += 1;
    if (smallSamples.length < 40) {
      smallSamples.push({
        selector: describe(el),
        text: labelText(el),
        width,
        height,
        inlineLink: el.tagName === "A" && getComputedStyle(el).display === "inline",
      });
    }
  });

  // Images without an alt attribute (alt="" is a valid decorative choice and is not reported).
  const imgs = Array.from(document.querySelectorAll<HTMLImageElement>("img:not([alt])")).filter(
    (img) => img.getAttribute("role") !== "presentation" && img.getAttribute("role") !== "none",
  );
  const imageSamples = imgs.slice(0, 20).map((img) => clip(img.getAttribute("src") ?? img.outerHTML, 140));

  // Form fields without an accessible name (aria-labelledby, aria-label, label[for], wrapping label, title).
  const fields = Array.from(
    document.querySelectorAll<HTMLElement>(
      'input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="reset"]):not([type="image"]), select, textarea',
    ),
  );
  const unnamed: { selector: string; type: string; reason: string }[] = [];
  let unnamedCount = 0;
  fields.forEach((field) => {
    if (!isVisible(field)) return;
    const type = field.getAttribute("type") ?? field.tagName.toLowerCase();
    const labelledBy = field.getAttribute("aria-labelledby");
    if (labelledBy) {
      const joined = labelledBy
        .split(/\s+/)
        .map((id) => document.getElementById(id)?.textContent ?? "")
        .join(" ")
        .trim();
      if (joined) return;
    }
    if ((field.getAttribute("aria-label") ?? "").trim()) return;
    if (field.id) {
      const forLabel = Array.from(document.querySelectorAll("label")).find((l) => l.htmlFor === field.id);
      if (forLabel && (forLabel.textContent ?? "").trim()) return;
    }
    const wrapping = field.closest("label");
    if (wrapping && (wrapping.textContent ?? "").trim()) return;
    if ((field.getAttribute("title") ?? "").trim()) return;
    unnamedCount += 1;
    const placeholder = (field.getAttribute("placeholder") ?? "").trim();
    if (unnamed.length < 30) {
      unnamed.push({
        selector: describe(field) + (field.getAttribute("name") ? `[name="${field.getAttribute("name")}"]` : ""),
        type,
        reason: placeholder ? "placeholder only (not a label)" : "no label, aria-label or aria-labelledby",
      });
    }
  });

  return {
    title: clip(document.title, 200),
    overflow,
    smallTargets: { count: smallCount, samples: smallSamples },
    imagesWithoutAlt: { count: imgs.length, samples: imageSamples },
    unnamedFields: { count: unnamedCount, samples: unnamed },
  };
}

function writeAtomic(file: string, content: string): void {
  const tmp = `${file}.${process.pid}.${Math.random().toString(36).slice(2)}.tmp`;
  writeFileSync(tmp, content);
  renameSync(tmp, file);
}

function saveResult(entry: PageEntry): void {
  mkdirSync(RESULTS_DIR, { recursive: true });
  writeAtomic(path.join(RESULTS_DIR, `${entry.viewport}__${entry.slug}.json`), JSON.stringify(entry, null, 2));
}

/** Rebuilds out/report.json from every result written so far (tests run in several workers). */
function rebuildReport(): void {
  if (!existsSync(RESULTS_DIR)) return;
  const entries: PageEntry[] = [];
  for (const file of readdirSync(RESULTS_DIR)) {
    if (!file.endsWith(".json")) continue;
    try {
      entries.push(JSON.parse(readFileSync(path.join(RESULTS_DIR, file), "utf8")) as PageEntry);
    } catch {
      // A file mid-write from another worker: the next rebuild picks it up.
    }
  }
  entries.sort((a, b) => {
    const va = VIEWPORT_ORDER.indexOf(a.viewport);
    const vb = VIEWPORT_ORDER.indexOf(b.viewport);
    return va - vb || ROUTES.findIndex((r) => r.slug === a.slug) - ROUTES.findIndex((r) => r.slug === b.slug);
  });

  const audits = entries.map((e) => e.audit).filter((a): a is PageAudit => a !== null);
  const summary = {
    pages: entries.length,
    loadFailures: entries.filter((e) => e.loadError !== null).length,
    serverErrors: entries.filter((e) => e.status !== null && e.status >= 500).length,
    pagesWithConsoleErrors: entries.filter((e) => e.consoleErrors.length > 0).length,
    pagesWithPageErrors: entries.filter((e) => e.pageErrors.length > 0).length,
    pagesWithBadResponses: entries.filter((e) => e.badResponses.length > 0).length,
    pagesWithOverflow: audits.filter((a) => a.overflow.overflows).length,
    pagesWithSmallTargets: audits.filter((a) => a.smallTargets.count > 0).length,
    smallTargetsTotal: audits.reduce((sum, a) => sum + a.smallTargets.count, 0),
    pagesWithImagesWithoutAlt: audits.filter((a) => a.imagesWithoutAlt.count > 0).length,
    imagesWithoutAltTotal: audits.reduce((sum, a) => sum + a.imagesWithoutAlt.count, 0),
    pagesWithUnnamedFields: audits.filter((a) => a.unnamedFields.count > 0).length,
    unnamedFieldsTotal: audits.reduce((sum, a) => sum + a.unnamedFields.count, 0),
  };

  const report = {
    generatedAt: new Date().toISOString(),
    baseURL: process.env.VISUAL_BASE_URL ?? "http://localhost:3000",
    note: "Collector output. Findings are not test failures; only load failures and 5xx responses fail the run.",
    summary,
    pages: entries,
  };
  mkdirSync(OUT_DIR, { recursive: true });
  writeAtomic(REPORT_PATH, JSON.stringify(report, null, 2));
}

const clip = (value: string, max = 300) => (value.length > max ? `${value.slice(0, max)}...` : value);

for (const route of ROUTES) {
  test(`${route.slug}`, async ({ page }: { page: Page }, testInfo: TestInfo) => {
    const viewport = testInfo.project.name;
    const screenshotRel = path.join(viewport, `${route.slug}.png`);
    const screenshotAbs = path.join(OUT_DIR, screenshotRel);
    mkdirSync(path.dirname(screenshotAbs), { recursive: true });

    const consoleErrors: string[] = [];
    const pageErrors: string[] = [];
    const failedRequests: string[] = [];
    const badResponses: string[] = [];
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(clip(msg.text()));
    });
    page.on("pageerror", (err) => pageErrors.push(clip(err.message)));
    page.on("response", (res) => {
      // Browser-side answers of 400 and up, with the URL, so a console "Failed to load resource" can be traced.
      if (res.status() >= 400) badResponses.push(clip(`${res.status()} ${res.request().method()} ${res.url()}`));
    });
    page.on("requestfailed", (req) => {
      // Next prefetches (?_rsc=...) that the navigation cancels are noise, not failures.
      const errorText = req.failure()?.errorText ?? "failed";
      if (errorText === "net::ERR_ABORTED" && req.url().includes("_rsc=")) return;
      failedRequests.push(clip(`${req.method()} ${req.url()} (${errorText})`));
    });

    const started = Date.now();
    let status: number | null = null;
    let loadError: string | null = null;
    let audit: PageAudit | null = null;

    try {
      const response = await page.goto(route.path, { waitUntil: "load", timeout: 60_000 });
      status = response ? response.status() : null;
      await page.waitForLoadState("networkidle", { timeout: 10_000 }).catch(() => undefined);
    } catch (err) {
      loadError = clip(err instanceof Error ? err.message : String(err), 500);
    }

    if (loadError === null) {
      try {
        audit = (await page.evaluate(auditPage)) as PageAudit;
      } catch (err) {
        loadError = clip(`audit failed: ${err instanceof Error ? err.message : String(err)}`, 500);
      }
      try {
        await page.screenshot({ path: screenshotAbs, fullPage: true, animations: "disabled" });
      } catch (err) {
        loadError = loadError ?? clip(`screenshot failed: ${err instanceof Error ? err.message : String(err)}`, 500);
      }
    }

    const entry: PageEntry = {
      viewport,
      route: route.path,
      slug: route.slug,
      note: route.note ?? null,
      path: route.path,
      status,
      loadError,
      durationMs: Date.now() - started,
      screenshot: screenshotRel.split(path.sep).join("/"),
      consoleErrors,
      pageErrors,
      failedRequests,
      badResponses,
      audit,
    };
    saveResult(entry);
    rebuildReport();

    expect(loadError, `page ${route.path} could not load at ${viewport}`).toBeNull();
    expect(status, `page ${route.path} returned no HTTP status`).not.toBeNull();
    expect(status ?? 0, `page ${route.path} answered ${status} at ${viewport}`).toBeLessThan(500);
  });
}
