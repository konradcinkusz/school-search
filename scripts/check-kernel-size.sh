#!/usr/bin/env bash
# Kernel ceiling (P2): the shared kernel may not exceed MAX_LINES lines of hand-written C#.
# Counts *.cs under src/SchoolSearch.ServiceDefaults, excluding build output (bin/, obj/).
# Exit 0 when within the ceiling, 1 when above it, 2 when the kernel directory is missing.
set -euo pipefail

MAX_LINES=800

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KERNEL_DIR="${ROOT}/src/SchoolSearch.ServiceDefaults"

if [[ ! -d "${KERNEL_DIR}" ]]; then
  echo "check-kernel-size: kernel directory not found: ${KERNEL_DIR}" >&2
  exit 2
fi

total=0
while IFS= read -r -d '' file; do
  lines=$(wc -l < "${file}")
  total=$((total + lines))
done < <(find "${KERNEL_DIR}" -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0)

echo "check-kernel-size: ServiceDefaults holds ${total} lines of .cs (ceiling ${MAX_LINES})"

if (( total > MAX_LINES )); then
  echo "check-kernel-size: FAILED. The kernel is a kernel, not a shared domain (P2). Move the code to the owning service or to Contracts." >&2
  exit 1
fi

echo "check-kernel-size: OK"
