# syntax=docker/dockerfile:1

FROM node:22-alpine AS web-build
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src
COPY IncidentFactory.sln ./
COPY src/IncidentFactory.Api/IncidentFactory.Api.csproj src/IncidentFactory.Api/
COPY src/IncidentFactory.HardScenarios/IncidentFactory.HardScenarios.csproj src/IncidentFactory.HardScenarios/
COPY tests/IncidentFactory.Tests/IncidentFactory.Tests.csproj tests/IncidentFactory.Tests/
RUN dotnet restore IncidentFactory.sln
COPY . ./
COPY --from=web-build /src/src/IncidentFactory.Api/wwwroot ./src/IncidentFactory.Api/wwwroot
RUN dotnet publish src/IncidentFactory.Api/IncidentFactory.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    IC_BASE_URL=http://host.docker.internal:5198
EXPOSE 8080
COPY --from=api-build /app/publish ./
ENTRYPOINT ["dotnet", "IncidentFactory.Api.dll"]
