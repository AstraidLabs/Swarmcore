FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Tracker.CacheCoordinator/Tracker.CacheCoordinator.Worker/Tracker.CacheCoordinator.Worker.csproj"
RUN dotnet publish "src/Services/Tracker.CacheCoordinator/Tracker.CacheCoordinator.Worker/Tracker.CacheCoordinator.Worker.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Tracker.CacheCoordinator.Worker.dll"]
