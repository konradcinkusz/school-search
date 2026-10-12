import Link from "next/link";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Pagination } from "@/components/pagination";
import { ScoreBadge, scoreStatusOf } from "@/components/score-badge";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog } from "@/lib/api";
import { formatInteger } from "@/lib/format";
import { parseRankingScope } from "@/lib/zasieg";
import { intParam } from "@/lib/validation";

// Data-driven: rendered per request.
export const dynamic = "force-dynamic";

const PAGE_SIZE = 50;

type Props = {
  params: Promise<{ zasieg: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { zasieg } = await params;
  const scope = parseRankingScope(zasieg);
  if (!scope) return { title: "Ranking" };
  const where = scope.kind === "country" ? "w kraju" : `w gminie (TERYT ${scope.gminaTeryt})`;
  return {
    title: `Ranking szkół ${where}`,
    description: `Ranking szkół podstawowych ${where}, od najwyższego wyniku. Nie jest oficjalnym rankingiem.`,
    alternates: { canonical: `/ranking/${scope.param}` },
  };
}

export default async function RankingPage({ params, searchParams }: Props) {
  const { zasieg } = await params;
  const scope = parseRankingScope(zasieg);
  if (!scope) notFound();

  const sp = await searchParams;
  const page = intParam(sp.page, 1, 100_000);
  const year = intParam(sp.rok, 0, 9999);

  // Descending only, by design: there is no sort or direction parameter on this page or in the API.
  const result = await catalog.ranking({
    scope: scope.kind,
    gmina: scope.kind === "gmina" ? scope.gminaTeryt : undefined,
    year: year > 0 ? year : undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const where = scope.kind === "country" ? "w kraju" : "w gminie";

  return (
    <section>
      <header className="page-header">
        <h1>Ranking szkół podstawowych {where}</h1>
        <p className="lead">
          Kolejność: od najwyższego wyniku. Szkoły bez wystarczającej kohorty nie są w rankingu.{" "}
          <Link href="/metodologia">Metodologia</Link>.
        </p>
      </header>
      <div className="notice notice--caution" role="note">
        Ranking nie jest oficjalnym rankingiem. Nie zastępuje rozmowy z nauczycielami ani wizyty w szkole.
      </div>

      {!result.ok ? (
        result.reason === "not_found" || result.reason === "bad_request" ? (
          <div className="notice" role="alert">
            Nie znaleziono rankingu dla tego zasięgu. {result.message ?? ""}
          </div>
        ) : (
          <ServiceUnavailable what="ranking" />
        )
      ) : (
        <>
          <ul className="meta-row" aria-label="Zakres rankingu">
            <li>
              <span className="badge badge--info">
                Wersja metodologii: <strong>{result.data.methodologyVersion}</strong>
              </span>
            </li>
            {result.data.examYear !== null ? (
              <li>
                <span className="badge">rok egzaminu {result.data.examYear}</span>
              </li>
            ) : null}
          </ul>
          {result.data.items.length === 0 ? (
            <p className="muted">Brak szkół z publikowanym wynikiem w tym zasięgu.</p>
          ) : (
            <div className="table-wrap">
              <table className="table--stack ranking-table">
                <caption className="muted">Ranking, od najwyższego wyniku</caption>
                <thead>
                  <tr>
                    <th scope="col">Miejsce</th>
                    <th scope="col">Szkoła</th>
                    <th scope="col">Wynik</th>
                  </tr>
                </thead>
                <tbody>
                  {result.data.items.map((item) => (
                    <tr key={item.slug}>
                      <td>
                        <span className="rank">{item.rank}</span>
                      </td>
                      <td>
                        <Link href={`/szkoly/${item.slug}`} className="ranking-table__name">
                          {item.name}
                        </Link>
                        <br />
                        <span className="muted">{item.gminaName ?? ""}</span>
                      </td>
                      <td>
                        <ScoreBadge status={scoreStatusOf(item.score)} block={item.score} variant="compact" />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {result.data.unrankedCount > 0 ? (
            <p className="notice" role="note">
              {formatInteger(result.data.unrankedCount)} szkół nie jest w rankingu, bo nie ma dla nich wyniku (zbyt
              mała kohorta). Brak wyniku nie oznacza zera ani oceny.
            </p>
          ) : null}
          <Pagination
            page={result.data.page}
            pageSize={result.data.pageSize}
            total={result.data.total}
            basePath={`/ranking/${scope.param}`}
            params={{ rok: year > 0 ? String(year) : undefined }}
          />
        </>
      )}
    </section>
  );
}
