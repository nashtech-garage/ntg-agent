# Custom PostgreSQL 17 image for LightRAG.
#
# Base image already bundles pgvector for vector RAG. This layer adds Apache
# AGE for graph RAG by compiling the apache/age PG17 release branch from
# source, then appending `shared_preload_libraries = 'age'` so newly initialised
# clusters auto-load the extension on startup.
#
# Pinned to linux/amd64 to match the existing AArch64-on-Mac convention used
# for the other lightrag containers in the AppHost.

FROM --platform=linux/amd64 pgvector/pgvector:0.8.2-pg17-trixie

ARG AGE_REF=release/PG17/1.7.0

USER root

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        build-essential \
        ca-certificates \
        git \
        flex \
        bison \
        postgresql-server-dev-17; \
    git clone --depth 1 --branch "${AGE_REF}" https://github.com/apache/age.git /tmp/age; \
    make -C /tmp/age PG_CONFIG=/usr/lib/postgresql/17/bin/pg_config; \
    make -C /tmp/age PG_CONFIG=/usr/lib/postgresql/17/bin/pg_config install; \
    rm -rf /tmp/age; \
    echo "shared_preload_libraries = 'age'" >> /usr/share/postgresql/postgresql.conf.sample; \
    apt-get purge -y --auto-remove \
        build-essential \
        git \
        flex \
        bison \
        postgresql-server-dev-17; \
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*
