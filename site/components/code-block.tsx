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
    <figure className="border-line border">
      <figcaption className="border-line flex items-center justify-between gap-3 border-b px-3 py-2">
        <span className="label truncate">{title}</span>
        <CopyButton text={code} label={title} />
      </figcaption>
      <pre className="overflow-x-auto p-3 font-mono text-[0.8125rem] leading-relaxed">
        <code>{code}</code>
      </pre>
      {caption ? (
        <p className="border-line text-dim border-t px-3 py-2 text-sm">{caption}</p>
      ) : null}
    </figure>
  );
}
