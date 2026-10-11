"use client";

import { useId, useState, type FormEvent } from "react";
import { billingFeedback } from "@/components/billing/billing-messages";
import { bffRequest } from "@/lib/bff-client";
import { parseDraft } from "@/lib/billing-parse";
import {
  DRAFT_BULLETS_MAX,
  DRAFT_BULLET_MAX,
  PROFILE_DESCRIPTION_MAX,
  PROFILE_HEADLINE_MAX,
  PROFILE_URL_MAX,
  type PremiumProfileView,
} from "@/lib/billing-types";

/**
 * Owner's sponsored profile. The text is plain text (React escaping) and goes through the Billing claim linter on
 * the server. The AI suggestion only fills the form: it is never saved or published until the owner presses save.
 */
export function ProfileEditor({
  slug,
  initial,
  onSaved,
}: {
  readonly slug: string;
  readonly initial: PremiumProfileView | null;
  readonly onSaved: () => void;
}) {
  const ids = { headline: useId(), description: useId(), url: useId(), bullets: useId() };
  const [headline, setHeadline] = useState(initial?.headline ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [websiteUrl, setWebsiteUrl] = useState(initial?.websiteUrl ?? "");
  const [bullets, setBullets] = useState("");
  const [drafting, setDrafting] = useState(false);
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState<{ kind: "error" | "done"; text: string; detail: string | null } | null>(null);

  async function onDraft() {
    const facts = bullets
      .split("\n")
      .map((line) => line.trim())
      .filter((line) => line !== "");
    if (facts.length === 0 || facts.length > DRAFT_BULLETS_MAX || facts.some((fact) => fact.length > DRAFT_BULLET_MAX)) {
      setFeedback({
        kind: "error",
        text: `Podaj od 1 do ${DRAFT_BULLETS_MAX} punktów, każdy do ${DRAFT_BULLET_MAX} znaków.`,
        detail: null,
      });
      return;
    }
    setDrafting(true);
    setFeedback(null);
    const response = await bffRequest(`/api/proxy/api/v1/premium/${encodeURIComponent(slug)}/draft`, {
      method: "POST",
      body: { bullets: facts },
    });
    setDrafting(false);
    const draft = response?.status === 200 ? parseDraft(response.body) : null;
    if (draft === null) {
      const failure = billingFeedback(response, "Nie udało się przygotować propozycji. Napisz tekst samodzielnie.");
      setFeedback({ kind: "error", text: failure.text, detail: failure.detail });
      return;
    }
    setHeadline(draft.headline.slice(0, PROFILE_HEADLINE_MAX));
    setDescription(draft.description.slice(0, PROFILE_DESCRIPTION_MAX));
    setFeedback({
      kind: "done",
      text: "Propozycja została wpisana do formularza. Nie jest zapisana. Sprawdź każdy fakt i zapisz profil, jeśli jest poprawny.",
      detail: null,
    });
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const title = headline.trim();
    const text = description.trim();
    const url = websiteUrl.trim();
    if (title === "" || title.length > PROFILE_HEADLINE_MAX) {
      setFeedback({ kind: "error", text: `Nagłówek jest wymagany (do ${PROFILE_HEADLINE_MAX} znaków).`, detail: null });
      return;
    }
    if (text === "" || text.length > PROFILE_DESCRIPTION_MAX) {
      setFeedback({ kind: "error", text: `Opis jest wymagany (do ${PROFILE_DESCRIPTION_MAX} znaków).`, detail: null });
      return;
    }
    if (url !== "" && (url.length > PROFILE_URL_MAX || !/^https:\/\/[^\s]+$/i.test(url))) {
      setFeedback({ kind: "error", text: "Adres strony musi zaczynać się od https:// (do 300 znaków).", detail: null });
      return;
    }
    setSaving(true);
    setFeedback(null);
    const response = await bffRequest(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/premium`, {
      method: "PUT",
      body: { headline: title, description: text, websiteUrl: url === "" ? null : url },
    });
    setSaving(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      setFeedback({ kind: "done", text: "Profil został zapisany i jest widoczny jako treść sponsorowana.", detail: null });
      onSaved();
      return;
    }
    const failure = billingFeedback(response, "Profilu nie udało się zapisać. Sprawdź treść i spróbuj ponownie.");
    setFeedback({ kind: "error", text: failure.text, detail: failure.detail });
  }

  return (
    <div>
      <h2>Profil rozszerzony</h2>
      <div className="card">
        <p className="muted">
          Tekst jest oznaczony jako treść sponsorowana. Nie pisz o najlepszości, rankingach, numerach pozycji, gwarancjach,
          wynikach egzaminów, certyfikatach ani o tym, że profil jest zweryfikowany. Takie sformułowania są odrzucane.
        </p>

        <details>
          <summary>Zaproponuj tekst (asystent AI)</summary>
          <p className="muted">
            Wpisz fakty o szkole, po jednym w wierszu (do {DRAFT_BULLETS_MAX} punktów). Asystent użyje tylko tych faktów.
            Propozycja nie jest zapisana ani opublikowana.
          </p>
          <div className="field">
            <label htmlFor={ids.bullets}>Fakty o szkole</label>
            <textarea id={ids.bullets} rows={5} value={bullets} onChange={(event) => setBullets(event.target.value)} />
          </div>
          <button type="button" className="button" onClick={onDraft} disabled={drafting}>
            {drafting ? "Przygotowywanie…" : "Zaproponuj tekst"}
          </button>
        </details>

        <form onSubmit={onSubmit} noValidate>
          <div className="field">
            <label htmlFor={ids.headline}>Nagłówek (do {PROFILE_HEADLINE_MAX} znaków)</label>
            <input
              id={ids.headline}
              type="text"
              maxLength={PROFILE_HEADLINE_MAX}
              value={headline}
              onChange={(event) => setHeadline(event.target.value)}
              required
            />
          </div>
          <div className="field">
            <label htmlFor={ids.description}>Opis (do {PROFILE_DESCRIPTION_MAX} znaków)</label>
            <textarea
              id={ids.description}
              rows={8}
              maxLength={PROFILE_DESCRIPTION_MAX}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              required
            />
          </div>
          <div className="field">
            <label htmlFor={ids.url}>Strona szkoły (https, nieobowiązkowo)</label>
            <input
              id={ids.url}
              type="url"
              maxLength={PROFILE_URL_MAX}
              value={websiteUrl}
              onChange={(event) => setWebsiteUrl(event.target.value)}
            />
          </div>
          <button type="submit" className="button" disabled={saving}>
            {saving ? "Zapisywanie…" : "Zapisz profil"}
          </button>
        </form>

        {feedback !== null ? (
          <div role={feedback.kind === "error" ? "alert" : "status"} className="notice">
            <p>{feedback.text}</p>
            {feedback.detail !== null ? <p className="muted">{feedback.detail}</p> : null}
          </div>
        ) : null}
      </div>
    </div>
  );
}
