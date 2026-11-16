# MusicDB-CS container image.
#   build:  docker build -t musicdb:dev .
# Built on an Apple Silicon Mac this produces an ARM64 image (same as the
# Ubuntu lab VMs). For x86 servers too:
#   docker buildx build --platform linux/amd64,linux/arm64 -t <registry>/musicdb:<tag> --push .
#
# Settings come from environment variables at run time, never from the image:
#   ConnectionStrings__DefaultConnection   MySQL (required)
#   ConnectionStrings__Redis               Redis cache + shared Data Protection keys
#   ReverseProxy__Enabled=true             when behind HAProxy / an ingress
#   Database__MigrateOnStartup=false       when a separate Job runs migrations
#   MusicLibrary__Path                     defaults to /music (mount a volume there)

# ---- build stage: full .NET 10 SDK ------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first, from the project file alone, so this layer stays cached
# until the package list changes (code-only changes rebuild much faster)
COPY MusicDatabase.csproj .
RUN dotnet restore -p:Configuration=Release   # Release: skips Debug-only packages

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# ---- run stage: ASP.NET runtime only ----------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# the music library is mounted here (read-only) by compose / Kubernetes
ENV MusicLibrary__Path=/music

# .NET images listen on 8080 by default (ASPNETCORE_HTTP_PORTS)
EXPOSE 8080

# run as the image's built-in non-root user, not root
USER $APP_UID

# No HEALTHCHECK: this image has no curl/wget. Kubernetes probes
# /healthz/live and /healthz/ready over HTTP from outside the container.
ENTRYPOINT ["dotnet", "MusicDatabase.dll"]
