# The build stage always runs on the machine doing the build and
# cross-compiles for the target, so nothing .NET runs under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION=0.0.0-dev
WORKDIR /source

# Restore in its own layer, from only the files that decide which packages
# are needed, so a source change does not download them again.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/RestOMatic.Web/RestOMatic.Web.csproj src/RestOMatic.Web/
RUN dotnet restore src/RestOMatic.Web/RestOMatic.Web.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/RestOMatic.Web/RestOMatic.Web.csproj \
        -a $TARGETARCH -c Release --no-restore \
        -p:Version=$VERSION \
        -o /app \
    && mkdir /empty-data

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# APP_UID is not set in this file. The base image defines it (ENV APP_UID=1654)
# along with a matching non-root user named "app", and it is inherited here.

# Everything the app must not lose is under /data. The directory is copied in,
# not created with RUN, so this stage runs no commands and needs no emulation
# when the image is built for another architecture.
COPY --from=build --chown=$APP_UID:$APP_UID /empty-data /data
ENV DataDirectory=/data
VOLUME /data

# The base image's built-in non-root user. It listens on 8080 by default.
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "RestOMatic.Web.dll"]
