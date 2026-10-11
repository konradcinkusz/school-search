import Link from "next/link";
import { Pagination } from "@/components/pagination";
import { ReviewWrite } from "@/components/reviews/review-write";
import { listSchoolReviews } from "@/lib/reviews";
import { REVIEWS_NOTICE_FALLBACK, type ReviewItem } from "@/lib/reviews-types";

/**
 * "Opinie użytkowników": a section of its own. It never feeds the score, the ranking or the AI explanation, and it
 * shows individual ratings only, never an average. Review text is plain text (React escaping).
 */
export const REVIEWS_PAGE_SIZE = 10;

export async function ReviewsSection({ slug, page }: { readonly slug: string; readonly page: number }) {
  const result = await listSchoolReviews(slug, page, REVIEWS_PAGE_SIZE);

  return (
    <section aria-labelledby="reviews-heading" id="opinie" className="reviews">
      <h2 id="reviews-heading">Opinie użytkowników</h2>
      <p className="card reviews__notice" role="note">
        {result.ok ? result.data.notice : REVIEWS_NOTICE_FALLBACK}
      </p>
      <ReviewWrite slug={slug} />

      {!result.ok ? (
        <div className="notice" role="alert">
          <p>
            <strong>Opinie są chwilowo niedostępne.</strong> Nie udało się pobrać opinii dla tej szkoły.
          </p>
        </div>
      ) : result.data.items.length === 0 ? (
        <p className="muted">Nie ma jeszcze opinii dla tej szkoły.</p>
      ) : (
        <>
          <ul className="plain-list reviews__list">
            {result.data.items.map((review) => (
              <ReviewCard key={review.id} review={review} slug={slug} />
            ))}
          </ul>
          <Pagination
            page={result.data.page}
            pageSize={result.data.pageSize}
            total={result.data.total}
            basePath={`/szkoly/${encodeURIComponent(slug)}`}
            params={{}}
            pageParam="opinie"
          />
        </>
      )}
    </section>
  );
}

function ReviewCard({ review, slug }: { readonly review: ReviewItem; readonly slug: string }) {
  return (
    <li className="card review">
      <p className="muted">
        {review.authorLabel} · {review.reviewedMonth} · rok szkolny {review.schoolYear}
      </p>
      <p>
        Ocena osoby piszącej: <strong>{review.rating} z 5</strong>
      </p>
      <h3 className="review__title">{review.title}</h3>
      <p className="review__body">{review.body}</p>
      {review.reply ? (
        <div className="review__reply">
          <p className="muted">
            Odpowiedź: {review.reply.representativeLabel} (
            {review.reply.verificationState === "verified_by_moderator"
              ? "potwierdzone przez moderatora"
              : "niezweryfikowane"}
            )
          </p>
          <p className="review__body">{review.reply.body}</p>
        </div>
      ) : null}
      <p className="muted review__report">
        <Link href={`/zglos-tresc?slug=${encodeURIComponent(slug)}&review=${encodeURIComponent(review.id)}`}>
          Zgłoś treść
        </Link>
      </p>
    </li>
  );
}
