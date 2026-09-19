import path from "node:path";
import type { NextConfig } from "next";

const config: NextConfig = {
  // Every page is prerendered and nothing is read at request time, so the site ships
  // as plain static files: no Next.js runtime, no serverless functions, and it can be
  // hosted anywhere that serves a directory.
  output: "export",

  // This app lives inside the edgepad monorepo and reads ../protocol/*.txt during the
  // build. Naming the repository root stops Next inferring a different workspace root
  // and warning about it.
  outputFileTracingRoot: path.resolve(process.cwd(), ".."),
};

export default config;
