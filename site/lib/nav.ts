/**
 * The page's table of contents. One list, used three times: by the sidebar, by the
 * mobile menu, and by the scroll-spy. Every `id` here must exist as a section anchor
 * on the page, which is why the sections take their heading from this list rather
 * than repeating it.
 */
export type NavItem = { id: string; label: string };
export type NavGroup = { label: string; items: NavItem[] };

export const NAV: NavGroup[] = [
  {
    label: "Start here",
    items: [
      { id: "overview", label: "What Edgepad is" },
      { id: "install", label: "Download and install" },
      { id: "using", label: "Using it" },
      { id: "settings", label: "Settings" },
    ],
  },
  {
    label: "How it works",
    items: [
      { id: "split", label: "The split" },
      { id: "transport", label: "Transport" },
      { id: "architecture", label: "Architecture" },
      { id: "threads", label: "Threads and latency" },
      { id: "protocol", label: "Wire protocol" },
      { id: "actions", label: "Actions and controls" },
      { id: "security", label: "Security and trust" },
    ],
  },
  {
    label: "Developing",
    items: [
      { id: "repo", label: "Repository layout" },
      { id: "prerequisites", label: "Prerequisites" },
      { id: "gate", label: "The quality gate" },
      { id: "run-locally", label: "Running it locally" },
      { id: "tests", label: "Tests" },
      { id: "contributing", label: "Contributing" },
      { id: "releases", label: "Releases" },
    ],
  },
  {
    label: "Reference",
    items: [
      { id: "decisions", label: "Design decisions" },
      { id: "limits", label: "Known limits" },
      { id: "history", label: "Version history" },
      { id: "credits", label: "Licence and credits" },
    ],
  },
];

export const ALL_NAV_IDS: string[] = NAV.flatMap((group) =>
  group.items.map((item) => item.id),
);

export const REPO = "https://github.com/akshit-bansal11/edgepad";
export const LATEST_RELEASE = `${REPO}/releases/latest`;
