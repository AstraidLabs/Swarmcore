FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "src/Services/Tracker.Gateway/Tracker.Gateway.Api/Tracker.Gateway.Api.csproj"
RUN dotnet publish "src/Services/Tracker.Gateway/Tracker.Gateway.Api/Tracker.Gateway.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080/tcp
EXPOSE 6969/udp

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Tracker.Gateway.Api.dll"]
