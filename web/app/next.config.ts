import path from "node:path";
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Trace from the workspace root so standalone output includes the pnpm store layout.
  outputFileTracingRoot: path.resolve(__dirname, ".."),
  reactStrictMode: true,
  poweredByHeader: false,
};

export default nextConfig;
