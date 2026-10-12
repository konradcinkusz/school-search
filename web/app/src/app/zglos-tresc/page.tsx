import type { Metadata } from "next";
import Link from "next/link";
import { NoticeForm } from "@/components/reviews/notice-form";
import { NoticeStatus } from "@/components/reviews/notice-status";
import { isSlug } from "@/lib/validation";

// Notice-and-action intake (DSA art. 16). Reads the optional ?slug= and ?review= prefill only.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Zgłoś treść",
  description: "Zgłoś treść niezgodną z prawem lub regulaminem. Każde zgłoszenie dostaje uzasadnioną decyzję.",
  alternates: { canonical: "/zglos-tresc" },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

export default async function ReportPage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const slugRaw = first(sp.slug);
  const reviewRaw = first(sp.review);
  const slug = slugRaw !== undefined && isSlug(slugRaw) ? slugRaw : undefined;
  const reviewId = reviewRaw !== undefined && UUID_PATTERN.test(reviewRaw) ? reviewRaw : undefined;
  const initialReference = slug !== undefined ? `/szkoly/${slug}${reviewId ? `#opinie` : ""}` : "";

  return (
    <article className="prose">
      <h1>Zgłoś treść</h1>
      <p>
        Zgłoszenie dotyczy treści na tym serwisie, na przykład opinii, które naruszają prawo lub{" "}
        <Link href="/regulamin">regulamin</Link>. Zgłoszenie możesz złożyć bez konta. Każde zgłoszenie rozpatruje
        człowiek, nie automat. Dostaniesz uzasadnienie decyzji (art. 17 DSA).
      </p>
      {reviewId !== undefined ? (
        <p className="muted">Zgłaszasz konkretną opinię. Jej numer zostanie dołączony do zgłoszenia.</p>
      ) : null}
      <h2>Nowe zgłoszenie</h2>
      <div className="card form-card">
        <NoticeForm initialReference={initialReference} reviewId={reviewId} />
      </div>
      <h2>Sprawdź status zgłoszenia</h2>
      <p className="muted">
        Potrzebny jest numer zgłoszenia i token potwierdzenia, które pokazaliśmy po wysłaniu.
      </p>
      <div className="card form-card">
        <NoticeStatus />
      </div>
      <p className="muted">
        Kontakt w sprawach serwisu: <Link href="/kontakt">punkt kontaktowy</Link>.
      </p>
    </article>
  );
}
