FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ChatSystem.API/ChatSystem.API/*.csproj ChatSystem.API/ChatSystem.API/
COPY ChatSystem.API/ChatSystem.Application/*.csproj ChatSystem.API/ChatSystem.Application/
COPY ChatSystem.API/ChatSystem.Domain/*.csproj ChatSystem.API/ChatSystem.Domain/
COPY ChatSystem.API/ChatSystem.Infrastructure/*.csproj ChatSystem.API/ChatSystem.Infrastructure/

RUN dotnet restore ChatSystem.API/ChatSystem.API/ChatSystem.API.csproj

COPY . .

RUN dotnet publish ChatSystem.API/ChatSystem.API/ChatSystem.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ChatSystem.API.dll"]