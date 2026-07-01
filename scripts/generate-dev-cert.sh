#!/usr/bin/env bash
# Generates a local ASP.NET Core HTTPS development certificate into ./.certs, using the
# dockerized .NET SDK (no .NET install required on the host). Run once before
# `docker compose up`. Safe to re-run; existing cert is left untouched unless --force is
# passed through to `dotnet dev-certs`.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

mkdir -p .certs

if [ -f .certs/aspnetapp.pfx ]; then
  echo "Dev certificate already exists at .certs/aspnetapp.pfx (delete it to regenerate)."
  exit 0
fi

docker run --rm \
  -v "$PWD/.certs":/https \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet dev-certs https -ep /https/aspnetapp.pfx -p devcertpass

echo "Dev certificate written to .certs/aspnetapp.pfx"
echo "Note: this is a self-signed cert for local development only; browsers will warn until you trust it."
