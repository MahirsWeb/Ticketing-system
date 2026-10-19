# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json TicketSystemTech.sln ./
COPY src/TicketSystemTech.Api/TicketSystemTech.Api.csproj src/TicketSystemTech.Api/
COPY src/TicketSystemTech.Application/TicketSystemTech.Application.csproj src/TicketSystemTech.Application/
COPY src/TicketSystemTech.Domain/TicketSystemTech.Domain.csproj src/TicketSystemTech.Domain/
COPY src/TicketSystemTech.Infrastructure/TicketSystemTech.Infrastructure.csproj src/TicketSystemTech.Infrastructure/
COPY tests/TicketSystemTech.Tests/TicketSystemTech.Tests.csproj tests/TicketSystemTech.Tests/
RUN dotnet restore TicketSystemTech.sln

COPY . .
RUN dotnet publish src/TicketSystemTech.Api/TicketSystemTech.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The application does not run as root. The base image already has an unprivileged user ("app", id in $APP_UID); the two places
# the application writes to are made its own, and are the two directories to mount a volume on, or the files are lost with
# the container: /data/uploads (ticket attachments and knowledge-base documents, kept private) and /app/wwwroot/avatars
# (profile pictures, served publicly).
RUN mkdir -p /data/uploads /app/wwwroot/avatars \
    && chown -R $APP_UID:$APP_UID /data /app/wwwroot/avatars
VOLUME ["/data", "/app/wwwroot/avatars"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    Storage__UploadsPath=/data/uploads

USER $APP_UID

# The image listens on 8080 (the .NET 8+ images default to it). Point a load balancer's health check at /health/ready.
EXPOSE 8080
ENTRYPOINT ["dotnet", "TicketSystemTech.Api.dll"]
