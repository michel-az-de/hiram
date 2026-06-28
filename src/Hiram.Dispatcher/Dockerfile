# syntax=docker/dockerfile:1.7
# Build from the repository root: docker build -f src/Hiram.Dispatcher/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/Hiram.Domain/Hiram.Domain.csproj src/Hiram.Domain/
COPY src/Hiram.Application/Hiram.Application.csproj src/Hiram.Application/
COPY src/Hiram.Contracts/Hiram.Contracts.csproj src/Hiram.Contracts/
COPY src/Hiram.Infrastructure/Hiram.Infrastructure.csproj src/Hiram.Infrastructure/
COPY src/Hiram.Api/Hiram.Api.csproj src/Hiram.Api/
COPY src/Hiram.Dispatcher/Hiram.Dispatcher.csproj src/Hiram.Dispatcher/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet restore src/Hiram.Dispatcher/Hiram.Dispatcher.csproj

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet publish src/Hiram.Dispatcher/Hiram.Dispatcher.csproj -c Release -o /app/publish

# The dispatcher has no HTTP surface, so it runs on the smaller runtime image. Liveness in k8s is an
# exec probe (pgrep), declared in the deployment manifest, not a Docker HEALTHCHECK.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER app

ENTRYPOINT ["dotnet", "Hiram.Dispatcher.dll"]
