# Stage 1: Build frontend
FROM oven/bun:1-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY frontend/ ./
RUN bun run build

# Stage 2: Build backend (self-contained, trimmed)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS backend-build
ARG TARGETARCH
WORKDIR /app
COPY backend/src/TransferCs.Api/TransferCs.Api.csproj backend/src/TransferCs.Api/
RUN dotnet restore backend/src/TransferCs.Api/TransferCs.Api.csproj \
    -r linux-musl-$([ "$TARGETARCH" = "amd64" ] && echo x64 || echo arm64)
COPY backend/src/TransferCs.Api/ backend/src/TransferCs.Api/
COPY --from=frontend-build /app/frontend/build/ backend/src/TransferCs.Api/wwwroot/
RUN dotnet publish backend/src/TransferCs.Api/TransferCs.Api.csproj \
    -c Release \
    -r linux-musl-$([ "$TARGETARCH" = "amd64" ] && echo x64 || echo arm64) \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:TrimMode=partial \
    -o /out

# Stage 3: Minimal alpine runtime
FROM alpine:3.21
RUN apk add --no-cache libstdc++ libgcc icu-libs \
    && addgroup -S transfercs && adduser -S transfercs -G transfercs \
    && mkdir -p /data && chown transfercs:transfercs /data
WORKDIR /app
COPY --from=backend-build /out .

USER transfercs

ENV ASPNETCORE_URLS=http://+:8080
ENV TransferCs__BasePath=/data
ENV TransferCs__TempPath=/tmp
VOLUME /data

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["./TransferCs.Api"]
