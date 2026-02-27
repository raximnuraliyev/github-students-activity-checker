# ───── Build Stage ─────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GithubActivityChecker.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# ───── Runtime Stage ─────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "GithubActivityChecker.dll"]
