#!/usr/bin/env bash
# deploy-nexus.sh — Build and push Trino C# NuGet packages to a Nexus repository.
#
# Usage:
#   ./deploy-nexus.sh push     Build Release packages and push to Nexus
#   ./deploy-nexus.sh source   Register Nexus as a local NuGet source (for dotnet restore)
#   ./deploy-nexus.sh list     List packages that would be pushed (dry-run)
#
# Required environment variables for push / source:
#   NEXUS_URL        Base URL of your Nexus instance  e.g. http://nexus.company.com:8081
#   NEXUS_REPO       Hosted repository name            e.g. nuget-hosted
#   NEXUS_USERNAME   Nexus username
#   NEXUS_PASSWORD   Nexus password
#
# Optional:
#   VERSION          Override package version          e.g. 1.2.3.0  (default: today's date yyyy.MM.dd.1)
#   BUILD_CONFIG     Release (default) or Debug
#   SOURCE_NAME      NuGet source alias (default: nexus-trino)
#   SKIP_TESTS       Set to 1 to skip test run before push

set -euo pipefail

# ── Defaults ──────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_DIR="$SCRIPT_DIR/trino-csharp"
SOLUTION_FILE="$SOLUTION_DIR/TrinoDriver.sln"

BUILD_CONFIG="${BUILD_CONFIG:-Release}"
SOURCE_NAME="${SOURCE_NAME:-nexus-trino}"
SKIP_TESTS="${SKIP_TESTS:-0}"

# Packages to publish (project paths relative to SOLUTION_DIR)
PUBLISH_PROJECTS=(
    "Trino.Client/Trino.Client.csproj"
    "Trino.Client.Auth/Trino.Client.Auth.csproj"
    "Trino.Data.ADO/Trino.Data.ADO.csproj"
)

# ── Helpers ───────────────────────────────────────────────────────────────────
info()  { echo "  [INFO]  $*"; }
ok()    { echo "  [ OK ]  $*"; }
err()   { echo "  [ERR]   $*" >&2; }
die()   { err "$*"; exit 1; }

require_env() {
    local missing=()
    for var in "$@"; do
        [[ -z "${!var:-}" ]] && missing+=("$var")
    done
    if [[ ${#missing[@]} -gt 0 ]]; then
        die "Missing required environment variables: ${missing[*]}"
    fi
}

nexus_push_url() {
    echo "${NEXUS_URL%/}/repository/${NEXUS_REPO}/"
}

nexus_group_url() {
    # Convention: group repo has same name but without "-hosted" suffix, or use "-group"
    local group="${NEXUS_REPO/-hosted/}-group"
    echo "${NEXUS_URL%/}/repository/${group}/"
}

# ── Commands ──────────────────────────────────────────────────────────────────

cmd_list() {
    info "Packages that would be pushed (dry-run):"
    echo
    for proj in "${PUBLISH_PROJECTS[@]}"; do
        local proj_path="$SOLUTION_DIR/$proj"
        local pkg_id
        pkg_id=$(grep -o 'PackageId>[^<]*' "$proj_path" 2>/dev/null | cut -d'>' -f2 || true)
        # Fall back to project filename if PackageId is an MSBuild variable or empty
        if [[ -z "$pkg_id" || "$pkg_id" == *'$('* ]]; then
            pkg_id="$(basename "$proj_path" .csproj)"
        fi
        echo "    • $pkg_id  ($proj)"
    done
    echo
    info "Push URL : $(nexus_push_url 2>/dev/null || echo '(set NEXUS_URL + NEXUS_REPO)')"
    info "Version  : ${VERSION:-$(date '+%Y.%-m.%-d').1}"
    info "Config   : $BUILD_CONFIG"
}

cmd_push() {
    require_env NEXUS_URL NEXUS_REPO NEXUS_USERNAME NEXUS_PASSWORD

    local push_url
    push_url="$(nexus_push_url)"
    local version="${VERSION:-$(date '+%Y.%-m.%-d').1}"
    local output_dir="$SOLUTION_DIR/artifacts"

    echo
    info "=== Trino C# Nexus Deployment ==="
    info "URL     : $push_url"
    info "Version : $version"
    info "Config  : $BUILD_CONFIG"
    echo

    # 1. Run tests unless skipped
    if [[ "$SKIP_TESTS" != "1" ]]; then
        info "Running unit tests..."
        dotnet test "$SOLUTION_DIR/Trino.Client.Test/Trino.Client.Test.csproj" \
            --configuration "$BUILD_CONFIG" \
            --no-restore \
            --verbosity minimal \
            2>&1 | tail -5
        ok "Tests passed"
    else
        info "Skipping tests (SKIP_TESTS=1)"
    fi

    # 2. Clean artifacts dir
    rm -rf "$output_dir"
    mkdir -p "$output_dir"

    # 3. Build and pack each project
    info "Building and packing packages..."
    for proj in "${PUBLISH_PROJECTS[@]}"; do
        local proj_path="$SOLUTION_DIR/$proj"
        local proj_name
        proj_name="$(basename "$proj_path" .csproj)"

        info "  Packing $proj_name..."
        dotnet pack "$proj_path" \
            --configuration "$BUILD_CONFIG" \
            --output "$output_dir" \
            /p:Version="$version" \
            --verbosity minimal

        ok "  $proj_name packed"
    done

    # 4. Push each .nupkg
    info "Pushing packages to Nexus..."
    local api_key="${NEXUS_USERNAME}:${NEXUS_PASSWORD}"

    for nupkg in "$output_dir"/*.nupkg; do
        [[ -f "$nupkg" ]] || continue
        local pkg_name
        pkg_name="$(basename "$nupkg")"

        info "  Pushing $pkg_name..."
        dotnet nuget push "$nupkg" \
            --source "$push_url" \
            --api-key "$api_key" \
            --skip-duplicate \
            2>&1 | grep -v "^$" | sed 's/^/    /'

        ok "  $pkg_name pushed"
    done

    echo
    ok "=== All packages pushed successfully ==="
    info "Packages available at: $push_url"
    echo
    info "To consume from another project, run:"
    echo "    ./deploy-nexus.sh source"
    echo "  or add to nuget.config:"
    echo "    <add key=\"$SOURCE_NAME\" value=\"$(nexus_group_url)\" />"
}

cmd_source() {
    require_env NEXUS_URL NEXUS_REPO NEXUS_USERNAME NEXUS_PASSWORD

    # Try group repo first (for consuming), fall back to hosted
    local source_url
    source_url="$(nexus_group_url)"

    info "Registering NuGet source: $SOURCE_NAME"
    info "URL: $source_url"
    echo

    # Remove if already registered
    if dotnet nuget list source 2>/dev/null | grep -q "$SOURCE_NAME"; then
        info "Source '$SOURCE_NAME' already exists — removing old entry..."
        dotnet nuget remove source "$SOURCE_NAME" 2>/dev/null || true
    fi

    dotnet nuget add source "$source_url" \
        --name "$SOURCE_NAME" \
        --username "$NEXUS_USERNAME" \
        --password "$NEXUS_PASSWORD" \
        --store-password-in-clear-text

    ok "Source '$SOURCE_NAME' registered."
    echo
    info "You can now restore packages with:"
    echo "    dotnet restore --source $SOURCE_NAME"
    echo
    info "Or add to your project's nuget.config (see nuget.config.example)"
}

# ── Entry point ───────────────────────────────────────────────────────────────
CMD="${1:-}"

case "$CMD" in
    push)    cmd_push ;;
    source)  cmd_source ;;
    list)    cmd_list ;;
    *)
        echo "Usage: $0 {push|source|list}"
        echo
        echo "  push    Build Release packages and push to Nexus"
        echo "  source  Register Nexus as a local NuGet source for dotnet restore"
        echo "  list    List packages that would be pushed (no network calls)"
        echo
        echo "Environment variables:"
        echo "  NEXUS_URL        e.g. http://nexus.company.com:8081"
        echo "  NEXUS_REPO       e.g. nuget-hosted"
        echo "  NEXUS_USERNAME   Nexus login"
        echo "  NEXUS_PASSWORD   Nexus password"
        echo "  VERSION          Package version override (default: yyyy.M.d.1)"
        echo "  BUILD_CONFIG     Release (default) or Debug"
        echo "  SKIP_TESTS       Set to 1 to skip unit tests before push"
        exit 1
        ;;
esac
