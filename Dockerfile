# Bygger webben och API:t till en image där API:t också servar webben (samma origin, se Program.cs).
FROM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY api/Kuta.Api/Kuta.Api.csproj api/Kuta.Api/
RUN dotnet restore api/Kuta.Api/Kuta.Api.csproj
COPY api/Kuta.Api/ api/Kuta.Api/
RUN dotnet publish api/Kuta.Api/Kuta.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out .
COPY --from=web /src/web/dist ./wwwroot
# aspnet-imagen lyssnar på ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Kuta.Api.dll"]
