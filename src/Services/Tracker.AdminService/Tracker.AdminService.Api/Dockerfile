FROM node:22-alpine AS ui-build
WORKDIR /ui

COPY src/Services/Tracker.AdminService/Tracker.AdminService.Ui/package*.json ./
RUN npm ci
COPY src/Services/Tracker.AdminService/Tracker.AdminService.Ui ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
COPY --from=ui-build /Tracker.AdminService.Api/wwwroot ./src/Services/Tracker.AdminService/Tracker.AdminService.Api/wwwroot
RUN dotnet restore "src/Services/Tracker.AdminService/Tracker.AdminService.Api/Tracker.AdminService.Api.csproj"
RUN dotnet publish "src/Services/Tracker.AdminService/Tracker.AdminService.Api/Tracker.AdminService.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Tracker.AdminService.Api.dll"]
