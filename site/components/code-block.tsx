import { CopyButton } from "@/components/copy-button";

/**
 * A command or a file listing. `title` names what the block is, which is also what the
 * copy button announces, so a screen reader hears "Copy the quality gate" rather than
 * "Copy code".
 */
export function CodeBlock({
  title,
  code,
  caption,
}: {
  title: string;
  code: string;
  caption?: string;
}) {
  return (
    <figure className="bg-card shadow-card overflow-hidden rounded-[var(--radius-card)]">
      <figcaption className="border-line flex items-center justify-between gap-3 border-b py-1.5 pr-1.5 pl-4">
        <span className="truncate text-[0.8125rem] font-bold">{title}</span>
        <CopyButton text={code} label={title} />
      </figcaption>
      <pre className="overflow-x-auto p-4 font-mono text-[0.8125rem] leading-relaxed">
        <code>{code}</code>
      </pre>
      {caption ? (
        <p className="border-line text-dim border-t px-4 py-3 text-sm leading-relaxed">
          {caption}
        </p>
      ) : null}
    </figure>
  );
}
