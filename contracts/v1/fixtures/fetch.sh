#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  printf 'usage: %s <api-commit-sha> <destination>\n' "$0" >&2
  exit 2
fi

ref="$1"
destination="$2"
repository="${VAULT_PREVIEW_CONTRACT_REPOSITORY:-Arbixal/vault-preview-lambda}"
repository_name="${repository##*/}"
archive_root="${repository_name}-${ref}"
archive_url="https://codeload.github.com/${repository}/tar.gz/${ref}"

rm -rf "$destination"
mkdir -p "$destination"

curl --fail --silent --show-error --location "$archive_url" \
  | tar --extract --gzip --strip-components=4 --directory "$destination" \
      "$archive_root/contracts/v1/fixtures"
