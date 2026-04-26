#!/usr/bin/env bash
#
# floQ — Deploy auf floq.at (Hetzner, root@46.62.224.113)
#
# Workflow:
#   1) Pre-flight Checks (Branch, Working Tree, unpushed Commits)
#   2) git push origin main
#   3) Auf Server: git pull + docker compose rebuild
#   4) Smoke-Test gegen https://floq.at
#
# Aufruf aus Rider:
#   Run > External Tools > floQ Deploy  (Program: bash, Arguments: scripts/deploy.sh, Working Dir: $ProjectFileDir$)
# oder direkt im Terminal:
#   ./scripts/deploy.sh
#
set -euo pipefail

# --- Konfiguration ---
SERVER_HOST="root@46.62.224.113"
SSH_KEY="${HOME}/.ssh/id_ed25519"
SERVER_PATH="/opt/floq"
COMPOSE_FILE="compose.production.yaml"
DOMAIN="https://floq.at"
EXPECTED_BRANCH="main"

# --- Farben ---
BOLD=$'\033[1m'; DIM=$'\033[2m'; RED=$'\033[31m'; GREEN=$'\033[32m'
YELLOW=$'\033[33m'; CYAN=$'\033[36m'; RESET=$'\033[0m'

step()  { echo "${BOLD}${CYAN}▶ $*${RESET}"; }
ok()    { echo "${GREEN}✓ $*${RESET}"; }
warn()  { echo "${YELLOW}! $*${RESET}"; }
fail()  { echo "${RED}✗ $*${RESET}" >&2; exit 1; }

# --- ins Repo wechseln (Script kann von ueberall aus aufgerufen werden) ---
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

echo
echo "${BOLD}floQ Deploy${RESET} ${DIM}— ${DOMAIN}${RESET}"
echo

# === 1) Pre-flight ==========================================================
step "Pre-flight"

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [ "${CURRENT_BRANCH}" != "${EXPECTED_BRANCH}" ]; then
  fail "Falscher Branch: ${CURRENT_BRANCH} (erwartet: ${EXPECTED_BRANCH})"
fi
ok "Branch: ${CURRENT_BRANCH}"

if [ -n "$(git status --porcelain)" ]; then
  warn "Working Tree hat uncommittete Aenderungen:"
  git status --short | sed 's/^/    /'
  fail "Erst committen (oder stashen), dann erneut deployen."
fi
ok "Working Tree clean"

git fetch --quiet origin "${EXPECTED_BRANCH}"
LOCAL_HEAD="$(git rev-parse HEAD)"
REMOTE_HEAD="$(git rev-parse "origin/${EXPECTED_BRANCH}")"
AHEAD="$(git rev-list --count "origin/${EXPECTED_BRANCH}..HEAD")"
BEHIND="$(git rev-list --count "HEAD..origin/${EXPECTED_BRANCH}")"

if [ "${BEHIND}" -gt 0 ]; then
  fail "Lokal ${BEHIND} Commits hinter origin/${EXPECTED_BRANCH} — erst pullen."
fi
if [ "${AHEAD}" -gt 0 ]; then
  ok "${AHEAD} Commit(s) zu pushen"
else
  ok "Lokal up-to-date mit origin"
fi

echo

# === 2) Push ================================================================
if [ "${AHEAD}" -gt 0 ]; then
  step "Push nach origin/${EXPECTED_BRANCH}"
  git push origin "${EXPECTED_BRANCH}"
  ok "Push erfolgreich"
  echo
fi

# === 3) Deploy auf Server ===================================================
step "Server: Pull + Rebuild"

ssh -i "${SSH_KEY}" -o StrictHostKeyChecking=accept-new "${SERVER_HOST}" \
  "set -e; cd '${SERVER_PATH}'; git pull --ff-only; docker compose -f '${COMPOSE_FILE}' up -d --build" \
  | sed 's/^/    /'

ok "Server-Rebuild durch"
echo

# === 4) Smoke-Test ==========================================================
step "Smoke-Test ${DOMAIN}"

# kurz warten, damit der neue Container hochfahren kann
sleep 3

HTTP_CODE="$(curl -sS -o /tmp/floq_smoke.html -w '%{http_code}' "${DOMAIN}/")" || HTTP_CODE="000"
if [ "${HTTP_CODE}" != "200" ]; then
  warn "HTTP ${HTTP_CODE} — Container-Logs:"
  ssh -i "${SSH_KEY}" "${SERVER_HOST}" "docker logs --tail 30 floq-floq-1" | sed 's/^/    /'
  fail "Deploy unsauber — Smoke-Test fehlgeschlagen."
fi
ok "HTTP 200"

# Sanity: enthält die Seite den aktuellen Claim?
if grep -q "fesch" /tmp/floq_smoke.html; then
  ok "Landing-Inhalt OK (Claim gefunden)"
else
  warn "Claim 'fesch' nicht im HTML gefunden — pruefen ob Layout/Inhalt erwartet ist."
fi

# Cert-Restlaufzeit anzeigen (informativ)
NOT_AFTER="$(echo | openssl s_client -servername floq.at -connect floq.at:443 2>/dev/null \
  | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2 || true)"
[ -n "${NOT_AFTER}" ] && echo "    ${DIM}Cert gueltig bis: ${NOT_AFTER}${RESET}"

echo
echo "${BOLD}${GREEN}✓ Deploy fertig.${RESET} ${DIM}${DOMAIN}${RESET}"
echo
