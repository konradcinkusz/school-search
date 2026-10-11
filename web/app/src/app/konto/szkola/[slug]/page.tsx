import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { OwnerSchoolPanel } from "@/components/billing/owner-school";
import { isSlug } from "@/lib/validation";

// Client-driven owner area. Access is re-checked by the Billing service on every call.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Profil szkoły",
  robots: { index: false, follow: false },
};

export default async function SchoolOwnerPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  if (!isSlug(slug)) notFound();
  return (
    <article>
      <h1>Profil szkoły: {slug}</h1>
      <OwnerSchoolPanel slug={slug} />
    </article>
  );
}
