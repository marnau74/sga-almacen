# 1. El front (React): se construye en wwwroot de la API.
FROM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# 2. La API (.NET 10), con el front dentro.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Sga.Dominio/Sga.Dominio.csproj src/Sga.Dominio/
COPY src/Sga.Aplicacion/Sga.Aplicacion.csproj src/Sga.Aplicacion/
COPY src/Sga.Infraestructura/Sga.Infraestructura.csproj src/Sga.Infraestructura/
COPY src/Sga.Api/Sga.Api.csproj src/Sga.Api/
RUN dotnet restore src/Sga.Api/Sga.Api.csproj
COPY .editorconfig ./
COPY src/ src/
COPY datos/articulos.json datos/
COPY --from=web /src/src/Sga.Api/wwwroot src/Sga.Api/wwwroot
RUN dotnet publish src/Sga.Api/Sga.Api.csproj -c Release -o /publicado --no-restore

# 3. Solo lo necesario para ejecutar, sin privilegios.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /publicado ./
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sga.Api.dll"]
