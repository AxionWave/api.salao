# Lyra API (.NET 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Lyra.sln Directory.Build.props ./
COPY src/Lyra.Core/Lyra.Core.csproj src/Lyra.Core/
COPY src/Lyra.Application/Lyra.Application.csproj src/Lyra.Application/
COPY src/Lyra.Infrastructure/Lyra.Infrastructure.csproj src/Lyra.Infrastructure/
COPY src/Lyra.API/Lyra.API.csproj src/Lyra.API/
RUN dotnet restore
COPY src ./src
RUN dotnet publish src/Lyra.API/Lyra.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Lyra.API.dll"]
