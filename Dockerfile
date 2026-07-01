# syntax=docker/dockerfile:1

# ---- Build stage ----
# Also used directly (target: build) for local dev/test via docker-compose, so the whole
# .NET SDK toolchain (dotnet build/test/ef) can run without installing .NET on the host.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY LeaveAutopilot.sln .
COPY src/LeaveAutopilot.Web/LeaveAutopilot.Web.csproj src/LeaveAutopilot.Web/
COPY tests/LeaveAutopilot.Tests/LeaveAutopilot.Tests.csproj tests/LeaveAutopilot.Tests/
RUN dotnet restore LeaveAutopilot.sln

COPY . .
RUN dotnet publish src/LeaveAutopilot.Web/LeaveAutopilot.Web.csproj -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "LeaveAutopilot.Web.dll"]
