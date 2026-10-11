# syntax=docker/dockerfile:1
# Build context: repository root (declared as context "." in flyio/web.fly.toml).
#   docker build -f web/app/Dockerfile .

# ---- deps: install the pnpm workspace from its lockfile only (cache-friendly) ----
FROM node:22-alpine AS deps
RUN npm install -g pnpm@10.28.0
WORKDIR /repo/web
COPY web/package.json web/pnpm-workspace.yaml web/pnpm-lock.yaml ./
COPY web/app/package.json ./app/
RUN pnpm install --frozen-lockfile

# ---- builder: compile Next.js to standalone output ----
FROM node:22-alpine AS builder
RUN npm install -g pnpm@10.28.0
WORKDIR /repo/web
COPY --from=deps /repo/web/node_modules ./node_modules
COPY --from=deps /repo/web/app/node_modules ./app/node_modules
# Explicit file list: never copy a host checkout's node_modules or .next into the image.
COPY web/package.json web/pnpm-workspace.yaml ./
COPY web/app/package.json web/app/tsconfig.json web/app/next.config.ts web/app/next-env.d.ts ./app/
COPY web/app/src ./app/src
ENV NEXT_TELEMETRY_DISABLED=1
RUN pnpm -C app build

# ---- runner: minimal, non-root, listens on 3000 (Fly internal_port) ----
FROM node:22-alpine AS runner
WORKDIR /srv
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
RUN addgroup --system --gid 1001 nodejs \
 && adduser --system --uid 1001 nextjs
# Standalone output is traced from web/ (outputFileTracingRoot), so server.js lives at app/server.js.
COPY --from=builder --chown=nextjs:nodejs /repo/web/app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /repo/web/app/.next/static ./app/.next/static
USER nextjs
EXPOSE 3000
LABEL org.opencontainers.image.source="https://github.com/konradcinkusz/school-search"
CMD ["node", "app/server.js"]
