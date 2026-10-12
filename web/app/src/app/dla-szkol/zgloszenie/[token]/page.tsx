import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type CorrectionStatus } from "@/lib/api";
import { formatDatePl } from "@/lib/format";
import { isToken } from "@/lib/validation";
import "../../../styles/static.css";

// Looked up per request; the status must never be cached.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Status zgłoszenia",
  description: "Sprawdź status zgłoszenia korekty danych szkoły.",
  robots: { index: false, follow: false },
};

type Props = { params: Promise<{ token: string }> };

const STATUS_LABEL: Record<CorrectionStatus, string> = {
  new: "nowe",
  in_review: "w weryfikacji",
  accepted: "przyjęte",
  rejected: "odrzucone",
  duplicate: "duplikat",
};

const FIELD_LABEL: Record<string, string> = {
  name: "Nazwa szkoły",
  address: "Adres",
  governing_body: "Organ prowadzący",
  cohort: "Uczniowie i roczniki",
  score: "Wynik lub obliczenia",
  other: "Inne",
};

export default async function CorrectionStatusPage({ params }: Props) {
  const { token } = await params;
  if (!isToken(token)) notFound();

  const result = await catalog.correction(token);
  if (!result.ok) {
    if (result.reason === "not_found") notFound();
    return (
      <article>
        <h1>Status zgłoszenia</h1>
        <ServiceUnavailable what="status zgłoszenia" />
        <p className="actions">
          <Link href={`/dla-szkol/zgloszenie/${token}`}>Odśwież stronę</Link>
          <Link href="/">Wróć do wyszukiwarki</Link>
        </p>
      </article>
    );
  }
  const view = result.data;
  const created = formatDatePl(view.createdAt);
  const statusText = STATUS_LABEL[view.status] ?? view.status;

  return (
    <article>
      <h1>Status zgłoszenia</h1>
      <dl className="facts">
        <dt>Status</dt>
        <dd>{statusText}</dd>
        {view.statusReason ? (
          <>
            <dt>Uzasadnienie</dt>
            <dd>{view.statusReason}</dd>
          </>
        ) : null}
        <dt>Szkoła</dt>
        <dd>{view.schoolSlug}</dd>
        <dt>Dotyczy</dt>
        <dd>{FIELD_LABEL[view.field] ?? view.field}</dd>
        {created ? (
          <>
            <dt>Przyjęto</dt>
            <dd>{created}</dd>
          </>
        ) : null}
      </dl>
      <p className="muted">Treść zgłoszenia nie jest wyświetlana na tej stronie.</p>
    </article>
  );
}
