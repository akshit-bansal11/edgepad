// ESLint's job here is only what Biome cannot do: React hooks, JSX correctness,
// accessibility and the Next.js-specific rules. Biome owns formatting and import
// order, so nothing in this file may reformat code.
//
// eslint-config-next's main entry exports a flat-config array directly
// (`declare const config: Linter.Config[]`), so it is spread, not called. It carries
// eslint-plugin-react, react-hooks, jsx-a11y and @next/eslint-plugin-next with it.
//
// No formatting-disable layer (the eslint-config-prettier equivalent) is needed:
// eslint-config-next ships no stylistic rules, because those left ESLint core.
import next from "eslint-config-next";

const config = [
  {
    ignores: [".next/**", "node_modules/**", "out/**", "next-env.d.ts", "biome.json"],
  },
  ...next,
];

export default config;
