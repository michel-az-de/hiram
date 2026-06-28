# syntax=docker/dockerfile:1.7
# Build from the repository root: docker build -f src/Hiram.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first so restore is a cached layer that only invalidates when a csproj changes.
COPY global.json Directory.Build.props ./
COPY src/Hiram.Domain/Hiram.Domain.csproj src/Hiram.Domain/
COPY src/Hiram.Application/Hiram.Application.csproj src/Hiram.Application/
COPY src/Hiram.Contracts/Hiram.Contracts.csproj src/Hiram.Contracts/
COPY src/Hiram.Infrastructure/Hiram.Infrastructure.csproj src/Hiram.Infrastructure/
COPY src/Hiram.Dispatcher/Hiram.Dispatcher.csproj src/Hiram.Dispatcher/
COPY src/Hiram.Api/Hiram.Api.csproj src/Hiram.Api/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet restore src/Hiram.Api/Hiram.Api.csproj

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet publish src/Hiram.Api/Hiram.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet image ships a non-root app user; k8s sets fsGroup so the mounted key ring is writable.
USER app
EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Base appsettings binds Kestrel to localhost:3357 for local dev. In the container we serve on every
# interface; an env override beats appsettings so the listener is reachable from outside the pod.
ENV Kestrel__Endpoints__Http__Url=http://+:8080

ENTRYPOINT ["dotnet", "Hiram.Api.dll"]
