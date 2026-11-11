# syntax=docker/dockerfile:1

# The base images are pinned by digest: the tag says which release line (.NET 10), the digest says which exact image, so a rebuild
# next month cannot quietly be a different image. Dependabot (docker ecosystem, .github/dependabot.yml) proposes the new digest
# when Microsoft publishes one.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

# Only the project files first, so that the packages are restored once and cached until a project file changes. The tests are not
# part of the image (see .dockerignore), so only the application is restored, not the solution.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/TicketSystemTech.Api/TicketSystemTech.Api.csproj src/TicketSystemTech.Api/
COPY src/TicketSystemTech.Application/TicketSystemTech.Application.csproj src/TicketSystemTech.Application/
COPY src/TicketSystemTech.Domain/TicketSystemTech.Domain.csproj src/TicketSystemTech.Domain/
COPY src/TicketSystemTech.Infrastructure/TicketSystemTech.Infrastructure.csproj src/TicketSystemTech.Infrastructure/
RUN dotnet restore src/TicketSystemTech.Api/TicketSystemTech.Api.csproj

COPY . .
RUN dotnet publish src/TicketSystemTech.Api/TicketSystemTech.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS runtime
WORKDIR /app
COPY --from=build /app .

# The application does not run as root. The base image already has an unprivileged user ("app", id in $APP_UID); the places the
# application writes to are made its own, and are the directories to mount a volume on, or the files are lost with the container:
#   /data/uploads         ticket attachments and knowledge-base documents (kept private)
#   /data/logs            the rolling log files
#   /app/wwwroot/avatars  profile pictures (served publicly)
RUN mkdir -p /data/uploads /data/logs /app/wwwroot/avatars \
    && chown -R $APP_UID:$APP_UID /data /app/wwwroot/avatars
VOLUME ["/data", "/app/wwwroot/avatars"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    Storage__UploadsPath=/data/uploads \
    Logging__File__Path=/data/logs/ticketsystem-.log

USER $APP_UID

# The image listens on 8080 (the .NET 8+ images default to it). The runtime image has neither curl nor wget, so the check is the
# application itself asking /health/live (see ContainerHealthProbe). A load balancer's own check can point at /health/ready.
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD ["dotnet", "TicketSystemTech.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "TicketSystemTech.Api.dll"]
