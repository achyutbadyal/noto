#!/bin/bash
# Usage: scripts/set_version.sh X.Y.Z[-prerelease]
# Sets <Version> in Directory.Build.props, the single source of the app version. Release CI refuses a tag
# that doesn't match it. Prints the follow-up git commands rather than running them.
set -euo pipefail

VERSION="${1:?usage: set_version.sh X.Y.Z[-prerelease]}"
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "error: '$VERSION' is not a semantic version (e.g. 1.2.3 or 1.2.3-beta.1)" >&2
  exit 1
fi

sed -i.bak "s|<Version>.*</Version>|<Version>$VERSION</Version>|" Directory.Build.props
rm -f Directory.Build.props.bak
grep -q "<Version>$VERSION</Version>" Directory.Build.props

echo "Version is now $VERSION. To release it:"
echo "  git commit -am \"chore: release v$VERSION\""
echo "  git tag v$VERSION"
echo "  git push origin main v$VERSION"
