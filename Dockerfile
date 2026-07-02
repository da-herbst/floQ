# === Build Stage ===
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node/npm nur für den Playwright-Chromium-Download (npx) — vor Code-Copy, damit gecacht.
RUN apt-get update && apt-get install -y --no-install-recommends nodejs npm \
    && rm -rf /var/lib/apt/lists/*
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN npx playwright@1.59.0 install chromium

COPY src/floQ.Domain/floQ.Domain.csproj ./floQ.Domain/
COPY src/floQ.Web/floQ.Web.csproj ./floQ.Web/
RUN dotnet restore floQ.Web/floQ.Web.csproj
COPY src/floQ.Domain/ ./floQ.Domain/
COPY src/floQ.Web/ ./floQ.Web/
WORKDIR /src/floQ.Web
RUN dotnet publish -c Release -o /app/publish

# === Runtime Stage ===
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Chromium-Systemabhängigkeiten für das Playwright-PDF-Rendering + Zeitzonen.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN apt-get update && apt-get install -y --no-install-recommends \
        tzdata \
        libnss3 libnspr4 libatk1.0-0t64 libatk-bridge2.0-0t64 \
        libcups2t64 libdrm2 libxkbcommon0 libxcomposite1 \
        libxdamage1 libxfixes3 libxrandr2 libgbm1 libpango-1.0-0 \
        libcairo2 libasound2t64 libatspi2.0-0t64 libxshmfence1 \
        libx11-6 libx11-xcb1 libxcb1 libxext6 libdbus-1-3 \
        fonts-liberation fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /ms-playwright /ms-playwright
EXPOSE 8083
ENV ASPNETCORE_URLS=http://+:8083
ENTRYPOINT ["dotnet", "floQ.Web.dll"]
