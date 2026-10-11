import Link from "next/link";

export interface PaginationProps {
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
  readonly basePath: string;
  /** Other query parameters to keep (for example q or rok). */
  readonly params: Readonly<Record<string, string | undefined>>;
  readonly pageParam?: string;
}

function hrefFor(basePath: string, params: PaginationProps["params"], pageParam: string, page: number): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, value);
  }
  search.set(pageParam, String(page));
  return `${basePath}?${search.toString()}`;
}

export function Pagination({ page, pageSize, total, basePath, params, pageParam = "page" }: PaginationProps) {
  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  if (pages <= 1) return null;
  const hasPrev = page > 1;
  const hasNext = page < pages;
  return (
    <nav aria-label="Stronicowanie" className="pagination">
      <p>
        Strona {page} z {pages}
      </p>
      <ul>
        <li>
          {hasPrev ? (
            <Link href={hrefFor(basePath, params, pageParam, page - 1)} rel="prev">
              Poprzednia
            </Link>
          ) : (
            <span className="muted">Poprzednia</span>
          )}
        </li>
        <li>
          {hasNext ? (
            <Link href={hrefFor(basePath, params, pageParam, page + 1)} rel="next">
              Następna
            </Link>
          ) : (
            <span className="muted">Następna</span>
          )}
        </li>
      </ul>
    </nav>
  );
}
