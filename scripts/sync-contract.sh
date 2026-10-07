#!/usr/bin/env bash
# Copies the check-in contract from the rest-o-matic repository into
# contract/checkin/v1/, unchanged, and records where it came from in
# contract/PROVENANCE.md.
#
#   scripts/sync-contract.sh [path to rest-o-matic]   (default ../rest-o-matic)
#
# Run it whenever the host changes the contract, then run the tests: they
# check this app's messages against the copied schemas and examples.
# rest-o-matic owns the contract; never edit the copy here.
set -euo pipefail

cd "$(dirname "$0")/.."
SOURCE="${1:-../rest-o-matic}"
FROM="$SOURCE/contract/checkin/v1"
TO=contract/checkin/v1

if [ ! -d "$FROM" ]; then
    echo "No contract at $FROM. Pass the path to a rest-o-matic checkout." >&2
    exit 1
fi
if [ -n "$(git -C "$SOURCE" status --porcelain -- contract)" ]; then
    echo "The contract in $SOURCE has uncommitted changes; commit them there first." >&2
    exit 1
fi

COMMIT=$(git -C "$SOURCE" log -1 --format='%h' -- contract/checkin/v1)
SUBJECT=$(git -C "$SOURCE" log -1 --format='%s' -- contract/checkin/v1)
DATE=$(git -C "$SOURCE" log -1 --format='%ad' --date=short -- contract/checkin/v1)
RELEASES=$(git -C "$SOURCE" tag --contains "$COMMIT" --sort=version:refname | head -n 1)

rm -rf "$TO"
mkdir -p "$TO"
cp -R "$FROM"/. "$TO"/

cat > contract/PROVENANCE.md <<EOF
# Where this contract comes from

\`checkin/v1/\` is a copy of \`contract/checkin/v1/\` in
[rest-o-matic](https://github.com/drewlsvern/rest-o-matic), which owns it. If
the two differ, the original wins. Do not edit the copy: change the contract
in rest-o-matic, then run \`scripts/sync-contract.sh\` here.

| | |
|---|---|
| Last changed in | \`$COMMIT\` ($DATE): $SUBJECT |
| First release with it | ${RELEASES:-none yet} |
| Copied | $(date +%Y-%m-%d) |

Keeping the copy current is manual for now. Within v1 the contract only adds
fields, which both sides ignore, so a stale copy breaks nothing; new fields
just go untested here. An incompatible change arrives as a \`v2\` folder.
EOF

echo "Copied $FROM ($COMMIT, ${RELEASES:-unreleased}) to $TO."
git status --short contract
