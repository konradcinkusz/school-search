import path from "node:path";
import type { NextConfig } from "next";

/**
 * Standard header set (SECURITY-REVIEW §4). The Content-Security-Policy is NOT set here: headers() is evaluated at build
 * time, and the policy depends on a runtime variable (GOOGLE_MAPS_API_KEY adds the Google Maps hosts, ADR-0022). It is
 * assembled per request in src/middleware.ts from src/lib/map-csp.ts.
 */
const nextConfig: NextConfig = {
  output: "standalone",
  // Trace from the workspace root so standalone output includes the pnpm store layout.
  outputFileTracingRoot: path.resolve(__dirname, ".."),
  reactStrictMode: true,
  poweredByHeader: false,
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
        ],
      },
    ];
  },
};

export default nextConfig;
