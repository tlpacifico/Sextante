# syntax=docker/dockerfile:1.7
# ----------------------------------------------------------------------
# Stage 1 — Angular build
#
# Replicate the repo layout so that angular.json's relative outputPath
# (../../Bootstrap/Sextante.Host/wwwroot) resolves correctly. Going
# through the angular.json config rather than `--output-path` keeps the
# `browser: ""` flattening that puts assets directly under wwwroot/.
# ----------------------------------------------------------------------
FROM node:lts-alpine AS spa-build
WORKDIR /work/src/Web/Sextante.Web

COPY src/Web/Sextante.Web/package.json src/Web/Sextante.Web/package-lock.json* ./
RUN npm ci --no-audit --no-fund

COPY src/Web/Sextante.Web/ ./
RUN node scripts/write-version.mjs \
 && npx ng build --configuration=production
# Angular emits to /work/src/Bootstrap/Sextante.Host/wwwroot/

# ----------------------------------------------------------------------
# Stage 2 — .NET publish
# ----------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Sextante.slnx ./
COPY src/ ./src/
COPY tests/ ./tests/

# Pre-warm restore for the Host
RUN dotnet restore src/Bootstrap/Sextante.Host/Sextante.Host.csproj

# Skip Angular MSBuild target — already produced in stage 1
RUN dotnet publish src/Bootstrap/Sextante.Host/Sextante.Host.csproj \
        -c Release \
        --no-restore \
        -p:SkipAngularBuild=true \
        -o /publish

# Drop Angular bundle into the published wwwroot
COPY --from=spa-build /work/src/Bootstrap/Sextante.Host/wwwroot /publish/wwwroot

# ----------------------------------------------------------------------
# Stage 3 — runtime
# ----------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="http://+:80;https://+:443" \
    DOTNET_RUNNING_IN_CONTAINER=true

# Volume mount points (declared via docker-compose)
RUN mkdir -p /var/letsencrypt-certs /app/logs

COPY --from=dotnet-build /publish ./

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "Sextante.Host.dll"]
