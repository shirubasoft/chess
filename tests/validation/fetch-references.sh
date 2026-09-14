#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$root/artifacts/stockfish"
curl --fail --location --silent --show-error \
  https://github.com/official-stockfish/Stockfish/releases/download/sf_17.1/stockfish-ubuntu-x86-64.tar \
  -o "$root/artifacts/stockfish/download.tar"
echo "4dafdd04f71e70755a327b5be258937b281e60ba87bc0a5801399908240d4a73  $root/artifacts/stockfish/download.tar" | sha256sum --check
tar -xf "$root/artifacts/stockfish/download.tar" -C "$root/artifacts/stockfish"

curl --fail --location --silent --show-error \
  https://raw.githubusercontent.com/AndyGrant/Ethereal/0e47e9b67f345c75eb965d9fb3e2493b6a11d09a/src/perft/standard.epd \
  -o "$root/artifacts/standard.epd"
echo "aede19fb39e4ce6d5d3ef15723a18a1e68b4d47dea9f26ecb0ba4533da806279  $root/artifacts/standard.epd" | sha256sum --check
