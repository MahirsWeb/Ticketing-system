# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TicketSystemTech.sln ./
COPY src/TicketSystemTech.Api/TicketSystemTech.Api.csproj src/TicketSystemTech.Api/
COPY src/TicketSystemTech.Application/TicketSystemTech.Application.csproj src/TicketSystemTech.Application/
COPY src/TicketSystemTech.Domain/TicketSystemTech.Domain.csproj src/TicketSystemTech.Domain/
COPY src/TicketSystemTech.Infrastructure/TicketSystemTech.Infrastructure.csproj src/TicketSystemTech.Infrastructure/
COPY tests/TicketSystemTech.Tests/TicketSystemTech.Tests.csproj tests/TicketSystemTech.Tests/
RUN dotnet restore TicketSystemTech.sln

COPY . .
RUN dotnet publish src/TicketSystemTech.Api/TicketSystemTech.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
# Render injects $PORT at container start; Program.cs reads it and binds Kestrel accordingly.

EXPOSE 8080
ENTRYPOINT ["dotnet", "TicketSystemTech.Api.dll"]
