FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CONFIGURATION=Release
ARG VERBOSITY=detailed

RUN apt-get -y update \
  && apt-get -y install curl \
  && curl -sL https://deb.nodesource.com/setup_24.x | bash \
  && apt-get -y install nodejs
RUN npm install -g npm

WORKDIR /workspace
COPY --parents src/**/*.*sproj ./
COPY --parents src/Frontend/package*.json ./
COPY --parents src/**/packages.lock.json ./
COPY ["Directory.*.props", "./"]
COPY ["global.json", "./"]

RUN dotnet restore src/Backend --locked-mode -v $VERBOSITY
COPY . .

RUN dotnet build src/Backend -c $CONFIGURATION -v $VERBOSITY


FROM build AS publish
RUN dotnet publish src/Backend \
  -c $CONFIGURATION \
  -o /app/publish \
  -v $VERBOSITY \
  --no-build \
  -p:UseAppHost=false


FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

HEALTHCHECK CMD curl --fail http://localhost:8080/healthz || exit 1
CMD ["dotnet", "Backend.dll"]
