/**
 * One visual style for every "what we do not sell or do" list (dla-szkol, cennik, metodologia).
 * The heading stays on the page so each page keeps its own heading level.
 */
export function NotSoldList({ items }: { items: readonly string[] }) {
  return (
    <ul className="checklist checklist--no">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  );
}
